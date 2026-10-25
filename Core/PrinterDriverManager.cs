using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using PrinterTool.Native;

namespace PrinterTool.Core;

/// <summary>重点三：打印机/驱动枚举、占用分析、彻底卸载。</summary>
public static class PrinterDriverManager
{
    private const string PrintRegistryRoot = @"SYSTEM\CurrentControlSet\Control\Print";
    private const string PrintersRegistryPath = @"SYSTEM\CurrentControlSet\Control\Print\Printers";

    /// <summary>需要枚举的驱动环境：64 位系统上同时枚举 x64 与 x86(WOW64) 两套 v3 驱动。</summary>
    public static List<string> DriverEnvironments()
    {
        var current = Environment.Is64BitProcess ? "Windows x64" : "Windows NT x86";
        var list = new List<string> { current };
        if (Environment.Is64BitOperatingSystem)
            list.Add(current == "Windows x64" ? "Windows NT x86" : "Windows x64");
        return list;
    }

    public static List<PrinterItem> GetPrinters()
    {
        var list = new List<PrinterItem>();
        foreach (var p in Winspool.EnumAllPrinters())
        {
            list.Add(new PrinterItem
            {
                Name = p.pPrinterName ?? "(未知)",
                DriverName = p.pDriverName ?? "",
                PortName = p.pPortName ?? "",
                ShareName = p.pShareName ?? "",
                IsConnection = (p.Attributes & Winspool.PRINTER_ATTRIBUTE_NETWORK) != 0,
                IsShared = (p.Attributes & Winspool.PRINTER_ATTRIBUTE_SHARED) != 0,
                JobCount = p.cJobs,
                StatusText = FormatPrinterStatus(p.Status),
            });
        }
        return list;
    }

    public static List<DriverItem> GetDrivers()
    {
        var printers = GetPrinters();
        var refsByDriver = GetAllRegistryPrinterReferences();
        var result = new List<DriverItem>();
        foreach (var env in DriverEnvironments())
        {
            List<Winspool.DRIVER_INFO_3> raw;
            try
            {
                raw = Winspool.EnumAllDrivers(env);
            }
            catch (Exception ex)
            {
                OperationLog.Warn($"枚举 {env} 环境驱动失败: {ex.Message}");
                continue;
            }
            foreach (var d in raw)
            {
                var name = d.pName ?? "";
                var item = new DriverItem
                {
                    Name = name,
                    Environment = env,
                    Version = (int)d.cVersion,
                    DriverPath = d.pDriverPath ?? "",
                    DataFile = d.pDataFile ?? "",
                    ConfigFile = d.pConfigFile ?? "",
                    DependentFiles = d.pDependentFiles ?? "",
                };
                item.Printers = printers
                    .Where(p => string.Equals(p.DriverName, name, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Name)
                    .ToList();
                if (refsByDriver.TryGetValue(name, out var refs))
                    item.HiddenReferences = refs
                        .Where(r => !printers.Any(p => string.Equals(p.Name, r, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                result.Add(item);
            }
        }
        return result;
    }

    /// <summary>扫描 Print\Printers 注册表，按 "Printer Driver" 值归类全部打印机项（含 EnumPrinters 看不到的）。</summary>
    private static Dictionary<string, List<string>> GetAllRegistryPrinterReferences()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using var root = Registry.LocalMachine.OpenSubKey(PrintersRegistryPath);
        if (root is null) return map;
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("Printer Driver") is string drv && !string.IsNullOrWhiteSpace(drv))
            {
                if (!map.TryGetValue(drv, out var list))
                    map[drv] = list = new List<string>();
                list.Add(name);
            }
        }
        return map;
    }

    /// <summary>当前仍引用指定驱动的打印机注册表项（实时扫描，含隐藏/残留/待删除项）。</summary>
    public static List<string> GetRegistryPrinterReferences(string driverName)
    {
        var result = new List<string>();
        using var root = Registry.LocalMachine.OpenSubKey(PrintersRegistryPath);
        if (root is null) return result;
        foreach (var name in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("Printer Driver") is string drv &&
                string.Equals(drv, driverName, StringComparison.OrdinalIgnoreCase))
                result.Add(name);
        }
        return result;
    }

