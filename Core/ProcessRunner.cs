using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PrinterTool.Core;

/// <summary>运行系统命令并捕获输出（UTF-8 优先、ANSI 代码页兜底，兼容中英文系统的本地化输出）。</summary>
public static class ProcessRunner
{
    static ProcessRunner()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* 忽略 */ }
    }

    public static (int ExitCode, string Output) Run(string fileName, string arguments, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return (-1, "");
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.StandardError.BaseStream.CopyTo(ms);
        p.WaitForExit(timeoutMs);
        var code = p.HasExited ? p.ExitCode : -1;

        var bytes = ms.ToArray();
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try { text = Encoding.GetEncoding(0).GetString(bytes); }
            catch { text = Encoding.UTF8.GetString(bytes); }
        }
        return (code, text.Trim());
    }
}
