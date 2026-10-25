using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PrinterTool.Core;

/// <summary>
/// LSA 账户权限：控制“来宾(Guest)是否允许从网络访问本机”。
/// 这是共享主机上“来宾模式共享”的核心开关（对应组策略“拒绝从网络访问这台计算机”）。
/// </summary>
public static class LsaRights
{
    private const string DenyNetworkLogon = "SeDenyNetworkLogonRight";
    private const uint PolicyAllAccess = 0xF0FFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [DllImport("advapi32.dll")]
    private static extern uint LsaOpenPolicy(IntPtr systemName, ref LSA_OBJECT_ATTRIBUTES attributes, uint desiredAccess, out IntPtr policyHandle);

    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr policyHandle);

    [DllImport("advapi32.dll")]
    private static extern uint LsaAddAccountRights(IntPtr policyHandle, byte[] accountSid, LSA_UNICODE_STRING[] userRights, int countOfRights);

    [DllImport("advapi32.dll")]
    private static extern uint LsaRemoveAccountRights(IntPtr policyHandle, byte[] accountSid, bool allRights, LSA_UNICODE_STRING[] userRights, int countOfRights);

    [DllImport("advapi32.dll")]
    private static extern int LsaNtStatusToWinError(uint status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupAccountName(string? systemName, string accountName, byte[] sid, ref int sidLength, StringBuilder domainName, ref int domainNameLength, out int sidUse);

    /// <summary>true = 重新拒绝来宾网络登录（恢复默认安全策略）；false = 允许来宾网络登录。</summary>
    public static void SetGuestNetworkLogonDenied(bool deny)
    {
        var sid = GetAccountSid("Guest");
        var oa = new LSA_OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<LSA_OBJECT_ATTRIBUTES>() };
        var status = LsaOpenPolicy(IntPtr.Zero, ref oa, PolicyAllAccess, out var policy);
        if (status != 0)
            throw new Win32Exception(LsaNtStatusToWinError(status), "打开本地安全策略失败 (LsaOpenPolicy)");
        try
        {
            var right = ToLsaString(DenyNetworkLogon);
            var rights = new[] { right };
            status = deny
                ? LsaAddAccountRights(policy, sid, rights, 1)
                : LsaRemoveAccountRights(policy, sid, false, rights, 1);
            if (status != 0)
                throw new Win32Exception(LsaNtStatusToWinError(status), (deny ? "添加" : "移除") + " “拒绝从网络访问此计算机”(Guest) 失败");
        }
        finally
        {
            LsaClose(policy);
        }
    }

    /// <summary>一键允许来宾网络访问：启用 Guest 账户 + 移除其“拒绝网络登录”限制。</summary>
    public static void AllowGuestNetworkLogon(Action<string>? log = null)
    {
        var (code, output) = ProcessRunner.Run("net.exe", "user Guest /active:yes");
        if (code == 0)
            log?.Invoke("Guest 账户已启用。");
        else
            log?.Invoke($"启用 Guest 账户失败（可能已启用或名称非 Guest）: {output}");

        SetGuestNetworkLogonDenied(false);
        log?.Invoke("已移除 Guest 的“拒绝从网络访问此计算机”限制。");
    }

    private static byte[] GetAccountSid(string accountName)
    {
        var sid = new byte[128];
        var sidLen = sid.Length;
        var domain = new StringBuilder(128);
        var domainLen = 128;
        if (!LookupAccountName(null, accountName, sid, ref sidLen, domain, ref domainLen, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"查找账户 {accountName} 失败");
        Array.Resize(ref sid, sidLen);
        return sid;
    }

    private static LSA_UNICODE_STRING ToLsaString(string s)
    {
        return new LSA_UNICODE_STRING
        {
            Buffer = Marshal.StringToHGlobalUni(s),
            Length = (ushort)(s.Length * 2),
            MaximumLength = (ushort)(s.Length * 2),
        };
    }
}