    /// <summary>删除单个打印机实例：网络连接走 DeletePrinterConnection，本地走 OpenPrinter+DeletePrinter。</summary>
    public static void DeletePrinterInstance(PrinterItem printer, Action<string> log)
    {
        if (printer.IsConnection)
        {
            log($"移除网络打印机连接: {printer.Name}");
            if (!Winspool.TryDeletePrinterConnection(printer.Name))
                throw new Win32Exception();
        }
        else
        {
            log($"删除本地打印机: {printer.Name}");
            Winspool.DeleteLocalPrinter(printer.Name);
        }
    }

    /// <summary>
    /// 彻底卸载驱动（升级阶梯，专治“驱动程序当前正在使用”）：
    /// 1. 备份注册表并删除所有可见的关联打印机实例；
    /// 2. 重启打印服务——让“待删除”打印机落地、卸载已加载的驱动 DLL；
    /// 3. DeletePrinterDriverEx；
    /// 4. 若仍被占用：停服务 → 备份并删除仍引用该驱动的隐藏/残留注册表打印机项 →
    ///    清空队列文件（防止待删除打印机被卡住的任务“复活”）→ 启服务 → 重试；
    /// 5. 若仍被占用：findstr 在 INF 目录定位驱动包 → pnputil /delete-driver /uninstall /force 强制卸载 → 重试；
    /// 6. 仍失败则抛出带处置建议的异常。
    /// </summary>
    public static void DeleteDriver(DriverItem driver, bool removePrinters, bool restartSpoolerAfter, Action<string> log)
    {
        var driverName = driver.Name;
        var printers = GetPrinters()
            .Where(p => string.Equals(p.DriverName, driverName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (printers.Count > 0 && !removePrinters)
            throw new InvalidOperationException($"驱动正被 {printers.Count} 台打印机使用，请选择同时移除打印机。");

        // 1) 删除可见打印机实例
        foreach (var p in printers)
        {
            BackupManager.ExportRegistryKey($@"HKLM\{PrintersRegistryPath}\{p.Name}", $"printer-{p.Name}");
            DeletePrinterInstance(p, log);
        }

        var driverKey = $@"HKLM\{PrintRegistryRoot}\Environments\{driver.Environment}\Drivers\Version-{driver.Version}\{driverName}";
        BackupManager.ExportRegistryKey(driverKey, $"driver-{driverName}");

        // 2) 重启服务：使打印机删除生效并释放已加载的驱动 DLL
        log("重启打印服务，使打印机删除生效并释放驱动 ...");
        SpoolerController.RestartSpooler(false, log);

        // 3) 首次尝试删除驱动
        log($"调用系统 API 删除驱动: {driverName}（{driver.Environment}, v{driver.Version}）...");
        if (Winspool.TryDeletePrinterDriver(driver.Environment, driverName))
        {
            FinishDriverDeletion(driver, printers.Count, log);
            return;
        }
        var firstError = new Win32Exception();
        log($"首次删除失败：{firstError.Message}");

        // 4) 清理仍引用该驱动的隐藏/残留注册表打印机项，并清空队列文件
        var refs = GetRegistryPrinterReferences(driverName);
        if (refs.Count > 0)
        {
            log($"发现 {refs.Count} 个仍引用该驱动的打印机注册表项（隐藏/残留/待删除）：");
            foreach (var r in refs) log("  • " + r);
        }
        else
        {
            log("无注册表打印机项残留，改为清空队列文件（卡住待删除打印机的任务文件）...");
        }

        ServiceHelper.Stop(SpoolerController.SpoolerName, 30000);
        try
        {
            foreach (var r in refs)
            {
                BackupManager.ExportRegistryKey($@"HKLM\{PrintersRegistryPath}\{r}", $"printer-ref-{r}");
                Registry.LocalMachine.DeleteSubKeyTree($@"{PrintersRegistryPath}\{r}", throwOnMissingSubKey: false);
                log($"已删除注册表打印机项: {r}");
            }
            var (deleted, _, _) = SpoolerController.ClearSpoolFiles();
            log($"已清空队列文件 {deleted} 个。");
        }
        finally
        {
            ServiceHelper.Start(SpoolerController.SpoolerName, 30000);
        }

        // 5) 重试
        log("重试删除驱动 ...");
        if (Winspool.TryDeletePrinterDriver(driver.Environment, driverName))
        {
            FinishDriverDeletion(driver, printers.Count, log);
            return;
        }
        var secondError = new Win32Exception();
        log($"第二次删除失败：{secondError.Message}");

        // 6) pnputil 兜底：在 INF 目录按驱动名定位驱动包并强制卸载
        log("尝试通过驱动存储（pnputil）强制卸载驱动包 ...");
        if (TryForceRemoveDriverPackage(driverName, log))
        {
            if (Winspool.TryDeletePrinterDriver(driver.Environment, driverName))
            {
                FinishDriverDeletion(driver, printers.Count, log);
                return;
            }
            var thirdError = new Win32Exception();
            log("驱动包已强制卸载，但驱动注册表项仍需清理。");
            throw new Win32Exception(thirdError.NativeErrorCode, BuildInUseGuidance(thirdError));
        }

        throw new Win32Exception(secondError.NativeErrorCode, BuildInUseGuidance(secondError));
    }

    private static void FinishDriverDeletion(DriverItem driver, int removedPrinters, Action<string> log)
    {
        log("驱动删除成功。");
        OperationLog.Info($"已删除驱动 {driver.Name}（{driver.Environment} v{driver.Version}），同时移除打印机 {removedPrinters} 台。");
    }

    private static string BuildInUseGuidance(Win32Exception cause)
    {
        return $"删除驱动失败：{cause.Message}（已尝试：删除打印机和隐藏引用、清空队列、重启服务、强制卸载驱动包）。\n" +
               "建议按以下顺序处置：\n" +
               "1) 重启电脑后、其他程序使用打印机前，立即重试删除；\n" +
               "2) 注销其他已登录用户（其网络打印机连接可能占用该驱动）后重试；\n" +
               "3) 退出打印机制造商常驻软件（如 HP Smart / 打印机状态监视器）后重试；\n" +
               "4) 仍失败时可在“残留清理”页删除该驱动的注册表项后重启服务（系统会自动备份）。";
    }

    /// <summary>在 C:\Windows\INF\oem*.inf 中按驱动名定位驱动包（oem##.inf 即 pnputil 发布名），强制卸载。</summary>
    private static bool TryForceRemoveDriverPackage(string driverName, Action<string> log)
    {
        try
        {
            var infDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
            var psi = new ProcessStartInfo("findstr.exe", $"/s /i /m /c:\"{driverName}\" \"{infDir}\\oem*.inf\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30000);

            var infs = output
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (infs.Count == 0)
            {
                log("未在 INF 目录中找到包含该驱动名的 oem 驱动包，跳过 pnputil 兜底。");
                return false;
            }

            // 只对打印机类的包执行强制卸载，避免误伤
            var printerPackages = DriverPackageManager.EnumPrinterPackages()
                .Select(x => x.PublishedName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var targets = infs
                .Select(f => Path.GetFileName(f) ?? "")
                .Where(f => f.Length > 0 && printerPackages.Contains(f))
                .ToList();
            if (targets.Count == 0)
            {
                log($"匹配到的 INF（{string.Join(", ", infs.Select(Path.GetFileName))}）不在打印机类驱动包列表中，跳过强制卸载。");
                return false;
            }

            bool anySuccess = false;
            foreach (var published in targets)
            {
                log($"pnputil /delete-driver {published} /uninstall /force ...");
                var (code, pkgOutput) = DriverPackageManager.DeletePackageEx(published);
                log($"pnputil 输出（exit={code}）: {pkgOutput.Replace("\n", " ").Trim()}");
                anySuccess |= code == 0;
            }
            return anySuccess;
        }
        catch (Exception ex)
        {
            log("pnputil 兜底执行异常: " + ex.Message);
            return false;
        }
    }

    internal static string FormatPrinterStatus(uint status)
    {
        if (status == 0) return "正常";
        var parts = new List<string>();
        void Add(uint bit, string text)
        {
            if ((status & bit) != 0) parts.Add(text);
        }
        Add(0x2, "错误"); Add(0x8, "卡纸"); Add(0x10, "缺纸"); Add(0x40, "纸张问题");
        Add(0x80, "脱机"); Add(0x400, "打印中"); Add(0x800, "出纸槽已满"); Add(0x1000, "不可用");
        Add(0x20000, "碳粉不足"); Add(0x40000, "无碳粉"); Add(0x100000, "需要人工干预");
        Add(0x200000, "内存不足"); Add(0x400000, "门未关"); Add(0x1, "已暂停"); Add(0x4, "待删除");
        return parts.Count == 0 ? "状态码:" + status : string.Join(", ", parts);
    }
}
