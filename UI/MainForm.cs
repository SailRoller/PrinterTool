using System;
using System.Drawing;
using System.Security.Principal;
using System.Windows.Forms;
using PrinterTool.Core;

namespace PrinterTool.UI;

public sealed class MainForm : Form
{
    private readonly TextBox _logBox = new();

    public MainForm()
    {
        Text = "打印机管理工具（内部版） v0.4";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1020, 700);
        MinimumSize = new Size(880, 600);
        Font = new Font("Microsoft YaHei UI", 9F);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new ServicePage());
        tabs.TabPages.Add(new SharePage());
        tabs.TabPages.Add(new DriverPage());
        tabs.TabPages.Add(new ResidualPage());
        tabs.TabPages.Add(new KnowledgePage());
        tabs.TabPages.Add(new LogPage());

        var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 150 };
        var logButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2, 4, 2, 0) };
        var logLabel = new Label { Text = "操作日志（全部写入 logs/ 目录，注册表修改前自动备份到 backups/）", AutoSize = true, Margin = new Padding(2, 6, 12, 0) };
        var btnCopy = new Button { Text = "复制全部", AutoSize = true };
        var btnClearView = new Button { Text = "清空显示", AutoSize = true };
        logButtons.Controls.Add(logLabel);
        logButtons.Controls.Add(btnCopy);
        logButtons.Controls.Add(btnClearView);

        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.Font = new Font("Consolas", 9F);
        _logBox.BackColor = Color.White;

        logPanel.Controls.Add(_logBox);
        logPanel.Controls.Add(logButtons);

        // WinForms 反向停靠：后添加的先停靠，因此先加 Fill 再加 Bottom
        Controls.Add(tabs);
        Controls.Add(logPanel);

        btnCopy.Click += (s, e) =>
        {
            try { Clipboard.SetText(string.IsNullOrEmpty(_logBox.Text) ? " " : _logBox.Text); } catch { /* 忽略 */ }
        };
        btnClearView.Click += (s, e) => _logBox.Clear();

        OperationLog.MessageAdded += AppendLog;
        Load += (s, e) =>
        {
            OperationLog.Info($"程序启动。管理员权限: {IsAdmin()}；日志目录: {OperationLog.LogDirectory}");
        };
    }

    private void AppendLog(string line)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(new Action(() => _logBox.AppendText(line + Environment.NewLine)));
    }

    private static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
