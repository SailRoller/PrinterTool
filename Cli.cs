using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PrinterTool.Core;

namespace PrinterTool;

/// <summary>命令行模式：供脚本 / 批量处理 / 自动化使用（也为后续 GPO 部署预留）。</summary>
internal static class Cli
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr hFile);

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    private const int STD_OUTPUT_HANDLE = -11;

    public static int Run(string[] args)
    {
        // 仅当标准输出不是管道/文件（即从 cmd 交互启动 WinExe）时才挂接父控制台，
        // 否则 AttachConsole 会让 Console.Out 偏离已继承的重定向句柄
        try
        {
            var h = GetStdHandle(STD_OUTPUT_HANDLE);
            if (h != IntPtr.Zero && h != (IntPtr)(-1))
            {
                var type = GetFileType(h); // 1=disk file, 2=char(console), 3=pipe
                if (type != 1 && type != 3)
                    AttachConsole(ATTACH_PARENT_PROCESS);
            }
            else
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
            }
        }
        catch { /* 忽略 */ }
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 忽略 */ }

        OperationLog.MessageAdded += line => Console.WriteLine(line);
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "--help" or "-h" or "/?" => PrintHelp(),
                "--list-services" => ListServices(),
                "--list-printers" => ListPrinters(),
                "--list-drivers" => ListDrivers(),
                "--list-packages" => ListPackages(),
                "--scan-residuals" => ScanResiduals(),
                "--restart-spooler" => RestartSpooler(args),
                "--delete-driver" => DeleteDriver(args),
                "--share-printer" => SharePrinterCli(args),
                "--unshare-printer" => UnsharePrinterCli(args),
                "--reconnect" => ReconnectCli(args),
                "--fix" => FixCli(args),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine("错误: " + ex.Message);
            return 1;
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine("打印机管理工具（内部版）命令行模式");
        Console.WriteLine("用法: PrinterTool.exe <命令> [参数]");
        Console.WriteLine();
        Console.WriteLine("命令:");
        Console.WriteLine("  --list-services                          列出打印相关服务状态");
        Console.WriteLine("  --list-printers                          列出本机打印机及驱动占用");
        Console.WriteLine("  --list-drivers                           列出已安装的打印机驱动");
        Console.WriteLine("  --list-packages                          列出驱动存储中的打印机类驱动包");
        Console.WriteLine("  --scan-residuals                         扫描驱动注册表残留");
        Console.WriteLine("  --restart-spooler [--no-clear]           重启打印服务（默认同时清空队列文件）");
        Console.WriteLine("  --delete-driver --name <驱动名> [--env <环境>]");
        Console.WriteLine("                    [--keep-printers] [--no-restart]");
        Console.WriteLine("                                           彻底删除驱动（默认同时删除关联打印机并重启服务）");
        Console.WriteLine("  --share-printer --name <打印机名> [--allow-guest]");
        Console.WriteLine("                                           本机一键共享（共享+网络发现服务+防火墙+可选来宾）");
        Console.WriteLine("  --unshare-printer --name <打印机名>      取消共享");
        Console.WriteLine("  --reconnect \\\\主机\\打印机 [--no-autofix]  一键诊断重连（失败自动按 11b/709/bc4 修复重试）");
        Console.WriteLine("  --fix rpc|discovery|guest                应用常见预案（RPC 认证/网络发现/来宾访问）");
        return 0;
    }

    private static int Unknown(string cmd)
    {
        Console.WriteLine("未知命令: " + cmd);
        PrintHelp();
        return 2;
    }

    private static int ListServices()
    {
        foreach (var (name, display) in WatchedServices.All)
            Console.WriteLine($"{name,-10} {ServiceHelper.StateText(ServiceHelper.GetState(name)),-8} {display}");
        return 0;
    }

    private static int ListPrinters()
    {
        var printers = PrinterDriverManager.GetPrinters();
        Console.WriteLine($"共 {printers.Count} 台打印机:");
        foreach (var p in printers)
        {
            var shared = p.IsShared && !string.IsNullOrEmpty(p.ShareName) ? $" 共享={p.ShareName}" : "";
            Console.WriteLine($"  {p.Name,-40} 驱动={p.DriverName} 端口={p.PortName} 类型={(p.IsConnection ? "网络连接" : "本地")} 任务={p.JobCount}{shared} 状态={p.StatusText}");
        }
        return 0;
    }

    private static int ListDrivers()
    {
        var drivers = PrinterDriverManager.GetDrivers();
        Console.WriteLine($"共 {drivers.Count} 个驱动:");
        foreach (var d in drivers)
        {
            var hidden = d.HiddenReferences.Count > 0 ? $"  隐藏引用x{d.HiddenReferences.Count}" : "";
            Console.WriteLine($"  {d.Name,-50} {d.Environment,-16} v{d.Version}  关联打印机x{d.Printers.Count}{hidden}");
        }
        return 0;
    }

    private static int ListPackages()
    {
        try
        {
            var packages = DriverPackageManager.EnumPrinterPackages();
            Console.WriteLine($"驱动存储中共 {packages.Count} 个打印机类驱动包:");
            foreach (var p in packages)
                Console.WriteLine($"  {p.PublishedName,-14} {p.OriginalName,-22} {p.Provider,-20} {p.Version}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("枚举驱动存储包失败（通常需要管理员权限）: " + ex.Message);
            return 1;
        }
        return 0;
    }

    private static int ScanResiduals()
    {
        var items = ResidualScanner.Scan();
        if (items.Count == 0)
        {
            Console.WriteLine("未发现驱动注册表残留。");
            return 0;
        }
        foreach (var i in items)
            Console.WriteLine($"  [{(i.Recommended ? "建议清理" : "疑似")}] {i.Environment}\\{i.VersionKey}\\{i.Name}");
        return 0;
    }

    private static int RestartSpooler(string[] args)
    {
        bool clear = !HasFlag(args, "--no-clear");
        SpoolerController.RestartSpooler(clear, m => OperationLog.Info(m));
        Console.WriteLine("完成。");
        return 0;
    }

    private static int DeleteDriver(string[] args)
    {
        var name = ValueOf(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("缺少 --name 参数。");
            return 2;
        }
        var env = ValueOf(args, "--env");
        var drivers = PrinterDriverManager.GetDrivers()
            .Where(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (env is not null)
            drivers = drivers.Where(d => string.Equals(d.Environment, env, StringComparison.OrdinalIgnoreCase)).ToList();
        if (drivers.Count == 0)
        {
            Console.WriteLine($"未找到驱动“{name}”。当前已安装驱动:");
            ListDrivers();
            return 1;
        }
        if (drivers.Count > 1)
        {
            Console.WriteLine("多个环境存在同名驱动，请用 --env 指定以下之一:");
            foreach (var d in drivers) Console.WriteLine("  --env \"" + d.Environment + "\"");
            return 2;
        }

        bool removePrinters = !HasFlag(args, "--keep-printers");
        bool restart = !HasFlag(args, "--no-restart");
        PrinterDriverManager.DeleteDriver(drivers[0], removePrinters, restart, m => OperationLog.Info(m));
        Console.WriteLine("完成。");
        return 0;
    }

    private static int SharePrinterCli(string[] args)
    {
        var name = ValueOf(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("缺少 --name 参数。");
            return 2;
        }
        bool allowGuest = HasFlag(args, "--allow-guest");
        var result = ShareHost.SharePrinter(name, allowGuest, m => OperationLog.Info(m));
        Console.WriteLine(result.SharePath);
        Console.WriteLine(result.Summary);
        return 0;
    }

    private static int UnsharePrinterCli(string[] args)
    {
        var name = ValueOf(args, "--name");
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.WriteLine("缺少 --name 参数。");
            return 2;
        }
        ShareHost.UnsharePrinter(name, m => OperationLog.Info(m));
        Console.WriteLine("已取消共享: " + name);
        return 0;
    }

    private static int ReconnectCli(string[] args)
    {
        var unc = args.Skip(1).FirstOrDefault(a => !a.StartsWith("-"));
        if (string.IsNullOrWhiteSpace(unc))
        {
            Console.WriteLine("用法: PrinterTool.exe --reconnect \\\\主机名\\打印机名 [--no-autofix]");
            return 2;
        }
        ShareClient.Reconnect(unc, autoFix: !HasFlag(args, "--no-autofix"), m => OperationLog.Info(m));
        Console.WriteLine("完成。");
        return 0;
    }

    private static int FixCli(string[] args)
    {
        var kind = args.Skip(1).FirstOrDefault(a => !a.StartsWith("-"));
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "rpc":
                ShareFixes.FixRpcAuthLevel(m => OperationLog.Info(m));
                return 0;
            case "discovery":
                ShareFixes.FixNetworkDiscovery(m => OperationLog.Info(m));
                return 0;
            case "guest":
                ShareFixes.FixGuestAccess(m => OperationLog.Info(m));
                return 0;
            default:
                Console.WriteLine("用法: PrinterTool.exe --fix rpc|discovery|guest");
                return 2;
        }
    }

    private static string? ValueOf(string[] args, string name)    {
        for (int i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}

