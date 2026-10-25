using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PrinterTool.Core;

public enum SvcState
{
    NotFound, Stopped, StartPending, StopPending, Running,
    Paused, ContinuePending, PausePending, Unknown,
}

/// <summary>Windows 服务控制（advapi32 P/Invoke，无外部包依赖）。</summary>
public static class ServiceHelper
{
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint SERVICE_CHANGE_CONFIG = 0x0002;
    private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    private const uint SERVICE_AUTO_START = 0x00000002;
    private const uint SERVICE_DEMAND_START = 0x00000003;
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint SERVICE_STOP_PENDING = 0x00000003;
    private const uint SERVICE_RUNNING = 0x00000004;
    private const uint SERVICE_CONTROL_STOP = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartService(IntPtr service, uint numServiceArgs, IntPtr serviceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr service, uint control, ref SERVICE_STATUS status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig(IntPtr service, uint serviceType, uint startType,
        uint errorControl, string? binaryPathName, string? loadOrderGroup, IntPtr tagId,
        string? dependencies, string? serviceStartName, string? password, string? displayName);

    /// <summary>设置服务启动类型（true=自动，false=手动）。</summary>
    public static void SetStartType(string serviceName, bool autoStart)
    {
        var svc = OpenServiceHandle(serviceName, SERVICE_CHANGE_CONFIG);
        try
        {
            if (!ChangeServiceConfig(svc, SERVICE_NO_CHANGE, autoStart ? SERVICE_AUTO_START : SERVICE_DEMAND_START,
                    SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
                throw new Win32Exception();
        }
        finally
        {
            CloseServiceHandle(svc);
        }
    }

    /// <summary>确保服务处于运行状态（未安装则记日志跳过，不抛异常）。</summary>
    public static void EnsureRunning(string serviceName, Action<string>? log = null)
    {
        var state = GetState(serviceName);
        if (state == SvcState.NotFound)
        {
            log?.Invoke($"服务未安装，跳过: {serviceName}");
            return;
        }
        if (state == SvcState.Running) return;
        Start(serviceName, 15000);
        log?.Invoke($"服务已启动: {serviceName}");
    }

    private static IntPtr OpenServiceHandle(string serviceName, uint access)
    {
        var scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var svc = OpenService(scm, serviceName, access | SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero)
            {
                var err = Marshal.GetLastWin32Error();
                if (err == 1060) // ERROR_SERVICE_DOES_NOT_EXIST
                    throw new InvalidOperationException($"服务未安装: {serviceName}");
                throw new Win32Exception(err);
            }
            return svc; // 服务句柄独立于 SCManager，可先关闭管理器句柄
        }
        finally
        {
            CloseServiceHandle(scm);
        }
    }

    public static SvcState GetState(string serviceName)
    {
        IntPtr svc;
        try
        {
            svc = OpenServiceHandle(serviceName, SERVICE_QUERY_STATUS);
        }
        catch (InvalidOperationException)
        {
            return SvcState.NotFound;
        }
        catch (Win32Exception)
        {
            return SvcState.Unknown;
        }
        try
        {
            return Map(CurrentState(svc));
        }
        finally
        {
            CloseServiceHandle(svc);
        }
    }

    public static void Start(string serviceName, int timeoutMs)
    {
        var svc = OpenServiceHandle(serviceName, SERVICE_START);
        try
        {
            if (CurrentState(svc) == SERVICE_RUNNING) return;
            if (!StartService(svc, 0, IntPtr.Zero))
            {
                var err = Marshal.GetLastWin32Error();
                if (err != 1056) // ERROR_SERVICE_ALREADY_RUNNING
                    throw new Win32Exception(err);
            }
            WaitFor(svc, SERVICE_RUNNING, timeoutMs, serviceName);
        }
        finally
        {
            CloseServiceHandle(svc);
        }
    }

    public static void Stop(string serviceName, int timeoutMs)
    {
        var svc = OpenServiceHandle(serviceName, SERVICE_STOP);
        try
        {
            var state = CurrentState(svc);
            if (state == SERVICE_STOPPED) return;
            if (state == SERVICE_STOP_PENDING)
            {
                WaitFor(svc, SERVICE_STOPPED, timeoutMs, serviceName);
                return;
            }
            var st = new SERVICE_STATUS();
            if (!ControlService(svc, SERVICE_CONTROL_STOP, ref st))
            {
                var err = Marshal.GetLastWin32Error();
                if (err != 1062) // ERROR_SERVICE_NOT_ACTIVE
                    throw new Win32Exception(err);
            }
            WaitFor(svc, SERVICE_STOPPED, timeoutMs, serviceName);
        }
        finally
        {
            CloseServiceHandle(svc);
        }
    }

    private static uint CurrentState(IntPtr svc)
    {
        var st = new SERVICE_STATUS();
        if (!QueryServiceStatus(svc, ref st)) throw new Win32Exception();
        return st.dwCurrentState;
    }

    private static void WaitFor(IntPtr svc, uint target, int timeoutMs, string serviceName)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CurrentState(svc) == target) return;
            System.Threading.Thread.Sleep(300);
        }
        throw new TimeoutException($"等待服务 {serviceName} 进入目标状态超时（{timeoutMs / 1000} 秒）。");
    }

    public static string StateText(SvcState s) => s switch
    {
        SvcState.NotFound => "未安装",
        SvcState.Stopped => "已停止",
        SvcState.StartPending => "正在启动",
        SvcState.StopPending => "正在停止",
        SvcState.Running => "运行中",
        SvcState.Paused => "已暂停",
        SvcState.ContinuePending => "恢复中",
        SvcState.PausePending => "暂停中",
        _ => "未知",
    };

    private static SvcState Map(uint s) => s switch
    {
        1 => SvcState.Stopped,
        2 => SvcState.StartPending,
        3 => SvcState.StopPending,
        4 => SvcState.Running,
        5 => SvcState.ContinuePending,
        6 => SvcState.Paused,
        7 => SvcState.PausePending,
        _ => SvcState.Unknown,
    };
}
