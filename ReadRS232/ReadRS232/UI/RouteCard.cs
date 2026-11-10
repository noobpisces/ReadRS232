using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using ReadRS232.Core;
using LinkState = ReadRS232.Core.LinkState;

namespace ReadRS232.UI
{
    /// <summary>Thẻ hiển thị 1 tuyến ở màn hình chính: trạng thái, cổng, giá trị lớn, nút Đọc.</summary>
    public sealed class RouteCard : Control
    {
        public RouteRuntime Route { get; private set; }

        readonly Label _name, _value, _status;
        readonly ComboBox _port;
        readonly Label _sourceLabel;
        readonly Button _read;
        readonly ToolTip _tip;
        readonly TableLayoutPanel _grid;
        readonly Timer _flash = new Timer { Interval = 30 };
        DateTime _flashStart;
        bool _suppressPortEvent;
        Color _dot = Theme.Muted;

        /// <summary>Người dùng đổi cổng ở thẻ → MainForm lưu cấu hình và khởi động lại tuyến.</summary>
        public event Action<RouteCard, string> PortChangeRequested;
        public event Action<RouteCard> SettingsRequested;

        public RouteCard(RouteRuntime route, ToolTip tip)
        {
            _tip = tip;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(12), Theme.S(10));

            var grid = _grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _name = Theme.Label("", Theme.Bold);
            _name.Padding = new Padding(Theme.S(14), Theme.S(4), 0, 0);   // chừa chỗ chấm trạng thái
            _name.AutoEllipsis = true;
            grid.Controls.Add(_name, 0, 0);

