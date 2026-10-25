using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrinterTool.Core;

namespace PrinterTool.UI;

/// <summary>重点三 UI：驱动注册表残留扫描与清理。</summary>
public sealed class ResidualPage : TabPage
{
    private readonly ListView _list = new();
    private readonly TextBox _result = new();
    private bool _busy;

    public ResidualPage()
    {
        Text = "残留清理";

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        var btnScan = new Button { Text = "扫描残留", AutoSize = true };
        var btnClean = new Button { Text = "清理所选…", AutoSize = true };
        buttons.Controls.Add(btnScan);
        buttons.Controls.Add(btnClean);

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.MultiSelect = true;
        _list.HideSelection = false;
        _list.Columns.Add("驱动名称", 250);
        _list.Columns.Add("位置（环境\\版本）", 230);
        _list.Columns.Add("类型", 250);
        _list.Columns.Add("建议", 90);

        var resultGroup = new GroupBox { Text = "操作结果", Dock = DockStyle.Bottom, Height = 150 };
        _result.Dock = DockStyle.Fill;
        _result.Multiline = true;
        _result.ReadOnly = true;
        _result.ScrollBars = ScrollBars.Vertical;
        _result.Font = new Font("Consolas", 9F);
        _result.BackColor = Color.White;
        resultGroup.Controls.Add(_result);

        Controls.Add(_list);
        Controls.Add(resultGroup);
        Controls.Add(buttons);

        btnScan.Click += async (s, e) => await ScanAsync();
        btnClean.Click += async (s, e) => await CleanAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ = ScanAsync();
    }

    private void R(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        if (_result.IsHandleCreated && _result.InvokeRequired)
            _result.BeginInvoke(new Action(() => _result.AppendText(line)));
        else
            _result.AppendText(line);
    }

    private async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var items = await Task.Run(ResidualScanner.Scan);
            _list.Items.Clear();
            foreach (var it in items)
            {
                var row = _list.Items.Add(it.Name);
                row.SubItems.Add($"{it.Environment}\\{it.VersionKey}");
                row.SubItems.Add(it.KindText);
                row.SubItems.Add(it.Recommended ? "建议清理" : "谨慎");
                row.Tag = it;
            }
            R(items.Count == 0
                ? "扫描完成：未发现驱动注册表残留。"
                : $"扫描完成：发现 {items.Count} 项，其中建议清理 {items.Count(x => x.Recommended)} 项。");
        }
        catch (Exception ex)
        {
            R("扫描失败: " + ex.Message);
            OperationLog.Error("残留扫描失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task CleanAsync()
    {
        if (_busy) return;
        if (_list.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择要清理的残留项（可按住 Ctrl 多选）。", "提示");
            return;
        }
        var items = _list.SelectedItems.Cast<ListViewItem>().Select(r => (ResidualItem)r.Tag!).ToList();
        var list = string.Join("\n  • ", items.Select(x => $"{x.Name} ({x.Environment}\\{x.VersionKey})"));
        if (MessageBox.Show($"将删除以下 {items.Count} 个驱动残留注册表项（每项都会先自动备份）：\n\n  • {list}\n\n是否继续？",
            "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _busy = true;
        int ok = 0, fail = 0;
        try
        {
            foreach (var item in items)
            {
                try
                {
                    await Task.Run(() => ResidualScanner.Delete(item, R));
                    ok++;
                }
                catch (Exception ex)
                {
                    fail++;
                    R($"清理失败 {item.Name}: {ex.Message}");
                }
            }
            R($"清理完成：成功 {ok}，失败 {fail}。");
        }
        finally
        {
            _busy = false;
        }

        if (ok > 0 &&
            MessageBox.Show("建议重启打印服务使清理生效，现在重启？", "重启服务",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _busy = true;
            try
            {
                await Task.Run(() => SpoolerController.RestartSpooler(false, R));
            }
            catch (Exception ex)
            {
                R("重启服务失败: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }
        await ScanAsync();
    }
}
