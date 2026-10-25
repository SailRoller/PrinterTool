using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrinterTool.Core;

namespace PrinterTool.UI;

/// <summary>重点一 UI：共享主机一键共享 / 接收主机一键重连 / 常见错误预案。</summary>
public sealed class SharePage : TabPage
{
    private readonly ComboBox _printerBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly Label _shareState = new();
    private readonly CheckBox _guestCheck = new();
    private readonly ComboBox _uncBox = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 340 };
    private readonly TextBox _result = new();
    private bool _busy;

    public SharePage()
    {
        Text = "共享修复";

        // ── 本机作为共享主机 ──
        var hostGroup = new GroupBox { Text = "本机作为共享主机（一键共享）", Dock = DockStyle.Top, Height = 165 };
        var btnRefresh = new Button { Text = "刷新", AutoSize = true };
        var btnShare = new Button { Text = "一键共享所选打印机", AutoSize = true };
        var btnUnshare = new Button { Text = "取消共享", AutoSize = true };
        var hostRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        hostRow.Controls.Add(_printerBox);
        hostRow.Controls.Add(btnRefresh);
        hostRow.Controls.Add(btnShare);
        hostRow.Controls.Add(btnUnshare);

        _shareState.Text = "当前状态: （未选择）";
        _shareState.Dock = DockStyle.Top;
        _shareState.Height = 24;

        _guestCheck.Text = "同时允许来宾访问（启用 Guest 网络登录，降低安全性；不勾选则客户端需账号密码）";
        _guestCheck.Dock = DockStyle.Top;
        _guestCheck.Height = 26;

        var hostTip = new Label
        {
            Text = "一键共享 = 共享所选打印机 + 启动网络发现服务 + 放行“文件和打印机共享”防火墙。完成后把共享路径发给同事，在其电脑上用本工具“一键重连”。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
        };
        hostGroup.Controls.Add(hostTip);
        hostGroup.Controls.Add(_guestCheck);
        hostGroup.Controls.Add(_shareState);
        hostGroup.Controls.Add(hostRow);

        // ── 本机作为接收主机 ──
        var clientGroup = new GroupBox { Text = "本机作为接收主机（一键重连）", Dock = DockStyle.Top, Height = 110 };
        var btnReconnect = new Button { Text = "一键诊断重连", AutoSize = true };
        var btnDiag = new Button { Text = "仅诊断主机", AutoSize = true };
        var clientRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        _uncBox.Items.Add(@"\\主机名\打印机名");
        clientRow.Controls.Add(_uncBox);
        clientRow.Controls.Add(btnReconnect);
        clientRow.Controls.Add(btnDiag);

        var clientTip = new Label
        {
            Text = "输入共享路径（\\\\主机名\\打印机名）。“一键诊断重连”会先诊断连通性，失败时自动按 11b/709/bc4 预案修复并重试。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
        };
        clientGroup.Controls.Add(clientTip);
        clientGroup.Controls.Add(clientRow);

        // ── 常见错误一键修复 ──
        var fixGroup = new GroupBox { Text = "常见错误一键修复（应用于本机）", Dock = DockStyle.Top, Height = 125 };
        var btnFixRpc = new Button { Text = "修复 11b / 709（RPC 认证级别）", AutoSize = true };
        var btnFixBc4 = new Button { Text = "修复 bc4（网络发现/防火墙）", AutoSize = true };
        var btnFixGuest = new Button { Text = "修复来宾访问被安全策略阻止", AutoSize = true };
        var fixRow1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        fixRow1.Controls.Add(btnFixRpc);
        fixRow1.Controls.Add(btnFixBc4);
        fixRow1.Controls.Add(btnFixGuest);

        var btnRestoreGuest = new Button { Text = "恢复来宾默认安全策略", AutoSize = true };
        var fixRow2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        fixRow2.Controls.Add(btnRestoreGuest);

        var fixTip = new Label
        {
            Text = "11b/709 通常需要共享主机与客户端两端都执行修复；“来宾”修复会降低本机 SMB 安全性，用完可用“恢复默认”回滚。所有注册表修改前自动备份。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
        };
        fixGroup.Controls.Add(fixTip);
        fixGroup.Controls.Add(fixRow2);
        fixGroup.Controls.Add(fixRow1);

        // ── 结果 ──
        var resultGroup = new GroupBox { Text = "操作结果", Dock = DockStyle.Fill };
        _result.Dock = DockStyle.Fill;
        _result.Multiline = true;
        _result.ReadOnly = true;
        _result.ScrollBars = ScrollBars.Vertical;
        _result.Font = new Font("Consolas", 9F);
        _result.BackColor = Color.White;
        resultGroup.Controls.Add(_result);

        // 反向停靠：hostGroup 最上，clientGroup 次之，fixGroup 再次，resultGroup 填充
        Controls.Add(resultGroup);
        Controls.Add(fixGroup);
        Controls.Add(clientGroup);
        Controls.Add(hostGroup);

        btnRefresh.Click += async (s, e) => await LoadPrintersAsync();
        _printerBox.SelectedIndexChanged += (s, e) => UpdateShareState();
        btnShare.Click += async (s, e) => await ShareSelectedAsync();
        btnUnshare.Click += async (s, e) => await UnshareSelectedAsync();
        btnReconnect.Click += async (s, e) => await ReconnectAsync();
        btnDiag.Click += async (s, e) => await DiagnoseAsync();
        btnFixRpc.Click += async (s, e) => await RunFixAsync("11b/709 RPC 认证级别", ShareFixes.FixRpcAuthLevel);
        btnFixBc4.Click += async (s, e) => await RunFixAsync("bc4 网络发现", ShareFixes.FixNetworkDiscovery);
        btnFixGuest.Click += async (s, e) => await RunFixAsync("来宾访问", ShareFixes.FixGuestAccess);
        btnRestoreGuest.Click += async (s, e) => await RestoreGuestAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ = LoadPrintersAsync();
    }

    private void R(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        if (_result.IsHandleCreated && _result.InvokeRequired)
            _result.BeginInvoke(new Action(() => _result.AppendText(line)));
        else
            _result.AppendText(line);
    }

    private async Task LoadPrintersAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var printers = await Task.Run(ShareHost.GetLocalPrinters);
            var selected = _printerBox.SelectedItem as string;
            _printerBox.Items.Clear();
            foreach (var p in printers)
                _printerBox.Items.Add(p.Name);
            if (selected is not null)
            {
                var idx = _printerBox.Items.IndexOf(selected);
                if (idx >= 0) _printerBox.SelectedIndex = idx;
            }
            else if (_printerBox.Items.Count > 0)
            {
                _printerBox.SelectedIndex = 0;
            }
            UpdateShareState();
        }
        catch (Exception ex)
        {
            R("加载打印机列表失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void UpdateShareState()
    {
        if (_printerBox.SelectedItem is not string name)
        {
            _shareState.Text = "当前状态: （未选择）";
            return;
        }
        var p = ShareHost.GetLocalPrinters()
            .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        _shareState.Text = p is null
            ? $"当前状态: 未找到 {name}"
            : p.IsShared
                ? $"当前状态: 已共享为 \\\\{Environment.MachineName}\\{p.ShareName}"
                : "当前状态: 未共享";
    }

    private async Task ShareSelectedAsync()
    {
        if (_busy) return;
        if (_printerBox.SelectedItem is not string name)
        {
            MessageBox.Show("请先选择要共享的打印机。", "提示");
            return;
        }
        var guest = _guestCheck.Checked;
        var confirm = guest
            ? $"确定共享打印机“{name}”？\n\n已勾选“允许来宾访问”：将启用 Guest 账户并允许来宾网络登录（降低本机安全性）。"
            : $"确定共享打印机“{name}”？\n\n客户端连接时将需要本机的账号密码。";
        if (MessageBox.Show(confirm, "一键共享", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _busy = true;
        try
        {
            R($"── 一键共享: {name} ──");
            var result = await Task.Run(() => ShareHost.SharePrinter(name, guest, R));
            R(result.Summary);
            R("── 一键共享完成 ──");
            MessageBox.Show($"共享完成。\n\n共享路径：{result.SharePath}\n\n把它发给同事，在其电脑上用本工具“一键重连”。",
                "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            R("共享失败: " + ex.Message);
            OperationLog.Error($"一键共享失败: {ex.Message}");
            MessageBox.Show("共享失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            await LoadPrintersAsync();
        }
    }

    private async Task UnshareSelectedAsync()
    {
        if (_busy) return;
        if (_printerBox.SelectedItem is not string name)
        {
            MessageBox.Show("请先选择打印机。", "提示");
            return;
        }
        if (MessageBox.Show($"确定取消共享“{name}”？", "取消共享", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _busy = true;
        try
        {
            await Task.Run(() => ShareHost.UnsharePrinter(name, R));
        }
        catch (Exception ex)
        {
            R("取消共享失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
            await LoadPrintersAsync();
        }
    }

    private async Task ReconnectAsync()
    {
        if (_busy) return;
        var unc = _uncBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(unc))
        {
            MessageBox.Show("请输入共享路径，格式 \\\\主机名\\打印机名。", "提示");
            return;
        }
        _busy = true;
        try
        {
            R($"── 一键诊断重连: {unc} ──");
            await Task.Run(() => ShareClient.Reconnect(unc, autoFix: true, R));
            R("── 重连完成 ──");
            if (!_uncBox.Items.Contains(unc)) _uncBox.Items.Add(unc);
            MessageBox.Show("打印机重连成功。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            R("重连失败: " + ex.Message);
            OperationLog.Error($"一键重连失败 ({unc}): {ex.Message}");
            MessageBox.Show(ex.Message, "重连失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task DiagnoseAsync()
    {
        if (_busy) return;
        var unc = _uncBox.Text?.Trim();
        var parsed = ShareClient.ParseUnc(unc ?? "");
        if (parsed is null)
        {
            MessageBox.Show("请输入格式正确的主机地址，例如 \\\\OFFICE-PC\\HP 1110。", "提示");
            return;
        }
        _busy = true;
        try
        {
            var host = parsed.Value.Host;
            R($"── 诊断主机: {host} ──");
            var (ping, smb) = await Task.Run(() => ShareClient.Diagnose(host));
            R($"Ping: {(ping ? "通" : "不通（可能禁 ICMP）")}");
            R($"TCP 445 (SMB): {(smb ? "通" : "不通——检查主机端共享/防火墙（可在主机端“一键共享”）")}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunFixAsync(string title, Action<Action<string>> fix)
    {
        if (_busy) return;
        if (MessageBox.Show($"确定在本机应用“{title}”修复？\n（注册表修改前会自动备份）", "确认修复",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _busy = true;
        try
        {
            R($"── 应用修复: {title} ──");
            await Task.Run(() => fix(R));
            R("── 修复完成 ──");
            MessageBox.Show($"“{title}”修复已应用。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            R("修复失败: " + ex.Message);
            OperationLog.Error($"应用修复 {title} 失败: {ex.Message}");
            MessageBox.Show("修复失败：\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RestoreGuestAsync()
    {
        if (_busy) return;
        if (MessageBox.Show("将恢复“拒绝来宾从网络访问本机”的默认安全策略，并禁用 Guest 账户。\n确定继续？",
                "恢复默认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        _busy = true;
        try
        {
            R("── 恢复来宾默认安全策略 ──");
            await Task.Run(() => ShareFixes.RestoreGuestPolicy(disableGuestAccount: true, R));
            R("── 恢复完成 ──");
        }
        catch (Exception ex)
        {
            R("恢复失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }
}
