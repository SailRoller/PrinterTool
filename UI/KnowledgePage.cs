using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrinterTool.UI;

/// <summary>打印常识：固定内容参考页（型号后缀字母含义、端口类型、实用小知识）。</summary>
public sealed class KnowledgePage : TabPage
{
    public KnowledgePage()
    {
        Text = "打印常识";

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(4) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(8, 8, 8, 8) };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;

        void Add(Control c, int marginTop = 10)
        {
            c.Margin = new Padding(4, marginTop, 4, 2);
            c.Dock = DockStyle.Top;
            stack.Controls.Add(c, 0, row++);
        }

        // ── 标题 ──
        Add(new Label
        {
            Text = "打印机型号后缀字母含义",
            Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold),
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 40, 62, 98),
        }, 4);
        Add(new Label
        {
            Text = "常见于惠普等喷墨/激光机型的命名中（如 M277dw、M425fdn、P3015dn），字母组合即代表功能组合：",
            AutoSize = true,
            ForeColor = Color.DimGray,
        }, 0);

        // ── 字母表 ──
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
            BackColor = Color.FromArgb(255, 233, 238, 246),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void Row(string letter, string full, string meaning, bool header = false)
        {
            var font = header
                ? new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
                : new Font("Microsoft YaHei UI", 9F);
            var letterLabel = new Label { Text = letter, Font = new Font("Microsoft YaHei UI", 9.75F, FontStyle.Bold), AutoSize = true, Margin = new Padding(6, 4, 4, 4) };
            var fullLabel = new Label { Text = full, Font = font, AutoSize = true, Margin = new Padding(4, 4, 4, 4), ForeColor = Color.FromArgb(255, 70, 100, 160) };
            var meaningLabel = new Label { Text = meaning, Font = font, AutoSize = true, Margin = new Padding(4, 4, 8, 4) };
            if (header)
            {
                letterLabel.ForeColor = Color.FromArgb(255, 40, 62, 98);
                fullLabel.ForeColor = Color.FromArgb(255, 40, 62, 98);
                meaningLabel.ForeColor = Color.FromArgb(255, 40, 62, 98);
            }
            table.Controls.Add(letterLabel, 0, table.RowCount);
            table.Controls.Add(fullLabel, 1, table.RowCount);
            table.Controls.Add(meaningLabel, 2, table.RowCount);
            table.RowCount++;
        }

        Row("字母", "全称", "含义", header: true);
        Row("n", "net", "具备网络功能（有线以太网口）");
        Row("w", "WiFi", "具备无线网络连接功能");
        Row("d", "Duplex", "支持自动双面打印");
        Row("f", "Fax", "带传真/电话功能");
        Row("h", "Handle", "带手柄");
        Row("L", "Large tray", "包含扩展纸盒：250 页大容量纸盒");
        Row("T", "Tray", "包含扩展纸盒：500 页大容量输入纸盒");
        Add(table, 8);

        // ── 示例 ──
        Add(new Label
        {
            Text = "例：惠普 M277dw —— “dw” 即代表该型号机型拥有双面打印(d)与无线网络连接(w)功能；\nM425fdn 则表示支持传真(f)、双面打印(d)与有线网络(n)。",
            AutoSize = true,
            BackColor = Color.FromArgb(255, 255, 246, 227),
            Padding = new Padding(8, 6, 8, 6),
        }, 8);

        // ── 实用小知识 ──
        Add(new Label
        {
            Text = "实用小知识",
            Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold),
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 40, 62, 98),
        }, 16);
        Add(new Label
        {
            Text =
                "• 端口类型：USB001 等为 USB 连接；WSD-xx 为自动发现的网络设备；IP 地址/RAW 9100 为网络打印机标准端口；\n" +
                "  PORTPROMPT: 与 FILE: 用于“打印到文件”（如 Microsoft Print to PDF、XPS）。\n" +
                "• 网络打印机建议使用标准 TCP/IP 端口并绑定静态 IP；WSD 端口的地址可能变化，是打印机“莫名脱机”的常见原因。\n" +
                "• 删除打印机 ≠ 删除驱动。重装驱动前，建议先在“驱动管理”页彻底删除旧驱动，再到“残留清理”页清理注册表残留。\n" +
                "• 打印机显示“脱机/错误”时，多为连接中断或队列卡死，可先到“服务管理”页执行 Spooler 一键修复。",
            AutoSize = true,
        }, 2);

        scroll.Controls.Add(stack);
        Controls.Add(scroll);
    }
}
