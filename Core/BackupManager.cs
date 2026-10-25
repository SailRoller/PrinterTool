using System;
using System.Diagnostics;
using System.IO;

namespace PrinterTool.Core;

/// <summary>注册表修改前的自动备份（reg export），备份文件按操作时间归档在 exe 目录 backups/ 下。</summary>
public static class BackupManager
{
    public static string BackupRoot => Path.Combine(AppContext.BaseDirectory, "backups");

    /// <summary>导出注册表项；成功返回备份文件路径，失败返回 null（不阻断主流程，但会记日志）。</summary>
    public static string? ExportRegistryKey(string registryKeyPath, string fileHint)
    {
        try
        {
            var dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, Sanitize(fileHint) + ".reg");
            var psi = new ProcessStartInfo("reg.exe", $"export \"{registryKeyPath}\" \"{file}\" /y")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            p.WaitForExit(15000);
            if (p.HasExited && p.ExitCode == 0 && File.Exists(file))
            {
                OperationLog.Info($"已备份注册表项 {registryKeyPath} → {file}");
                return file;
            }
            OperationLog.Warn($"注册表备份失败 (exit={(p.HasExited ? p.ExitCode.ToString() : "timeout")}): {registryKeyPath}");
            return null;
        }
        catch (Exception ex)
        {
            OperationLog.Warn($"注册表备份异常: {registryKeyPath} ({ex.Message})");
            return null;
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 80 ? name.Substring(0, 80) : name;
    }
}
