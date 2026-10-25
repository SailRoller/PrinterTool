using System;
using System.ComponentModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PrinterTool.Native;

namespace PrinterTool.Core;

/// <summary>
/// 接收主机模式：一键重连 \\主机\打印机。
/// 流程：诊断主机连通性 → 移除旧连接 → AddPrinterConnection → 失败按错误码自动应用预案（11b/709/bc4）后重试。
/// </summary>
public static class ShareClient
{
    public static string NormalizeUnc(string input)
    {
        var s = input.Trim().Replace('/', '\\');
        if (s.StartsWith(@"\\")) return s;
        return @"\\" + s;
    }

    /// <summary>解析 \\host\printer；格式不合法返回 null。</summary>
    public static (string Host, string Printer)? ParseUnc(string unc)
    {
        var s = NormalizeUnc(unc).TrimEnd('\\');
        if (s.Length <= 3) return null;
        var rest = s.Substring(2);
        var idx = rest.IndexOf('\\');
        if (idx <= 0 || idx == rest.Length - 1) return null;
        return (rest.Substring(0, idx), rest.Substring(idx + 1));
    }

    /// <summary>诊断主机：Ping 可达性与 SMB 445 端口。</summary>
    public static (bool Ping, bool Smb445) Diagnose(string host)
    {
        bool ping = false;
        try
        {
            using var p = new Ping();
            var reply = p.Send(host, 2000);
            ping = reply.Status == IPStatus.Success;
        }
        catch { /* 解析失败/超时 */ }

        bool smb = false;
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, 445);
            smb = task.Wait(2500) && client.Connected;
        }
        catch { /* 端口不通 */ }
        return (ping, smb);
    }

    /// <summary>一键诊断重连；autoFix=true 时失败后自动按错误码套用预案并重试一次。</summary>
    public static void Reconnect(string unc, bool autoFix, Action<string> log)
    {
        var target = NormalizeUnc(unc);
        var parsed = ParseUnc(target);
        if (parsed is null)
            throw new ArgumentException("地址格式应为 \\\\主机名\\打印机名，例如 \\\\OFFICE-PC\\HP DJ 1110 series");
        var (host, _) = parsed.Value;

        log($"诊断主机 {host} ...");
        var (ping, smb) = Diagnose(host);
        log($"  Ping: {(ping ? "通" : "不通（对方可能禁 ICMP，不一定影响连接）")}");
        log($"  TCP 445 (SMB): {(smb ? "通" : "不通")}");
        if (!smb)
            log("  警告: 445 不通——请确认主机已开机并已共享；可在主机端运行本工具的“一键共享”。");

        // 移除旧连接（失败不阻断）
        var existing = PrinterDriverManager.GetPrinters()
            .FirstOrDefault(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            log("移除旧的打印机连接 ...");
            try
            {
                Winspool.TryDeletePrinterConnection(target);
            }
            catch (Exception ex)
            {
                log("  旧连接移除失败（继续尝试添加）: " + ex.Message);
            }
        }

        log($"连接 {target} ...");
        if (Winspool.TryAddPrinterConnection(target))
        {
            log("连接成功。");
            OperationLog.Info($"共享打印机重连成功: {target}");
            return;
        }
        var error = new Win32Exception();
        var codeText = $"0x{error.NativeErrorCode:X}";
        log($"连接失败: {error.Message}（错误码 {error.NativeErrorCode}, {codeText}）");

        if (!autoFix)
        {
            throw new InvalidOperationException(
                $"连接失败: {error.Message}（{codeText}）。可在“常见错误一键修复”中手动应用对应预案后重试。");
        }

        switch ((uint)error.NativeErrorCode)
        {
            case 281:   // 0x0000011b
            case 1801:  // 0x00000709
                log($"识别为 {codeText}（11b/709 类），自动应用 RPC 认证级别修复 ...");
                ShareFixes.FixRpcAuthLevel(log);
                break;
            case 3012:  // 0x00000bc4
                log($"识别为 {codeText}（bc4 类），自动应用网络发现修复 ...");
                ShareFixes.FixNetworkDiscovery(log);
                break;
            default:
                throw new InvalidOperationException(
                    $"连接失败: {error.Message}（{codeText}）。\n" +
                    "可依次尝试：\n" +
                    "1) 若提示“来宾访问被安全策略阻止”，执行“修复来宾访问被阻止”；\n" +
                    "2) 在共享主机端运行本工具的“一键共享”；\n" +
                    "3) 在共享主机端执行“修复 11b/709”；\n" +
                    "4) 核对主机上的打印机共享名是否正确（含大小写与空格）。");
        }

        log("预案已应用，重试连接 ...");
        if (Winspool.TryAddPrinterConnection(target))
        {
            log("连接成功。");
            OperationLog.Info($"应用预案后共享打印机重连成功: {target}");
            return;
        }
        var retryError = new Win32Exception();
        throw new InvalidOperationException(
            $"重试后仍失败: {retryError.Message}（0x{retryError.NativeErrorCode:X}）。" +
            "若为 11b/709，请务必在【共享主机】那台机器上也执行本修复。");
    }
}
