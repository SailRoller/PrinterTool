using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrinterTool.Core;

namespace PrinterTool.UI;

/// <summary>重点二 UI：服务状态看板 + Spooler 一键修复 + 卡死任务扫描清理。</summary>
public sealed class ServicePage : TabPage
{
    private readonly ListView _serviceList = new();
    private readonly TextBox _result = new();
    private readonly CheckBox _clearCheck = new();
    private readonly ListView _fileList = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _busy;

    public ServicePage()
    {
        Text = "服务管理";

        // ── 卡死任务清理 ──
        var fileGroup = new GroupBox { Text = "卡死任务清理（清理时会自动停止/重启打印服务）", Dock = DockStyle.Fill };
        var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        var btnScan = new Button { Text = "扫描队列残留文件", AutoSize = true };
        var btnClear = new Button { Text = "清理（重启服务并清空队列）", AutoSize = true };
        fileButtons.Controls.Add(btnScan);
        fileButtons.Controls.Add(btnClear);

        _fileList.Dock = DockStyle.Fill;
        _fileList.View = View.Details;
        _fileList.FullRowSelect = true;
        _fileList.Columns.Add("文件名", 260);
        var sizeCol = _fileList.Columns.Add("大小", 90);
        sizeCol.TextAlign = HorizontalAlignment.Right;
        _fileList.Columns.Add("修改时间", 170);

        fileGroup.Controls.Add(_fileList);
        fileGroup.Controls.Add(fileButtons);

        // ── Spooler 一键修复 ──
        var fixGroup = new GroupBox { Text = "Print Spooler 一键修复", Dock = DockStyle.Top, Height = 175 };
        var btnFix = new Button { Text = "重启 Print Spooler 服务", Dock = DockStyle.Top, Height = 34 };
        _clearCheck.Text = "重启时同时清空打印队列文件（将取消所有未完成的打印任务，推荐）";
        _clearCheck.Checked = true;
        _clearCheck.Dock = DockStyle.Top;
        _clearCheck.Height = 26;
        _result.Dock = DockStyle.Fill;
        _result.Multiline = true;
        _result.ReadOnly = true;
        _result.ScrollBars = ScrollBars.Vertical;
        _result.Font = new Font("Consolas", 9F);
        _result.BackColor = Color.White;

        fixGroup.Controls.Add(_result);
        fixGroup.Controls.Add(btnFix);
        fixGroup.Controls.Add(_clearCheck);

        // ── 服务状态看板 ──
        var svcGroup = new GroupBox { Text = "打印相关服务（每 3 秒自动刷新）", Dock = DockStyle.Top, Height = 215 };
        var svcButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        var btnStart = new Button { Text = "启动", AutoSize = true };
        var btnStop = new Button { Text = "停止", AutoSize = true };
        var btnRestart = new Button { Text = "重启", AutoSize = true };
        svcButtons.Controls.Add(btnStart);
        svcButtons.Controls.Add(btnStop);
        svcButtons.Controls.Add(btnRestart);

        _serviceList.Dock = DockStyle.Fill;
        _serviceList.View = View.Details;
        _serviceList.FullRowSelect = true;
        _serviceList.Columns.Add("服务名", 100);
        _serviceList.Columns.Add("显示名称", 320);
        _serviceList.Columns.Add("状态", 100);
        foreach (var (name, display) in WatchedServices.All)
        {
            var item = _serviceList.Items.Add(name);
            item.SubItems.Add(display);
            item.SubItems.Add("…");
            item.Tag = name;
        }

        svcGroup.Controls.Add(_serviceList);
        svcGroup.Controls.Add(svcButtons);

        // 反向停靠：svcGroup 顶、fixGroup 次之、fileGroup 填充
        Controls.Add(fileGroup);
        Controls.Add(fixGroup);
        Controls.Add(svcGroup);

        _timer.Interval = 3000;
        _timer.Tick += (s, e) => { if (!_busy) RefreshStates(); };

        btnStart.Click += async (s, e) => await OperateServiceAsync("启动", n => ServiceHelper.Start(n, 30000));
        btnStop.Click += async (s, e) => await OperateServiceAsync("停止", n => ServiceHelper.Stop(n, 30000));
        btnRestart.Click += async (s, e) => await OperateServiceAsync("重启", n => { ServiceHelper.Stop(n, 30000); ServiceHelper.Start(n, 30000); });
        btnFix.Click += async (s, e) => await OneClickFixAsync();
        btnScan.Click += async (s, e) => await ScanFilesAsync();
        btnClear.Click += async (s, e) => await CleanFilesAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RefreshStates();
        _timer.Start();
    }

