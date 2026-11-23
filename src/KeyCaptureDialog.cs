using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WinForms
{
    // Captures a single key or chord only while this dialog's capture field has focus.
    // No global keyboard hook, key logging or background monitoring is installed.
    internal sealed class HotkeyCaptureTextBox : TextBox
    {
        protected override bool IsInputKey(Keys keyData)
        {
            // Tab/arrows are normally treated as dialog navigation, not key input.
            // Here we need to capture them as actual automation steps.
            return true;
        }
    }

    internal static class KeyCaptureDialog
    {
        public static string Capture(IWin32Window owner)
        {
            string result = null;
            using (Form f = new Form())
            {
                f.Text = "录入键盘按键 / 组合键";
                f.ClientSize = new Size(560, 280);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Color.FromArgb(246, 248, 252);
                f.Font = new Font("DengXian", 10F);
                f.AutoScaleMode = AutoScaleMode.Font;

                Label instructions = new Label {
                    Left = 20, Top = 17, Width = 525, Height = 46,
                    Text = "单击下方输入框，然后直接按键：例如 Enter、Tab、Space、Ctrl+C。\r\n录入成功后会自动填写参数 1，无需手动输入按键名称。"
                };
                HotkeyCaptureTextBox box = new HotkeyCaptureTextBox {
                    Left = 20, Top = 72, Width = 520, Height = 33,
                    ReadOnly = true, ShortcutsEnabled = false,
                    Text = "请在这里按下按键或组合键…",
                    BackColor = Color.White, ForeColor = Color.FromArgb(27, 55, 100)
                };
                Label message = new Label {
                    Left = 20, Top = 113, Width = 520, Height = 32,
                    Text = "仅在本窗口录入，不会监控其他软件的键盘操作。",
                    ForeColor = Color.FromArgb(91, 103, 120)
                };
                f.Controls.Add(instructions);
                f.Controls.Add(box);
                f.Controls.Add(message);

                box.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    string key = ToKeyName(e.KeyCode);
                    if (key == null)
                    {
                        if (IsModifier(e.KeyCode))
                        {
                            message.Text = "请按住 Ctrl / Alt / Shift，再按一个其他按键。";
                        }
                        else
                        {
                            message.Text = "此键暂不支持录入。特殊快捷键可用下方按钮，或直接填参数。";
                        }
                        return;
                    }
                    List<string> parts = new List<string>();
                    if (e.Control) parts.Add("CTRL");
                    if (e.Alt) parts.Add("ALT");
                    if (e.Shift) parts.Add("SHIFT");
                    parts.Add(key);
                    result = String.Join("+", parts.ToArray());
                    f.DialogResult = DialogResult.OK;
                    f.Close();
                };

                Label quick = new Label {
                    Left = 20, Top = 149, Width = 510, Height = 23,
                    Text = "常用按键和系统截获的快捷键（点一下直接录入）："
                };
                f.Controls.Add(quick);
                string[] shortcuts = { "ENTER", "TAB", "SPACE", "ESC", "CTRL+C", "CTRL+V", "ALT+TAB", "WIN+R" };
                for (int i = 0; i < shortcuts.Length; i++)
                {
                    string value = shortcuts[i];
                    int col = i % 4;
                    int row = i / 4;
                    Button button = new Button {
                        Text = value, Left = 20 + col * 131, Top = 179 + row * 36,
                        Width = 122, Height = 30,
                        FlatStyle = FlatStyle.Flat, BackColor = Color.White,
                        ForeColor = Color.FromArgb(23, 35, 54)
                    };
                    button.FlatAppearance.BorderColor = Color.FromArgb(204, 216, 231);
                    button.Click += delegate {
                        result = value;
                        f.DialogResult = DialogResult.OK;
                        f.Close();
                    };
                    f.Controls.Add(button);
                }
                Button cancel = new Button {
                    Text = "取消", Left = 445, Top = 249, Width = 95, Height = 27
                };
                cancel.Click += delegate { f.DialogResult = DialogResult.Cancel; f.Close(); };
                f.Controls.Add(cancel);
                f.Shown += delegate { box.Focus(); box.Select(0, 0); };
                return f.ShowDialog(owner) == DialogResult.OK ? result : null;
            }
        }

        private static bool IsModifier(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                key == Keys.LWin || key == Keys.RWin;
        }

        private static string ToKeyName(Keys key)
        {
            int vk = (int)key;
            if (vk >= (int)Keys.A && vk <= (int)Keys.Z) return ((char)vk).ToString();
            if (vk >= (int)Keys.D0 && vk <= (int)Keys.D9) return ((char)vk).ToString();
            if (vk >= (int)Keys.NumPad0 && vk <= (int)Keys.NumPad9)
                return "NUMPAD" + (vk - (int)Keys.NumPad0);
            if (vk >= (int)Keys.F1 && vk <= (int)Keys.F12)
                return "F" + (vk - (int)Keys.F1 + 1);
            switch (key)
            {
                case Keys.Enter: return "ENTER";
                case Keys.Tab: return "TAB";
                case Keys.Space: return "SPACE";
                case Keys.Escape: return "ESC";
                case Keys.Up: return "UP";
                case Keys.Down: return "DOWN";
                case Keys.Left: return "LEFT";
                case Keys.Right: return "RIGHT";
                case Keys.Back: return "BACKSPACE";
                case Keys.Delete: return "DELETE";
                case Keys.Insert: return "INSERT";
                case Keys.Home: return "HOME";
                case Keys.End: return "END";
                case Keys.PageUp: return "PAGEUP";
                case Keys.PageDown: return "PAGEDOWN";
                default: return null;
            }
        }
    }
}
