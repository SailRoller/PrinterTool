using System.Collections.Generic;

namespace PrinterTool.Core;

/// <summary>服务看板关注的服务列表（名称用于控制，显示名仅用于展示）。</summary>
public static class WatchedServices
{
    public static readonly (string Name, string Display)[] All =
    {
        ("Spooler", "打印后台处理程序 (Print Spooler)"),
        ("Fax", "传真服务 (Fax)"),
        ("lpdsvc", "LPD 打印服务 (TCP/IP Print Server)"),
    };
}

public sealed class PrinterItem
{
    public string Name = "";
    public string DriverName = "";
    public string PortName = "";
    public string ShareName = "";
    public bool IsConnection;
    public bool IsShared;
    public uint JobCount;
    public string StatusText = "";
}

public sealed class DriverItem
{
    public string Name = "";
    public string Environment = "";
    public int Version;
    public string DriverPath = "";
    public string DataFile = "";
    public string ConfigFile = "";
    public string DependentFiles = "";
    public List<string> Printers = new();

    /// <summary>注册表中仍引用该驱动、但 EnumPrinters 已看不到的打印机项（隐藏/残留/待删除）。</summary>
    public List<string> HiddenReferences = new();
}

public sealed class SpoolFileItem
{
    public string FileName = "";
    public long Length;
    public System.DateTime LastWriteTime;
}

public enum ResidualKind
{
    /// <summary>Version-3 注册表孤儿：API 已枚举不到对应驱动，是卸载失败/更新残留。</summary>
    OrphanV3,

    /// <summary>Version-4 驱动但当前无任何打印机关联，属疑似残留，由用户判断。</summary>
    SuspectV4,
}

public sealed class ResidualItem
{
    public string Name = "";
    public string Environment = "";
    public string VersionKey = "";
    public string RegistryPath = "";
    public ResidualKind Kind;

    public bool Recommended => Kind == ResidualKind.OrphanV3;

    public string KindText => Kind switch
    {
        ResidualKind.OrphanV3 => "孤儿驱动（v3，API 已枚举不到）",
        _ => "疑似残留（v4，无打印机关联）",
    };
}

public sealed class DriverPackageItem
{
    public string PublishedName = "";
    public string OriginalName = "";
    public string Provider = "";
    public string Version = "";
}
