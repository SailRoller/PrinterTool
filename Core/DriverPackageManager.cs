using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PrinterTool.Core;

/// <summary>
/// 驱动存储（Driver Store）中的打印机类驱动包管理，基于 pnputil。
/// 用于 DeletePrinterDriverEx 之外的“最后一公里”：清除 INF 驱动包本体，为重装驱动扫清障碍。
/// </summary>
public static class DriverPackageManager
{
    private const string PrinterClassGuid = "4d36e979-e325-11ce-bfc1-08002be10318";

    static DriverPackageManager()
    {
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* 忽略 */ }
    }

    public static List<DriverPackageItem> EnumPrinterPackages()
    {
        var output = RunPnputil("/enum-drivers", out _);
        var packages = new List<DriverPackageItem>();
        if (output is null) return packages;

        var block = new List<string>();
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                FlushBlock(block, packages);
                block.Clear();
            }
            else
            {
                block.Add(line);
            }
        }
        FlushBlock(block, packages);
        return packages;
    }

    /// <summary>删除驱动存储包：pnputil /delete-driver &lt;发布名&gt; /uninstall /force。返回退出码与输出。</summary>
    public static (int ExitCode, string Output) DeletePackageEx(string publishedName)
    {
        var output = RunPnputil($"/delete-driver {publishedName} /uninstall /force", out var exitCode);
        OperationLog.Info($"pnputil 删除驱动包 {publishedName}，exit={exitCode}，输出:\n{output}");
        return (exitCode, output is null ? "（无输出）" : output);
    }

    /// <summary>删除驱动存储包，返回格式化输出供界面展示。</summary>
    public static string DeletePackage(string publishedName)
    {
        var (code, output) = DeletePackageEx(publishedName);
        return $"exit={code}\n{output}";
    }

    private static void FlushBlock(List<string> block, List<DriverPackageItem> packages)
    {
        if (block.Count == 0) return;
        var parsed = ParseBlock(block);
        if (parsed is not null) packages.Add(parsed);
    }

    /// <summary>
    /// pnputil 输出会随系统语言本地化（标签是中文/英文），因此不做标签匹配：
    /// - 用打印机类 GUID 识别块；
    /// - 按出现顺序取第 1/第 2 个 *.inf 值作为发布名/原始名；
    /// - 用与语言无关的“日期+版本号”模式识别驱动版本行。
    /// </summary>
    private static DriverPackageItem? ParseBlock(List<string> block)
    {
        var values = new List<string>();
        foreach (var line in block)
        {
            var idx = line.IndexOf(':');
            if (idx > 0 && idx < line.Length - 1)
                values.Add(line.Substring(idx + 1).Trim());
        }
        if (values.Count == 0) return null;
        if (!values.Any(v => v.ToLowerInvariant().Contains(PrinterClassGuid))) return null;

        var infs = values
            .Where(v => Regex.IsMatch(v, @"^[A-Za-z0-9_\-.]+\.inf$", RegexOptions.IgnoreCase))
            .ToList();
        var item = new DriverPackageItem
        {
            PublishedName = infs.Count > 0 ? infs[0] : "",
            OriginalName = infs.Count > 1 ? infs[1] : "",
        };
        item.Version = values.FirstOrDefault(v => Regex.IsMatch(v, @"^\d{2}/\d{2}/\d{4}\s+\d+(\.\d+)+$")) ?? "";
        item.Provider = values.FirstOrDefault(v =>
            !v.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)
            && !v.ToLowerInvariant().Contains(PrinterClassGuid)
            && !Regex.IsMatch(v, @"^\d{2}/\d{2}/\d{4}")) ?? "";
        return item;
    }

    private static string? RunPnputil(string arguments, out int exitCode)
    {
        var psi = new ProcessStartInfo("pnputil.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        if (p is null)
        {
            exitCode = -1;
            return null;
        }
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.StandardError.BaseStream.CopyTo(ms);
        p.WaitForExit(120000);
        exitCode = p.HasExited ? p.ExitCode : -1;

        var bytes = ms.ToArray();
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes); // 先尝试严格 UTF-8
        }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(0).GetString(bytes); } // 退回系统 ANSI 代码页
            catch { return Encoding.UTF8.GetString(bytes); }
        }
    }
}