    private void RefreshStates()
    {
        foreach (ListViewItem item in _serviceList.Items)
        {
            var name = (string)item.Tag!;
            try
            {
                item.SubItems[2].Text = ServiceHelper.StateText(ServiceHelper.GetState(name));
            }
            catch (Exception ex)
            {
                item.SubItems[2].Text = "查询失败";
                OperationLog.Warn($"查询服务 {name} 状态失败: {ex.Message}");
            }
        }
    }

    private string? SelectedService()
        => _serviceList.SelectedItems.Count == 0 ? null : (string?)_serviceList.SelectedItems[0].Tag;

    /// <summary>线程安全的日志输出到本页结果框。</summary>
    private void R(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        if (_result.IsHandleCreated && _result.InvokeRequired)
            _result.BeginInvoke(new Action(() => _result.AppendText(line)));
        else
            _result.AppendText(line);
    }

    private async Task OperateServiceAsync(string action, Action<string> work)
    {
        var name = SelectedService();
        if (name is null)
        {
            MessageBox.Show("请先在列表中选择一个服务。", "提示");
            return;
        }
        if (_busy) return;
        _busy = true;
        try
        {
            OperationLog.Info($"操作服务: {action} {name}");
            await Task.Run(() => work(name));
            R($"已执行“{action}”：{name} → {ServiceHelper.StateText(ServiceHelper.GetState(name))}");
        }
        catch (Exception ex)
        {
            R($"操作失败: {ex.Message}");
            OperationLog.Error($"{action} {name} 失败: {ex.Message}");
            MessageBox.Show($"{action}服务 {name} 失败：\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            RefreshStates();
        }
    }

    private async Task OneClickFixAsync()
    {
        if (_busy) return;
        bool clear = _clearCheck.Checked;
        var confirmed = clear
            ? MessageBox.Show("将停止打印服务、清空所有未完成的打印任务文件，然后重启服务。\n确定继续？",
                "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            : MessageBox.Show("将重启 Print Spooler 服务（保留打印队列）。确定继续？",
                "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirmed != DialogResult.Yes) return;

        _busy = true;
        try
        {
            R("── 开始 Spooler 一键修复 ──");
            await Task.Run(() => SpoolerController.RestartSpooler(clear, R));
            R("── 一键修复完成 ──");
        }
        catch (Exception ex)
        {
            R("修复失败: " + ex.Message);
            OperationLog.Error("Spooler 一键修复失败: " + ex.Message);
            MessageBox.Show("修复失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            RefreshStates();
        }
    }

    private async Task ScanFilesAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var files = await Task.Run(SpoolerController.GetSpoolFiles);
            _fileList.Items.Clear();
            foreach (var f in files.OrderBy(x => x.FileName))
            {
                var it = _fileList.Items.Add(f.FileName);
                it.SubItems.Add(FormatSize(f.Length));
                it.SubItems.Add(f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            R($"扫描完成：队列目录 {SpoolerController.GetSpoolDirectory()} 中共 {files.Count} 个文件。");
        }
        catch (Exception ex)
        {
            R("扫描失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task CleanFilesAsync()
    {
        if (_busy) return;
        if (MessageBox.Show("清理将停止打印服务、删除队列目录中的全部文件，然后重启服务。\n确定继续？",
            "确认清理", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        _busy = true;
        try
        {
            R("── 开始清理卡死任务 ──");
            await Task.Run(() => SpoolerController.RestartSpooler(true, R));
            R("── 清理完成 ──");
        }
        catch (Exception ex)
        {
            R("清理失败: " + ex.Message);
            OperationLog.Error("清理卡死任务失败: " + ex.Message);
            MessageBox.Show("清理失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            RefreshStates();
        }
        await ScanFilesAsync();
    }

    private static string FormatSize(long len)
        => len >= 1024 * 1024 ? $"{len / 1024.0 / 1024.0:F1} MB"
         : len >= 1024 ? $"{len / 1024.0:F1} KB"
         : $"{len} B";
}
