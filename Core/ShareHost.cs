using System;
using System.Collections.Generic;
using System.Linq;
using PrinterTool.Native;

namespace PrinterTool.Core;

/// <summary>共享主机模式：一键把本机打印机共享给局域网（共享打印机 + 发布/发现服务 + 防火墙放行 + 可选来宾）。</summary>
public static class ShareHost
{
    public sealed record ShareResult(string SharePath, string Summary);

    public static List<PrinterItem> GetLocalPrinters()
        => PrinterDriverManager.GetPrinters().Where(p => !p.IsConnection).ToList();

    /// <summary>一键共享：确保打印服务运行 → 共享所选打印机 → 启动网络发现服务 → 放行防火墙 → 可选允许来宾。</summary>
    public static ShareResult SharePrinter(string printerName, bool allowGuest, Action<string> log)
    {
        var summary = new List<string>();

        // 1) 确保打印服务运行
        if (ServiceHelper.GetState(SpoolerController.SpoolerName) != SvcState.Running)
        {
            log("启动打印服务 ...");
            ServiceHelper.Start(SpoolerController.SpoolerName, 30000);
        }

        // 2) 共享打印机
        Winspool.SetPrinterShared(printerName, true, null);
        var shareName = PrinterDriverManager.GetPrinters()
            .FirstOrDefault(p => string.Equals(p.Name, printerName, StringComparison.OrdinalIgnoreCase))?.ShareName;
        if (string.IsNullOrWhiteSpace(shareName)) shareName = printerName;
        var sharePath = $@"\\{Environment.MachineName}\{shareName}";
        log($"已共享打印机: {sharePath}");
        summary.Add("共享路径: " + sharePath);

        // 3) 网络发现/发布服务
        log("启用网络发现相关服务 ...");
        foreach (var (svc, auto) in new[] { ("FDResPub", true), ("fdPHost", false), ("SSDPSRV", false), ("upnphost", false) })
        {
            try
            {
                ServiceHelper.SetStartType(svc, auto);
                ServiceHelper.EnsureRunning(svc);
                summary.Add($"{svc}: {ServiceHelper.StateText(ServiceHelper.GetState(svc))}");
            }
            catch (Exception ex)
            {
                summary.Add($"{svc}: 失败 {ex.Message}");
            }
        }

        // 4) 防火墙放行
        log("启用“文件和打印机共享”防火墙规则组 ...");
        foreach (var line in FirewallHelper.EnableGroups("文件和打印机共享", "File and Printer Sharing"))
            summary.Add("防火墙 " + line);

        // 5) 可选来宾访问
        if (allowGuest)
        {
            log("允许来宾网络访问（启用 Guest 账户并解除网络登录限制）...");
            LsaRights.AllowGuestNetworkLogon(log);
            summary.Add("来宾访问: 已允许（安全性降低，可用预案区的“恢复默认”回滚）");
        }
        else
        {
            summary.Add("来宾访问: 未改动（客户端需账号密码；如需免密请勾选允许来宾）");
        }

        OperationLog.Info($"一键共享完成: {sharePath}（来宾={allowGuest}）");
        return new ShareResult(sharePath, string.Join("\n", summary));
    }

    public static void UnsharePrinter(string printerName, Action<string> log)
    {
        Winspool.SetPrinterShared(printerName, false, null);
        log($"已取消共享: {printerName}");
        OperationLog.Info($"已取消共享打印机 {printerName}。");
    }
}
