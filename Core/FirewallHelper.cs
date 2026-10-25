using System;
using System.Collections.Generic;
using System.Linq;

namespace PrinterTool.Core;

/// <summary>通过 netsh 启用防火墙规则组；组名在中英文系统上不同，因此同时尝试两种写法。</summary>
public static class FirewallHelper
{
    /// <summary>启用指定防火墙规则组，返回每组的结果摘要。</summary>
    public static List<string> EnableGroups(params string[] groups)
    {
        var results = new List<string>();
        foreach (var group in groups)
        {
            var (code, output) = ProcessRunner.Run(
                "netsh.exe", $"advfirewall firewall set rule group=\"{group}\" new enable=Yes", 60000);
            var summary = $"[{group}] exit={code}: {OneLine(output)}";
            results.Add(summary);
            OperationLog.Info("启用防火墙组: " + summary);
        }
        return results;
    }

    private static string OneLine(string s)
    {
        var joined = string.Join(" | ", s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        return joined.Length > 140 ? joined.Substring(0, 140) + "…" : joined;
    }
}