            _port = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Theme.S(110), DrawMode = DrawMode.OwnerDrawFixed, Margin = new Padding(0) };
            Theme.StyleCombo(_port);
            _port.DrawItem += DrawPortItem;
            _port.DropDown += (s, e) => FillPorts(true);
            _port.SelectionChangeCommitted += (s, e) =>
            {
                if (_suppressPortEvent || !(_port.SelectedItem is string p)) return;
                PortChangeRequested?.Invoke(this, p);
            };
            _sourceLabel = Theme.Label("", Theme.Small, Theme.Muted);
            _sourceLabel.Margin = new Padding(0, Theme.S(4), 0, 0);
            var right0 = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0) };
            right0.Controls.Add(_port);
            right0.Controls.Add(_sourceLabel);
            grid.Controls.Add(right0, 1, 0);

            _value = Theme.Label("---", Theme.Value, Theme.Text);
            _value.Margin = new Padding(0, Theme.S(2), 0, 0);
            _value.Cursor = Cursors.Hand;
            _value.DoubleClick += (s, e) => Safe(() => Route.Resend());
            tip.SetToolTip(_value, "Nhấp đúp để gửi lại giá trị này");
            grid.Controls.Add(_value, 0, 1);
            grid.SetColumnSpan(_value, 2);

            _status = Theme.Label("", Theme.Small, Theme.Muted);
            _status.AutoEllipsis = true;
            _status.AutoSize = false;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Margin = new Padding(0);
            grid.Controls.Add(_status, 0, 2);

            _read = Theme.Button("Đọc", primary: true);
            _read.Margin = new Padding(Theme.S(8), 0, 0, 0);
            _read.Click += (s, e) => Safe(() => Route.RequestRead());
            grid.Controls.Add(_read, 1, 2);

            Controls.Add(grid);

            var menu = Theme.Menu();
            menu.Items.Add(Theme.Item("Đọc ngay", (s, e) => Safe(() => Route.RequestRead())));
            menu.Items.Add(Theme.Item("Gửi lại giá trị gần nhất", (s, e) => Safe(() => Route.Resend())));
            menu.Items.Add(Theme.Item("Chép giá trị", (s, e) => { if (Route.LastValue != null) Clipboard.SetText(Route.LastValue.Value); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Theme.Item("Cài đặt tuyến này…", (s, e) => SettingsRequested?.Invoke(this)));
            ContextMenuStrip = menu;
            foreach (Control c in new Control[] { grid, _name, _value, _status }) c.ContextMenuStrip = menu;

            _flash.Tick += (s, e) => AnimateFlash();
            Bind(route);
        }

        public void Bind(RouteRuntime route)
        {
            if (Route != null) Route.Changed -= OnRouteChanged;
            Route = route;
            Route.Changed += OnRouteChanged;
            _name.Text = route.Name;
            bool serial = route.Config.Source is SerialEndpointConfig;
            _port.Visible = serial;
            _sourceLabel.Visible = !serial;
            _sourceLabel.Text = route.Config.Source.Describe();
            FillPorts(false);
            RefreshView();
        }

        void FillPorts(bool keepOpen)
        {
            if (!(Route.Config.Source is SerialEndpointConfig sc)) return;
            _suppressPortEvent = true;
            var names = PortCatalog.Names().ToList();
            if (!string.IsNullOrEmpty(sc.PortName) && !names.Contains(sc.PortName, StringComparer.OrdinalIgnoreCase))
                names.Insert(0, sc.PortName);    // cổng đã chọn nhưng đang rút ra: vẫn hiện
            _port.BeginUpdate();
            _port.Items.Clear();
            foreach (var n in names) _port.Items.Add(n);
            _port.SelectedItem = names.FirstOrDefault(n => string.Equals(n, sc.PortName, StringComparison.OrdinalIgnoreCase));
            _port.DropDownWidth = Math.Max(_port.Width, Theme.S(300));
            _port.EndUpdate();
            _suppressPortEvent = false;
        }

        public void OnPortsChanged() { if (!_port.DroppedDown) FillPorts(false); }

        void DrawPortItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            string port = (string)_port.Items[e.Index];
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool sel = (e.State & DrawItemState.Selected) != 0 && !edit;
            using (var bg = new SolidBrush(sel ? Theme.Accent : Theme.Surface2)) e.Graphics.FillRectangle(bg, e.Bounds);
            var fore = sel ? Color.Black : Theme.Text;
            TextRenderer.DrawText(e.Graphics, port, Theme.UI, new Point(e.Bounds.X + 2, e.Bounds.Y + 1), fore);
            if (!edit)
            {
                string f = PortCatalog.Friendly(port);
                bool present = PortCatalog.Names().Contains(port, StringComparer.OrdinalIgnoreCase);
                if (!present) f = "(chưa cắm)";
                if (f != null)
                {
                    int x = e.Bounds.X + TextRenderer.MeasureText(port, Theme.UI).Width + 4;
                    TextRenderer.DrawText(e.Graphics, f, Theme.Small, new Rectangle(x, e.Bounds.Y + 2, e.Bounds.Right - x, e.Bounds.Height),
                        sel ? Color.FromArgb(30, 30, 30) : Theme.Muted, TextFormatFlags.EndEllipsis);
                }
            }
        }

        void OnRouteChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)RefreshView); } catch { }
        }

        string _shownSeq;
        public void RefreshView()
        {
            var r = Route;
            var st = r.Source.State;
            _dot = st == LinkState.Connected ? Theme.Ok : st == LinkState.Connecting ? Theme.Warn : st == LinkState.Error ? Theme.Err : Theme.Muted;

            // trạng thái đầu ra chuyển tiếp (ví dụ cổng com0com chưa mở được)
            var badOut = r.Outputs.Select(o => o.Host).FirstOrDefault(h => h != null && h.State != LinkState.Connected);
            string status;
            Color statusColor = Theme.Muted;
            if (st != LinkState.Connected)
            {
                status = r.Source.StateText;
                statusColor = st == LinkState.Error ? Theme.Err : Theme.Warn;
            }
            else if (badOut != null)
            {
                status = "Đầu ra: " + badOut.StateText;
                statusColor = Theme.Warn;
            }
            else if (r.LastResultTime != default)
            {
                status = r.LastResultTime.ToString("HH:mm:ss") + "  " + r.LastResult;
                statusColor = r.LastResultOk ? Theme.Muted : Theme.Err;
            }
            else status = "Sẵn sàng · " + r.Source.StateText;

            _status.Text = status;
            _status.ForeColor = statusColor;
            _tip.SetToolTip(_status, status);
            _tip.SetToolTip(this, r.Source.StateText);

            _read.Text = r.HasRequestCommand ? "Đọc" : "Gửi lại";
            _read.Enabled = r.HasRequestCommand ? st == LinkState.Connected : r.LastValue != null;

            var lv = r.LastValue;
            string seq = lv == null ? null : lv.Sequence.ToString();
            if (lv != null && seq != _shownSeq)
            {
                _shownSeq = seq;
                _value.Text = lv.Value;
                _flashStart = DateTime.Now;
                _flash.Start();
            }
            Invalidate();
        }

        void AnimateFlash()
        {
            double t = (DateTime.Now - _flashStart).TotalMilliseconds / 700.0;
            if (t >= 1) { _value.ForeColor = Theme.Text; _flash.Stop(); return; }
            _value.ForeColor = Blend(Theme.Accent, Theme.Text, t * t);
        }

        static Color Blend(Color a, Color b, double t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        void Safe(Action a)
        {
            try { a(); }
            catch (Exception ex)
            {
                _status.Text = ex.Message;
                _status.ForeColor = Theme.Err;
            }
        }

        public override Size GetPreferredSize(Size proposed)
        {
            int w = Math.Max(proposed.Width, Theme.S(200));
            var inner = _grid.GetPreferredSize(new Size(w - Padding.Horizontal, 0));
            return new Size(w, inner.Height + Padding.Vertical);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.RoundRect(r, Theme.S(8)))
            using (var fill = new SolidBrush(Theme.Surface))
            using (var pen = new Pen(Theme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            // chấm trạng thái, canh theo dòng tên
            float d = Theme.S(9);
            var nameLoc = _name.Parent.PointToScreen(_name.Location);
            var local = PointToClient(nameLoc);
            float cy = local.Y + _name.Padding.Top + _name.Font.Height / 2f;
            using (var b = new SolidBrush(_dot)) g.FillEllipse(b, local.X, cy - d / 2, d, d);
            if (_dot == Theme.Ok)
                using (var halo = new SolidBrush(Color.FromArgb(50, _dot))) g.FillEllipse(halo, local.X - 3, cy - d / 2 - 3, d + 6, d + 6);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (Route != null) Route.Changed -= OnRouteChanged;
                _flash.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
