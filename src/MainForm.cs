using System;
using System.Drawing;
using System.Windows.Forms;
namespace WinForms
{
    public sealed partial class MainForm : Form
    {
        private static readonly Color Navy=Color.FromArgb(15,23,42),Accent=Color.FromArgb(37,99,235),Canvas=Color.FromArgb(244,247,251);
        private static readonly Font Ui=new Font("Microsoft YaHei UI",9.5F,FontStyle.Regular),UiActive=new Font("Microsoft YaHei UI",10F,FontStyle.Bold);
        private readonly Panel content=new Panel{Dock=DockStyle.Fill};
        public MainForm()
        {
            Text=ProductInfo.Name+" BAT 自动化独立版 V"+ProductInfo.Version+"-BAT";Width=1220;Height=790;MinimumSize=new Size(1000,670);StartPosition=FormStartPosition.CenterScreen;Font=Ui;BackColor=Canvas;AutoScaleMode=AutoScaleMode.Dpi;
            Controls.Add(content);BuildAutomation();
        }
        private bool ConfirmAction(string title,string message)
        {return MessageBox.Show(this,message+"\r\n\r\n确认执行？",title,MessageBoxButtons.YesNo,MessageBoxIcon.Warning)==DialogResult.Yes;}
        private Panel Page(string name, string description, out FlowLayoutPanel toolbar)
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
            Panel header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Canvas };
            Label title = new Label { Text = name, Location = new Point(25, 13), AutoSize = true, ForeColor = Navy,
                Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold) };
            Label subtitle = new Label { Text = description, Location = new Point(27, 55), AutoSize = true,
                ForeColor = Color.FromArgb(99, 110, 125), Font = new Font("Microsoft YaHei UI", 9F) };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, BackColor = Canvas, Padding = new Padding(21, 8, 5, 5),
                WrapContents = false, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight };
            Panel body = new Panel { Name = "pageBody", Dock = DockStyle.Fill, BackColor = Canvas };
            p.Controls.Add(body);
            p.Controls.Add(toolbar);
            p.Controls.Add(header);
            content.Controls.Add(p);
            return p;
        }
        private Button Action(FlowLayoutPanel bar, string text, EventHandler handler, bool primary)
        {
            Button b = new Button { Text = text, AutoSize = false, Height = 36, Width = Math.Max(116, text.Length * 13 + 29),
                Margin = new Padding(4, 2, 8, 2), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Navy };
            b.FlatAppearance.BorderColor = Color.FromArgb(204, 216, 231);
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(31, 87, 186) : Color.FromArgb(237, 243, 253);
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.Click += handler;
            bar.Controls.Add(b);
            return b;
        }
        private RichTextBox ReadBox()
        {
            return new RichTextBox { ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 10F), BackColor = Color.White, ForeColor = Color.FromArgb(37, 50, 68),
                WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical, Dock = DockStyle.Fill };
        }
        private Panel ContentCard(Control child)
        {
            Panel outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 6, 24, 20), BackColor = Canvas };
            Panel inner = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18) };
            child.Dock = DockStyle.Fill;
            inner.Controls.Add(child);
            outer.Controls.Add(inner);
            return outer;
        }
        private DataGridView Grid()
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.ReadOnly = true;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.RowHeadersVisible = false;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.ColumnHeadersHeight = 38;
            g.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            g.RowTemplate.Height = 33;
            g.EnableHeadersVisualStyles = false;
            g.GridColor = Color.FromArgb(229, 234, 242);
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(71,85,105);
            g.ColumnHeadersDefaultCellStyle.Font = UiActive;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248,250,252);
            g.DefaultCellStyle.ForeColor = Navy;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            g.DefaultCellStyle.SelectionForeColor = Navy;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(244, 247, 251);
            g.Font = Ui;
            return g;
        }
        private void AddColumn(DataGridView g, string name, string title, int width, bool fill)
        {
            DataGridViewTextBoxColumn c = new DataGridViewTextBoxColumn();
            c.Name = name;
            c.HeaderText = title;
            c.Width = width;
            if (fill) { c.MinimumWidth = Math.Min(width, 280); c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; }
            g.Columns.Add(c);
        }
        private Form NewDialog(string title, int width, int height)
        {
            return new Form { Text = title, ClientSize = new Size(width,height), StartPosition=FormStartPosition.CenterParent,
                MinimumSize=new Size(Math.Min(width,590),Math.Min(height,440)), Font=Ui, BackColor=Canvas, AutoScaleMode=AutoScaleMode.Font };
        }
        private Label FormLabel(string caption, int x, int y, int width)
        {
            return new Label { Text=caption, Left=x, Top=y, Width=width, Height=27, TextAlign=ContentAlignment.MiddleLeft };
        }
        private TextBox FormText(string value, int x, int y, int width)
        {
            return new TextBox { Text=value, Left=x, Top=y, Width=width, Height=27 };
        }
        private Button FormButton(string title, int x, int y, int width, EventHandler action)
        {
            Button b = new Button { Text=title, Left=x,Top=y,Width=width,Height=34,
                Font=Ui, FlatStyle=FlatStyle.Flat, BackColor=Color.White,
                ForeColor=Navy, Cursor=Cursors.Hand };
            b.FlatAppearance.BorderColor=Color.FromArgb(204,216,231);
            b.FlatAppearance.MouseOverBackColor=Color.FromArgb(237,243,253);
            b.Click+=action; return b;
        }
    }
}
