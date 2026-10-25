using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrinterTool.Core;

namespace PrinterTool.UI;

/// <summary>驱动存储（Driver Store）打印机类驱动包管理对话框，底层为 pnputil。</summary>
public sealed class DriverStoreForm : Form
{
    private readonly ListView _list = new();
    private readonly Button _refresh = new() { Text = "刷新", AutoSize = true };
    private readonly Button _delete = new() { Text = "删除所选驱动包…", AutoSize = true };
    private bool _busy;

    public DriverStoreForm()
    {
        Text = "驱动存储包管理（打印机类）";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(780, 460);
        MinimumSize = new Size(640, 360);
        Font = new Font("Microsoft YaHei UI", 9F);

        var top = new Panel { Dock = DockStyle.Top, Height = 60 };
        var warn = new Label
        {
            Text = "此处管理驱动存储（Driver Store）中的打印机类 INF 驱动包。删除驱动包会影响所有依赖它的驱动，通常用于“彻底卸载后重装”场景；删除前请确认驱动列表中相关驱动已移除。",
            Dock = DockStyle.Fill,
            ForeColor = Color.Firebrick,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 4, 0),
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        buttons.Controls.Add(_refresh);
        buttons.Controls.Add(_delete);
        top.Controls.Add(warn);
        top.Controls.Add(buttons);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.Columns.Add("发布名称", 110);
        _list.Columns.Add("原始 INF", 170);
        _list.Columns.Add("提供商", 200);
        _list.Columns.Add("驱动版本", 230);

        Controls.Add(_list);
        Controls.Add(top);

        Shown += (s, e) => LoadPackages();
        _refresh.Click += (s, e) => LoadPackages();
        _delete.Click += async (s, e) => await DeleteSelectedAsync();
    }

    private async void LoadPackages()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var packages = await Task.Run(DriverPackageManager.EnumPrinterPackages);
            _list.Items.Clear();
            foreach (var p in packages)
            {
                var it = _list.Items.Add(p.PublishedName);
                it.SubItems.Add(p.OriginalName);
                it.SubItems.Add(p.Provider);
                it.SubItems.Add(p.Version);
                it.Tag = p;
            }
            Text = $"驱动存储包管理（打印机类）- 共 {packages.Count} 个";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "枚举驱动存储包失败（通常需要管理员权限）：\n" + ex.Message, "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (_busy) return;
        if (_list.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "请先选择要删除的驱动包。", "提示");
            return;
        }
        if (_list.SelectedItems[0].Tag is not DriverPackageItem item) return;

        if (MessageBox.Show(this,
                $"确定删除驱动包 {item.PublishedName}（{item.OriginalName}）？\n将执行: pnputil /delete-driver {item.PublishedName} /uninstall /force",
                "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _busy = true;
        try
        {
            var output = await Task.Run(() => DriverPackageManager.DeletePackage(item.PublishedName));
            MessageBox.Show(this, output, "pnputil 输出", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "删除失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
        }
        LoadPackages();
    }
}
