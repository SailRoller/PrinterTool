using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace PrinterTool.Core;

/// <summary>
/// 重点三·残留清理：扫描 HKLM\SYSTEM\CurrentControlSet\Control\Print\Environments 下的驱动注册表项。
/// - Version-3 键若枚举不到对应驱动（API 视角已不存在）→ 孤儿残留，建议清理；
/// - Version-4 键（v4 类驱动，不走 v3 API）若无任何打印机关联 → 疑似残留，由用户判断。
/// </summary>
public static class ResidualScanner
{
    private const string EnvironmentsPath = @"SYSTEM\CurrentControlSet\Control\Print\Environments";

    public static List<ResidualItem> Scan()
    {
        var result = new List<ResidualItem>();

        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in PrinterDriverManager.GetDrivers())
            installed.Add(d.Environment + "\\" + d.Name);

        var printerDrivers = new HashSet<string>(
            PrinterDriverManager.GetPrinters().Select(p => p.DriverName),
            StringComparer.OrdinalIgnoreCase);

        using var envRoot = Registry.LocalMachine.OpenSubKey(EnvironmentsPath);
        if (envRoot is null) return result;

        foreach (var env in envRoot.GetSubKeyNames())
        {
            using var driversKey = envRoot.OpenSubKey(env + @"\Drivers");
            if (driversKey is null) continue;

            foreach (var versionKey in driversKey.GetSubKeyNames())
            {
                using var versionKeyObj = driversKey.OpenSubKey(versionKey);
                if (versionKeyObj is null) continue;

                foreach (var name in versionKeyObj.GetSubKeyNames())
                {
                    var full = $@"{EnvironmentsPath}\{env}\Drivers\{versionKey}\{name}";
                    if (versionKey.StartsWith("Version-3", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!installed.Contains(env + "\\" + name))
                            result.Add(new ResidualItem
                            {
                                Name = name,
                                Environment = env,
                                VersionKey = versionKey,
                                RegistryPath = full,
                                Kind = ResidualKind.OrphanV3,
                            });
                    }
                    else
                    {
                        if (!printerDrivers.Contains(name))
                            result.Add(new ResidualItem
                            {
                                Name = name,
                                Environment = env,
                                VersionKey = versionKey,
                                RegistryPath = full,
                                Kind = ResidualKind.SuspectV4,
                            });
                    }
                }
            }
        }
        return result;
    }

    /// <summary>清理单项残留：先 reg export 备份，再删除注册表键。清理后建议重启打印服务。</summary>
    public static void Delete(ResidualItem item, Action<string> log)
    {
        BackupManager.ExportRegistryKey(@"HKLM\" + item.RegistryPath, "residual-" + item.Name);
        log($"删除注册表项: HKLM\\{item.RegistryPath}");
        Registry.LocalMachine.DeleteSubKeyTree(item.RegistryPath, throwOnMissingSubKey: false);
        OperationLog.Info($"已清理驱动残留注册表项: {item.Name}（{item.Environment}\\{item.VersionKey}）");
    }
}
