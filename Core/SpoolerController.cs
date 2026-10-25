using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace PrinterTool.Core;

/// <summary>重点二：Print Spooler 标准化重启与打印队列（卡死任务）清理。</summary>
public static class SpoolerController
{
    public const string SpoolerName = "Spooler";

    /// <summary>真实 Spool 目录：优先读注册表 DefaultSpoolDirectory，否则用系统默认路径。</summary>
    public static string GetSpoolDirectory()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers");
            if (key?.GetValue("DefaultSpoolDirectory") is string s && !string.IsNullOrWhiteSpace(s))
                return Environment.ExpandEnvironmentVariables(s);
        }
        catch
        {
            // 读取失败时退回默认路径
        }
        return Path.Combine(Environment.SystemDirectory, "spool", "PRINTERS");
    }

    public static List<SpoolFileItem> GetSpoolFiles()
    {
        var list = new List<SpoolFileItem>();
        try
        {
            var dir = new DirectoryInfo(GetSpoolDirectory());
            if (!dir.Exists) return list;
            foreach (var f in dir.GetFiles())
                list.Add(new SpoolFileItem { FileName = f.Name, Length = f.Length, LastWriteTime = f.LastWriteTime });
        }
        catch (Exception ex)
        {
            OperationLog.Warn("读取 Spool 目录失败: " + ex.Message);
        }
        return list;
    }

    /// <summary>清空队列目录（必须在服务停止后调用）。带一次重试以应对句柄延迟释放。</summary>
    public static (int Deleted, int Failed, string Directory) ClearSpoolFiles()
    {
        int deleted = 0, failed = 0;
        var dir = GetSpoolDirectory();
        if (!Directory.Exists(dir)) return (0, 0, dir);

        foreach (var file in Directory.GetFiles(dir))
        {
            if (TryDelete(file)) { deleted++; continue; }
            Thread.Sleep(300);
            if (TryDelete(file)) { deleted++; continue; }
            failed++;
            OperationLog.Warn($"删除队列文件失败: {file}");
        }
        return (deleted, failed, dir);
    }

    private static bool TryDelete(string file)
    {
        try { File.Delete(file); OperationLog.Info("已删除队列文件: " + file); return true; }
        catch { return false; }
    }

    /// <summary>标准修复流程：停止服务 →（可选）清空队列目录 → 启动服务 → 验证。</summary>
    public static void RestartSpooler(bool clearSpool, Action<string> log)
    {
        var state = ServiceHelper.GetState(SpoolerName);
        log($"当前 {SpoolerName} 状态: {ServiceHelper.StateText(state)}");
        if (state == SvcState.NotFound)
            throw new InvalidOperationException("未找到 Print Spooler 服务。");

        if (state != SvcState.Stopped)
        {
            log("正在停止 Print Spooler 服务 ...");
            ServiceHelper.Stop(SpoolerName, 30000);
            log("服务已停止。");
        }

        if (clearSpool)
        {
            var (deleted, failed, dir) = ClearSpoolFiles();
            log($"已清理队列目录 {dir}：删除 {deleted} 个文件，失败 {failed} 个。");
        }

        log("正在启动 Print Spooler 服务 ...");
        ServiceHelper.Start(SpoolerName, 30000);
        log($"服务已启动，当前状态: {ServiceHelper.StateText(ServiceHelper.GetState(SpoolerName))}。");
        OperationLog.Info("Print Spooler 重启流程完成" + (clearSpool ? "（含队列清理）" : "") + "。");
    }
}
