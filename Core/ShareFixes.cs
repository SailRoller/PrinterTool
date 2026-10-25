using System;
using Microsoft.Win32;

namespace PrinterTool.Core;

/// <summary>
/// 共享打印机常见错误预案（修改前一律先备份注册表）：
/// - 0x0000011b / 0x00000709：Print Spooler RPC 认证隐私级别（PrintNightmare 补丁 KB5005565 等的已知影响）
/// - 0x00000bc4：网络发现/浏览服务未运行、防火墙组未放行
/// - “安全策略阻止未经身份验证的来宾访问”：本机(客户端) SMB 来宾登录被禁
/// </summary>
public static class ShareFixes
{
    /// <summary>修复 11b / 709：设置 RpcAuthnLevelPrivacyEnabled=1 并重启打印服务。</summary>
    public static void FixRpcAuthLevel(Action<string> log)
    {
        const string subKey = @"SYSTEM\CurrentControlSet\Control\Print";
        BackupManager.ExportRegistryKey(@"HKLM\" + subKey, "print-rpc");
        using (var key = Registry.LocalMachine.OpenSubKey(subKey, writable: true))
        {
            if (key is null) throw new InvalidOperationException("注册表项不存在: " + subKey);
            key.SetValue("RpcAuthnLevelPrivacyEnabled", 1, RegistryValueKind.DWord);
        }
        log("已设置 RpcAuthnLevelPrivacyEnabled = 1。");
        SpoolerController.RestartSpooler(false, log);
        log("完成。注意：11b/709 通常需要【共享主机】与【客户端】两端都执行本修复。");
        OperationLog.Info("已应用 11b/709 RPC 认证级别修复。");
    }

    /// <summary>修复 bc4：启动网络发现相关服务并放行防火墙规则组。</summary>
    public static void FixNetworkDiscovery(Action<string> log)
    {
        log("启用并启动网络发现相关服务 ...");
        (string Name, bool Auto)[] services =
        {
            ("FDResPub", true),   // Function Discovery Resource Publication（发布本机到网络）
            ("fdPHost", false),   // Function Discovery Provider Host
            ("SSDPSRV", false),   // SSDP Discovery
            ("upnphost", false),  // UPnP Device Host
        };
        foreach (var (name, auto) in services)
        {
            try
            {
                ServiceHelper.SetStartType(name, auto);
                ServiceHelper.EnsureRunning(name);
                log($"  {name}: {ServiceHelper.StateText(ServiceHelper.GetState(name))}");
            }
            catch (Exception ex)
            {
                log($"  {name}: 失败 {ex.Message}");
            }
        }

        log("启用防火墙规则组（中英文组名均尝试）...");
        foreach (var line in FirewallHelper.EnableGroups("文件和打印机共享", "File and Printer Sharing", "网络发现", "Network Discovery"))
            log("  " + line);

        OperationLog.Info("已应用 bc4 网络发现修复。");
        log("完成。若仍报 bc4，请确认两台机器在同一网段/工作组，或直接用 \\\\主机名\\打印机名 重连。");
    }

    /// <summary>修复“安全策略阻止未经身份验证的来宾访问”（本机作为客户端访问来宾共享）。</summary>
    public static void FixGuestAccess(Action<string> log)
    {
        const string subKey = @"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters";
        BackupManager.ExportRegistryKey(@"HKLM\" + subKey, "lanmanworkstation-guest");
        using (var key = Registry.LocalMachine.OpenSubKey(subKey, writable: true))
        {
            if (key is null) throw new InvalidOperationException("注册表项不存在: " + subKey);
            key.SetValue("AllowInsecureGuestAuth", 1, RegistryValueKind.DWord);
        }
        log("已设置 AllowInsecureGuestAuth = 1（允许不安全的来宾登录）。");
        log("安全提示：该设置降低本机 SMB 安全性；更稳妥的做法是在共享主机上为使用者创建账号密码。");
        log("如未立即生效，请重启 Workstation 服务或重启电脑。");
        OperationLog.Info("已应用来宾访问修复（AllowInsecureGuestAuth=1）。");
    }

    /// <summary>恢复来宾默认安全策略：重新拒绝来宾网络登录，可选禁用 Guest 账户。</summary>
    public static void RestoreGuestPolicy(bool disableGuestAccount, Action<string> log)
    {
        LsaRights.SetGuestNetworkLogonDenied(true);
        log("已恢复“拒绝来宾(Guest)从网络访问此计算机”。");
        if (disableGuestAccount)
        {
            var (code, output) = ProcessRunner.Run("net.exe", "user Guest /active:no");
            log(code == 0 ? "已禁用 Guest 账户。" : $"禁用 Guest 失败: {output}");
        }
        OperationLog.Info("已恢复来宾默认安全策略。");
    }
}
