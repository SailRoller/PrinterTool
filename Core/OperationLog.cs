using System;
using System.IO;

namespace PrinterTool.Core;

/// <summary>全量操作日志：写入 exe 目录 logs/yyyy-MM-dd.log，同时向订阅者（界面/CLI）推送。</summary>
public static class OperationLog
{
    private static readonly object Gate = new();

    public static event Action<string>? MessageAdded;

    public static string LogDirectory => Path.Combine(AppContext.BaseDirectory, "logs");

    private static string LogFile => Path.Combine(LogDirectory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogFile, line + Environment.NewLine);
            }
            catch
            {
                // 日志写失败不应影响主流程
            }
        }
        MessageAdded?.Invoke(line);
    }
}
