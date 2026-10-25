using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PrinterTool.Core;

namespace PrinterTool.UI;

/// <summary>日志与备份查看：日志在 logs/，注册表备份在 backups/（按操作时间归档）。</summary>
public sealed class LogPage : TabPage
{
    private readonly TextBox _box = new();

    public LogPage()
    {
        Text = "日志与备份";

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
        var btnRefresh = new Button { Text = "刷新", AutoSize = true };
        var btnLog = new Button { Text = "打开日志目录", AutoSize = true };
        var btnBackup = new Button { Text = "打开备份目录", AutoSize = true };
        buttons.Controls.Add(btnRefresh);
        buttons.Controls.Add(btnLog);
        buttons.Controls.Add(btnBackup);

        _box.Dock = DockStyle.Fill;
        _box.Multiline = true;
        _box.ReadOnly = true;
        _box.ScrollBars = ScrollBars.Both;
        _box.WordWrap = false;
        _box.Font = new Font("Consolas", 9F);
        _box.BackColor = Color.White;

        Controls.Add(_box);
        Controls.Add(buttons);

        btnRefresh.Click += (s, e) => LoadFile();
        btnLog.Click += (s, e) => OpenDir(OperationLog.LogDirectory);
        btnBackup.Click += (s, e) => OpenDir(BackupManager.BackupRoot);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        LoadFile();
    }

    private void LoadFile()
    {
        try
        {
            var dir = OperationLog.LogDirectory;
            var file = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.log").OrderByDescending(f => f).FirstOrDefault()
                : null;
            _box.Text = file is null ? "（暂无日志文件）" : File.ReadAllText(file);
            _box.SelectionStart = _box.Text.Length;
            _box.ScrollToCaret();
        }
        catch (Exception ex)
        {
            _box.Text = "读取日志失败: " + ex.Message;
        }
    }

    private static void OpenDir(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开目录失败: " + ex.Message);
        }
    }
}
