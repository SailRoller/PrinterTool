using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrinterTool.Core;

namespace PrinterTool.UI;

/// <summary>重点三 UI：驱动与占用清单 + 驱动彻底卸载 + 驱动存储包管理入口。</summary>
public sealed class DriverPage : TabPage
{
    private readonly ListView _list = new();
    private readonly TextBox _details = new();
    private readonly Button _refresh = new() { Text = "刷新列表", AutoSize = true };
    private readonly Button _delete = new() { Text = "删除所选驱动…", AutoSize = true };
    private readonly Button _store = new() { Text = "管理驱动存储包…", AutoSize = true };
    private bool _busy;

    public DriverPage()
    {
        Text = "驱动管理";

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        buttons.Controls.Add(_refresh);
        buttons.Controls.Add(_delete);
        buttons.Controls.Add(_store);

        var detailsGroup = new GroupBox { Text = "所选驱动详情", Dock = DockStyle.Bottom, Height = 200 };
        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = false;
        _details.Font = new Font("Consolas", 9F);
        _details.BackColor = Color.White;
        detailsGroup.Controls.Add(_details);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Columns.Add("驱动名称", 280);
        _list.Columns.Add("环境", 110);
        _list.Columns.Add("版本", 60);
        _list.Columns.Add("驱动文件", 280);
        var useCol = _list.Columns.Add("关联打印机", 90);
        useCol.TextAlign = HorizontalAlignment.Right;
        _list.SelectedIndexChanged += (s, e) => ShowDetails();

        Controls.Add(_list);
        Controls.Add(detailsGroup);
        Controls.Add(buttons);

        _refresh.Click += async (s, e) => await LoadDriversAsync();
        _delete.Click += async (s, e) => await DeleteSelectedAsync();
        _store.Click += (s, e) => new DriverStoreForm().ShowDialog(FindForm());
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ = LoadDriversAsync();
    }

    private void R(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        if (_details.IsHandleCreated && _details.InvokeRequired)
            _details.BeginInvoke(new Action(() => _details.AppendText(line)));
        else
            _details.AppendText(line);
    }

    private async Task LoadDriversAsync()
    {
        if (_busy) return;
        _busy = true;
        var selectedName = _list.SelectedItems.Count > 0 ? ((DriverItem?)_list.SelectedItems[0].Tag)?.Name : null;
        try
        {
            var drivers = await Task.Run(PrinterDriverManager.GetDrivers);
            _list.Items.Clear();
            foreach (var d in drivers.OrderByDescending(x => x.Printers.Count).ThenBy(x => x.Name))
            {
                var it = _list.Items.Add(d.Name);
                it.SubItems.Add(d.Environment);
                it.SubItems.Add("v" + d.Version);
                it.SubItems.Add(Path.GetFileName(d.DriverPath));
                it.SubItems.Add(d.Printers.Count.ToString());
                it.Tag = d;
                if (selectedName is not null && string.Equals(d.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                    it.Selected = true;
            }
            R($"已加载 {drivers.Count} 个打印机驱动。");
        }
        catch (Exception ex)
        {
            R("加载驱动列表失败: " + ex.Message);
            OperationLog.Error("加载驱动列表失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
        ShowDetails();
    }

    private void ShowDetails()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not DriverItem d)
        {
            _details.Clear();
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"驱动名称: {d.Name}");
        sb.AppendLine($"环境/版本: {d.Environment} / v{d.Version}");
        sb.AppendLine($"驱动文件: {d.DriverPath}");
        sb.AppendLine($"配置文件: {d.ConfigFile}");
        sb.AppendLine($"数据文件: {d.DataFile}");
        sb.AppendLine($"依赖文件: {d.DependentFiles}");
        sb.AppendLine($"关联打印机 ({d.Printers.Count}):");
        foreach (var p in d.Printers)
            sb.AppendLine("  • " + p);
        sb.AppendLine($"隐藏/残留注册表引用 ({d.HiddenReferences.Count}):");
        foreach (var h in d.HiddenReferences)
            sb.AppendLine("  • " + h);
        _details.Text = sb.ToString();
    }

    private async Task DeleteSelectedAsync()
    {
        if (_busy) return;
        if (_list.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择要删除的驱动。", "提示");
            return;
        }
        if (_list.SelectedItems[0].Tag is not DriverItem d) return;

        var sb2 = new StringBuilder();
        if (d.Printers.Count > 0)
            sb2.Append($"驱动“{d.Name}”正被以下 {d.Printers.Count} 台打印机使用：\n\n  • {string.Join("\n  • ", d.Printers)}\n");
        if (d.HiddenReferences.Count > 0)
            sb2.Append($"\n另发现 {d.HiddenReferences.Count} 个隐藏/残留的注册表引用（将一并备份并清理）：\n\n  • {string.Join("\n  • ", d.HiddenReferences)}\n");
        sb2.Append($"\n环境: {d.Environment} (v{d.Version})。");
        sb2.Append("\n删除过程将重启打印服务；若打印机仍有未完成任务，将清空打印队列以完成删除。");
        sb2.Append("\n若仍提示“驱动正在使用”，工具会自动清理隐藏引用并强制卸载驱动存储包。");
        sb2.Append("\n\n是否继续？");
        if (MessageBox.Show(sb2.ToString(), "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _busy = true;
        try
        {
            R($"── 开始删除驱动: {d.Name} ──");
            await Task.Run(() => PrinterDriverManager.DeleteDriver(d, removePrinters: true, restartSpoolerAfter: true, R));
            R("── 驱动删除完成 ──");
            MessageBox.Show($"驱动 {d.Name} 已删除。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            R("删除失败: " + ex.Message);
            OperationLog.Error($"删除驱动 {d.Name} 失败: {ex.Message}");
            MessageBox.Show("删除失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
        }
        await LoadDriversAsync();
    }
}
