using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PrinterTool.Native;

/// <summary>winspool.drv 打印后台 API 互操作（枚举打印机/驱动、删除打印机/驱动）。</summary>
internal static class Winspool
{
    internal const uint PRINTER_ENUM_LOCAL = 0x00000002;
    internal const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;
    internal const uint PRINTER_ATTRIBUTE_NETWORK = 0x00000010;
    internal const uint PRINTER_ATTRIBUTE_SHARED = 0x00000008;
    internal const uint DPD_DELETE_ALL_FILES = 0x00000004;
    internal const uint PRINTER_ALL_ACCESS = 0xF000C;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct PRINTER_DEFAULTS
    {
        public string? pDatatype;
        public IntPtr pDevMode;
        public uint DesiredAccess;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct PRINTER_INFO_2
    {
        public string? pServerName;
        public string? pPrinterName;
        public string? pShareName;
        public string? pPortName;
        public string? pDriverName;
        public string? pComment;
        public string? pLocation;
        public IntPtr pDevMode;
        public string? pSepFile;
        public string? pPrintProcessor;
        public string? pDataType;
        public string? pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct DRIVER_INFO_3
    {
        public uint cVersion;
        public string? pName;
        public string? pEnvironment;
        public string? pDriverPath;
        public string? pDataFile;
        public string? pConfigFile;
        public string? pHelpFile;
        public string? pDependentFiles;
        public string? pMonitorName;
        public string? pDefaultDataType;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool EnumPrinters(uint Flags, string? Name, uint Level, IntPtr pPrinterEnum, uint cbBuf,
        out uint pcbNeeded, out uint pcReturned);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool EnumPrinterDrivers(string? pName, string? pEnvironment, uint Level, IntPtr pDriverInfo,
        uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool DeletePrinterDriverEx(string? pName, string? pEnvironment, string? pDriverName,
        uint dwDeleteFlag, uint dwVersionFlag);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool DeletePrinterConnection(string pName);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool AddPrinterConnection(string pName);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetPrinter(IntPtr hPrinter, uint Level, IntPtr pPrinter, uint cbBuf, out uint pcbNeeded);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetPrinter(IntPtr hPrinter, uint Level, IntPtr pPrinter, uint Command);

    [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr hPrinter, ref PRINTER_DEFAULTS pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool DeletePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    /// <summary>枚举本机打印机（本地 + 网络连接），Level 2。</summary>
    internal static List<PRINTER_INFO_2> EnumAllPrinters()
    {
        const uint flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;
        EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0) return new List<PRINTER_INFO_2>();
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(flags, null, 2, buffer, needed, out needed, out var returned))
                throw new Win32Exception();
            return ReadStructArray<PRINTER_INFO_2>(buffer, (int)returned);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>枚举指定环境（如 "Windows x64"、"Windows NT x86"）下安装的 v3 打印驱动。</summary>
    internal static List<DRIVER_INFO_3> EnumAllDrivers(string environment)
    {
        EnumPrinterDrivers(null, environment, 3, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0) return new List<DRIVER_INFO_3>();
        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinterDrivers(null, environment, 3, buffer, needed, out needed, out var returned))
                throw new Win32Exception();
            return ReadStructArray<DRIVER_INFO_3>(buffer, (int)returned);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static bool TryDeletePrinterDriver(string environment, string driverName)
        => DeletePrinterDriverEx(null, environment, driverName, DPD_DELETE_ALL_FILES, 0);

    internal static bool TryDeletePrinterConnection(string printerName)
        => DeletePrinterConnection(printerName);

    /// <summary>添加（重连）网络打印机连接，pName 形如 \\server\printer。</summary>
    internal static bool TryAddPrinterConnection(string printerName)
        => AddPrinterConnection(printerName);

    /// <summary>设置/取消打印机共享（GetPrinter level 2 → 改 Attributes/ShareName → SetPrinter）。</summary>
    internal static void SetPrinterShared(string printerName, bool shared, string? shareName)
    {
        var def = new PRINTER_DEFAULTS { pDatatype = null, pDevMode = IntPtr.Zero, DesiredAccess = PRINTER_ALL_ACCESS };
        if (!OpenPrinter(printerName, out var handle, ref def))
            throw new Win32Exception();
        try
        {
            GetPrinter(handle, 2, IntPtr.Zero, 0, out var needed);
            if (needed == 0) throw new Win32Exception();
            var buffer = Marshal.AllocHGlobal((int)needed);
            var newShareName = IntPtr.Zero;
            try
            {
                if (!GetPrinter(handle, 2, buffer, needed, out _))
                    throw new Win32Exception();
                var info = Marshal.PtrToStructure<PRINTER_INFO_2>(buffer);
                if (shared)
                {
                    info.Attributes |= PRINTER_ATTRIBUTE_SHARED;
                    newShareName = Marshal.StringToHGlobalUni(string.IsNullOrWhiteSpace(shareName) ? printerName : shareName!);
                }
                else
                {
                    info.Attributes &= ~PRINTER_ATTRIBUTE_SHARED;
                }
                Marshal.StructureToPtr(info, buffer, false);
                if (newShareName != IntPtr.Zero)
                {
                    // pShareName 声明为 string，无法直接承载新指针，按字段偏移写回
                    var offset = (int)Marshal.OffsetOf<PRINTER_INFO_2>(nameof(PRINTER_INFO_2.pShareName));
                    Marshal.WriteIntPtr(buffer, offset, newShareName);
                }
                if (!SetPrinter(handle, 2, buffer, 0))
                    throw new Win32Exception();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                if (newShareName != IntPtr.Zero) Marshal.FreeHGlobal(newShareName);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    /// <summary>删除本地打印机实例（打开后标记删除）。</summary>
    internal static void DeleteLocalPrinter(string printerName)
    {
        var def = new PRINTER_DEFAULTS { pDatatype = null, pDevMode = IntPtr.Zero, DesiredAccess = PRINTER_ALL_ACCESS };
        if (!OpenPrinter(printerName, out var handle, ref def))
            throw new Win32Exception();
        try
        {
            if (!DeletePrinter(handle))
                throw new Win32Exception();
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    private static List<T> ReadStructArray<T>(IntPtr buffer, int count) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var list = new List<T>(count);
        long basePtr = buffer.ToInt64();
        for (int i = 0; i < count; i++)
            list.Add(Marshal.PtrToStructure<T>((IntPtr)(basePtr + (long)i * size)));
        return list;
    }
}
