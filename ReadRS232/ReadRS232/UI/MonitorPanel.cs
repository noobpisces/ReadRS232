using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ReadRS232.Core;

namespace ReadRS232.UI
{
    /// <summary>
    /// Bảng Monitor: xem dữ liệu thô RX/TX theo thời gian thực (dạng chữ hoặc HEX),
    /// và gửi thử lệnh / giả lập dữ liệu nhận được để kiểm tra cấu hình.
    /// </summary>
    public sealed class MonitorPanel : Panel
    {
        readonly ListBox _list;
        readonly ComboBox _filter, _sendMode;
        readonly CheckBox _hex, _pause;
        readonly TextBox _input;
        readonly List<LogEntry> _all = new List<LogEntry>();
        readonly Timer _pump = new Timer { Interval = 120 };
        Func<IReadOnlyList<RouteRuntime>> _routes;
        public int MaxLines { get; set; } = 2000;

        public MonitorPanel()
        {
            BackColor = Theme.Bg;
            Padding = new Padding(Theme.S(10), 0, Theme.S(10), Theme.S(10));

            // ---- thanh công cụ ----
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, 0, 0, Theme.S(6)) };
            _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(130), Margin = new Padding(0, Theme.S(2), Theme.S(8), 0) };
            Theme.StyleCombo(_filter);
            _filter.SelectedIndexChanged += (s, e) => Rebuild();
            _hex = Check("HEX");
            _hex.CheckedChanged += (s, e) => Rebuild();
            _pause = Check("Tạm dừng");
            var clear = ToolButton("Xoá");
            clear.Click += (s, e) => { _all.Clear(); _list.Items.Clear(); };
            var copy = ToolButton("Chép");
            copy.Click += (s, e) => CopyAll();
            bar.Controls.AddRange(new Control[] { _filter, _hex, _pause, clear, copy });

            // ---- danh sách log ----
            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0x10, 0x12, 0x14),
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                Font = Theme.Mono,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Theme.Mono.Height + Theme.S(3),
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended,
                HorizontalScrollbar = true
            };
            _list.DrawItem += DrawEntry;
            _list.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopySelected(); e.Handled = true; } };

            // ---- ô gửi ----
            var send = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, BackColor = Theme.Bg, Padding = new Padding(0, Theme.S(6), 0, 0) };
            send.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            send.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            send.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _input = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(2), Theme.S(6), 0) };
            Theme.StyleText(_input);
            Theme.Placeholder(_input, @"Dữ liệu… hỗ trợ \r \n \t \xHH   (Enter để gửi)");
            _input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSend(); } };
            _sendMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(150), Margin = new Padding(0, Theme.S(1), Theme.S(6), 0) };
            Theme.StyleCombo(_sendMode);
            _sendMode.Items.AddRange(new object[] { "Gửi tới thiết bị", "Giả lập thiết bị gửi" });
            _sendMode.SelectedIndex = 1;
            var btn = Theme.Button("Gửi");
            btn.Margin = new Padding(0);
            btn.Click += (s, e) => DoSend();
            send.Controls.Add(_input, 0, 0);
            send.Controls.Add(_sendMode, 1, 0);
            send.Controls.Add(btn, 2, 0);

            Controls.Add(_list);
            Controls.Add(send);
            Controls.Add(bar);

            _pump.Tick += (s, e) => Pump();
            _pump.Start();
        }

        static Size ToolSize(string text) => new Size(TextRenderer.MeasureText(text, Theme.UI).Width + Theme.S(18), Theme.S(26));

        static Button ToolButton(string text)
        {
            var b = Theme.Button(text);
            b.AutoSize = false;
            b.Padding = new Padding(0);
            b.Size = ToolSize(text);
            b.Margin = new Padding(0, 0, Theme.S(6), 0);
            return b;
        }

        CheckBox Check(string text) => new CheckBox
        {
            Text = text,
            Appearance = Appearance.Button,
            FlatStyle = FlatStyle.Flat,
            AutoSize = false,
            Size = ToolSize(text),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface2,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, Theme.S(6), 0),
            FlatAppearance = { BorderColor = Theme.Border, CheckedBackColor = Color.FromArgb(0x1F, 0x4A, 0x73) },
            Cursor = Cursors.Hand
        };

        public void SetRoutes(Func<IReadOnlyList<RouteRuntime>> routes)
        {
            _routes = routes;
            string keep = _filter.SelectedItem as string;
            _filter.Items.Clear();
            _filter.Items.Add("Tất cả tuyến");
            foreach (var r in routes()) _filter.Items.Add(r.Name);
            _filter.SelectedItem = keep != null && _filter.Items.Contains(keep) ? keep : "Tất cả tuyến";
        }

        string FilterRoute => _filter.SelectedIndex > 0 ? (string)_filter.SelectedItem : null;

        void Pump()
        {
            if (!Visible) { while (AppLog.TryTake(out var e0)) Keep(e0); return; }
            var fresh = new List<LogEntry>();
            while (AppLog.TryTake(out var e)) { Keep(e); fresh.Add(e); }
            if (fresh.Count == 0 || _pause.Checked) return;
            string f = FilterRoute;
            bool atBottom = _list.Items.Count == 0 || _list.TopIndex + VisibleRows() >= _list.Items.Count - 1;
            _list.BeginUpdate();
            foreach (var e in fresh) if (f == null || e.Route == f) _list.Items.Add(e);
            while (_list.Items.Count > MaxLines) _list.Items.RemoveAt(0);
            _list.EndUpdate();
            if (atBottom && _list.Items.Count > 0) _list.TopIndex = Math.Max(0, _list.Items.Count - VisibleRows());
        }

        void Keep(LogEntry e)
        {
            _all.Add(e);
            if (_all.Count > MaxLines * 2) _all.RemoveRange(0, _all.Count - MaxLines);
        }

        int VisibleRows() => Math.Max(1, _list.ClientSize.Height / _list.ItemHeight);

        void Rebuild()
        {
            string f = FilterRoute;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var e in _all.Where(x => f == null || x.Route == f).Skip(Math.Max(0, _all.Count - MaxLines))) _list.Items.Add(e);
            _list.EndUpdate();
            if (_list.Items.Count > 0) _list.TopIndex = Math.Max(0, _list.Items.Count - VisibleRows());
        }

        // ---------------- vẽ 1 dòng ----------------
        static readonly Dictionary<LogKind, (string Tag, Color Color)> Tags = new Dictionary<LogKind, (string, Color)>
        {
            [LogKind.Rx] = ("RX ◀", Theme.Rx),
            [LogKind.Tx] = ("TX ▶", Theme.Tx),
            [LogKind.Value] = ("VAL ", Theme.Ok),
            [LogKind.Out] = ("OUT ", Theme.Accent),
            [LogKind.Info] = ("·   ", Theme.Muted),
            [LogKind.Warn] = ("WARN", Theme.Warn),
            [LogKind.Error] = ("ERR ", Theme.Err),
        };

        string Body(LogEntry e)
        {
            if (e.Data == null) return e.Text;
            string d = _hex.Checked ? TextUtil.ToHex(e.Data, 512) : TextUtil.Visualize(Encoding.GetEncoding(28591).GetString(e.Data, 0, Math.Min(e.Data.Length, 2048)));
            return string.IsNullOrEmpty(e.Text) ? d : e.Text + "  " + d;
        }

        void DrawEntry(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;
            var le = (LogEntry)_list.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? Color.FromArgb(0x24, 0x3B, 0x55) : _list.BackColor)) e.Graphics.FillRectangle(bg, e.Bounds);
            var t = Tags[le.Kind];
            int x = e.Bounds.X + Theme.S(4);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            void Part(string s, Color c)
            {
                TextRenderer.DrawText(e.Graphics, s, Theme.Mono, new Point(x, e.Bounds.Y + 1), c, flags);
                x += TextRenderer.MeasureText(e.Graphics, s, Theme.Mono, Size.Empty, flags).Width;
            }
            Part(le.Time.ToString("HH:mm:ss.fff") + "  ", Theme.Muted);
            if (FilterRoute == null && !string.IsNullOrEmpty(le.Route)) Part(Pad(le.Route, 12) + " ", Color.FromArgb(0xB3, 0x9D, 0xDB));
            Part(t.Tag + "  ", t.Color);
            Part(Body(le), le.Kind == LogKind.Info ? Theme.Muted : le.Kind == LogKind.Error ? Theme.Err : Theme.Text);
        }

        static string Pad(string s, int n) => s.Length > n ? s.Substring(0, n - 1) + "…" : s.PadRight(n);

        string LineText(LogEntry e) => $"{e.Time:HH:mm:ss.fff}\t{e.Route}\t{e.Kind}\t{Body(e)}";

        void CopySelected()
        {
            var lines = _list.SelectedItems.Cast<LogEntry>().Select(LineText).ToArray();
            if (lines.Length > 0) Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        void CopyAll()
        {
            var lines = _list.Items.Cast<LogEntry>().Select(LineText).ToArray();
            if (lines.Length > 0) Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }

        void DoSend()
        {
            var routes = _routes?.Invoke();
            if (routes == null || routes.Count == 0) return;
            var r = FilterRoute == null ? routes[0] : routes.FirstOrDefault(x => x.Name == FilterRoute) ?? routes[0];
            string text = _input.Text;
            if (text.Length == 0) return;
            try
            {
                if (_sendMode.SelectedIndex == 0) r.SendCommand(text, force: true, quiet: false);
                else r.InjectReceived(text);
                _input.SelectAll();
            }
            catch (Exception ex) { AppLog.Write(r.Name, LogKind.Error, "Gửi lỗi: " + ex.Message); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _pump.Dispose();
            base.Dispose(disposing);
        }
    }
}
