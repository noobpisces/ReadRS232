using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using ReadRS232.Core;

namespace ReadRS232.UI
{
    /// <summary>
    /// Màn hình chính: gọn, mỗi tuyến 1 thẻ. Bánh răng mở Cài đặt, biểu tượng terminal mở Monitor.
    /// </summary>
    public sealed class MainForm : Form
    {
        AppConfig _cfg;
        readonly Engine _engine = new Engine();
        readonly FlowLayoutPanel _cards;
        readonly MonitorPanel _monitor;
        readonly Panel _monitorHost;
        readonly Button _btnPin, _btnMonitor;
        readonly Label _empty;
        bool _monitorOn;   // không dùng Control.Visible: trả về false khi form chưa hiện
        readonly NotifyIcon _tray;
        readonly ToolTip _tip = new ToolTip { InitialDelay = 400 };
        readonly Timer _deviceDebounce = new Timer { Interval = 600 };

        static readonly int CardWidth = Theme.S(380);

        public MainForm(AppConfig cfg, string startupWarning)
        {
            _cfg = cfg;
            Text = "ReadRS232";
            Theme.Apply(this);
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;

            // ---- header ----
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(36), BackColor = Theme.Bg, Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(6), 0) };
            var brand = Theme.Label("ReadRS232", Theme.Bold, Theme.Muted);
            brand.Location = new Point(Theme.S(14), Theme.S(10));
            var ver = Theme.Label("v" + Application.ProductVersion.Split('.').Take(2).Aggregate((a, b) => a + "." + b), Theme.Small, Color.FromArgb(0x5D, 0x63, 0x6B));
            ver.Location = new Point(brand.Right + Theme.S(32), Theme.S(11));
            var icons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, Theme.S(2), 0, 0) };
            var btnSettings = Theme.IconButton("", "Cài đặt", _tip);
            _btnMonitor = Theme.IconButton("", "Monitor: xem dữ liệu thô / gửi thử", _tip);
            _btnPin = Theme.IconButton("", "Luôn nằm trên cùng", _tip);
            icons.Controls.AddRange(new Control[] { btnSettings, _btnMonitor, _btnPin });
            header.Controls.Add(icons);
            header.Controls.Add(brand);
            header.Controls.Add(ver);
            brand.SizeChanged += (s, e) => ver.Left = brand.Right + Theme.S(6);

            // ---- thẻ ----
            _cards = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Bg,
                Padding = new Padding(Theme.S(10), 0, Theme.S(10), Theme.S(6))
            };
            _empty = Theme.Label("Chưa có tuyến nào đang chạy.\nBấm ⚙ để thêm thiết bị.", Theme.UI, Theme.Muted);
            _empty.Padding = new Padding(Theme.S(8), Theme.S(16), 0, Theme.S(16));

            // ---- monitor ----
            _monitor = new MonitorPanel { Dock = DockStyle.Fill, MaxLines = Math.Max(200, cfg.Ui.MonitorMaxLines) };
            _monitorHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
            var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
            var gap = new Panel { Dock = DockStyle.Top, Height = Theme.S(8), BackColor = Theme.Bg };
            _monitorHost.Controls.Add(_monitor);
            _monitorHost.Controls.Add(gap);
            _monitorHost.Controls.Add(sep);

            Controls.Add(_monitorHost);
            Controls.Add(_cards);
            Controls.Add(header);

            // ---- khay hệ thống ----
            var trayMenu = Theme.Menu();
            trayMenu.Items.Add(Theme.Item("Mở ReadRS232", (s, e) => RestoreFromTray()));
            trayMenu.Items.Add(Theme.Item("Cài đặt…", (s, e) => { RestoreFromTray(); OpenSettings(null); }));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(Theme.Item("Thoát", (s, e) => ExitApp()));
            _tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "ReadRS232", ContextMenuStrip = trayMenu, Visible = true };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };

            // ---- sự kiện ----
            btnSettings.Click += (s, e) => OpenSettings(null);
            _btnMonitor.Click += (s, e) => ToggleMonitor(!_monitorOn);
            _btnPin.Click += (s, e) => { _cfg.Ui.AlwaysOnTop = !_cfg.Ui.AlwaysOnTop; ApplyUi(); SaveConfig(); };
            _deviceDebounce.Tick += (s, e) => { _deviceDebounce.Stop(); OnDevicesChanged(); };
            _engine.RoutesChanged += () => _monitor.SetRoutes(() => _engine.Routes);

            Task.Run(() => PortCatalog.Refresh());
            StartEngine();
            ApplyUi();
            ToggleMonitor(_cfg.Ui.ShowMonitor, save: false);
            PlaceWindow();

            if (startupWarning != null)
                Shown += (s, e) => MessageBox.Show(this, startupWarning, "ReadRS232", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (_cfg.Ui.StartMinimized)
                Load += (s, e) => BeginInvoke((Action)(() => { WindowState = FormWindowState.Minimized; }));
        }

        // ===================================================================
        void StartEngine()
        {
            _engine.Start(_cfg);
            RebuildCards();
        }

        void RebuildCards()
        {
            _cards.SuspendLayout();
            foreach (Control c in _cards.Controls.Cast<Control>().ToList()) { _cards.Controls.Remove(c); if (c != _empty) c.Dispose(); }
            foreach (var r in _engine.Routes.Where(r => r.Config.ShowOnMain))
            {
                var card = new RouteCard(r, _tip) { Margin = new Padding(0, 0, 0, Theme.S(8)) };
                card.Width = CardWidth;
                card.Height = card.GetPreferredSize(new Size(CardWidth, 0)).Height;
                card.PortChangeRequested += OnCardPortChange;
                card.SettingsRequested += c => OpenSettings(c.Route.Config.Id);
                _cards.Controls.Add(card);
            }
            if (_cards.Controls.Count == 0)
            {
                _empty.Text = _engine.Routes.Count == 0
                    ? "Chưa có tuyến nào đang chạy.\nBấm ⚙ (góc trên phải) để thêm thiết bị."
                    : $"{_engine.Routes.Count} tuyến đang chạy ngầm.\nBật \"Hiện ở màn hình chính\" trong Cài đặt để xem ở đây.";
                _cards.Controls.Add(_empty);
            }
            _cards.ResumeLayout(true);
            UpdateSize();
        }

        void OnCardPortChange(RouteCard card, string port)
        {
            var rc = card.Route.Config;
            if (rc.Source is SerialEndpointConfig sc)
            {
                sc.PortName = port;
                SaveConfig();
                var nr = _engine.Restart(rc);
                if (nr != null) card.Bind(nr);
                AppLog.Write(rc.Name, LogKind.Info, "Đổi nguồn sang " + port);
            }
        }

        void OpenSettings(string routeId)
        {
            using (var f = new SettingsForm(_cfg, routeId))
            {
                bool top = TopMost;
                TopMost = false;
                var res = f.ShowDialog(this);
                TopMost = top;
                if (res != DialogResult.OK || f.Result == null) return;
                _cfg = f.Result;
                SaveConfig();
                ApplyUi();
                StartEngine();
                AppLog.Write("", LogKind.Info, "Đã áp dụng cấu hình mới");
            }
        }

        void SaveConfig()
        {
            try { _cfg.Save(Program.ConfigPath); }
            catch (Exception ex) { MessageBox.Show(this, "Không lưu được cấu hình:\n" + ex.Message, "ReadRS232", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void ApplyUi()
        {
            TopMost = _cfg.Ui.AlwaysOnTop;
            _btnPin.Text = _cfg.Ui.AlwaysOnTop ? "" : "";
            _btnPin.ForeColor = _cfg.Ui.AlwaysOnTop ? Theme.Accent : Theme.Muted;
            _monitor.MaxLines = Math.Max(200, _cfg.Ui.MonitorMaxLines);
            ApplyStartup(_cfg.Ui.StartWithWindows);
        }

        static void ApplyStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    string name = "ReadRS232";
                    if (on) k.SetValue(name, Program.StartupCommand());
                    else if (k.GetValue(name) != null) k.DeleteValue(name, false);
                }
            }
            catch { }
        }

        void ToggleMonitor(bool show, bool save = true)
        {
            int monitorH = Math.Max(Theme.S(180), _cfg.Ui.MonitorHeight);
            if (!show && _monitorOn) _cfg.Ui.MonitorHeight = _monitorHost.Height;
            _monitorHost.Visible = _monitorOn = show;
            _btnMonitor.ForeColor = show ? Theme.Accent : Theme.Muted;
            FormBorderStyle = show ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;
            MaximizeBox = show;
            _cfg.Ui.ShowMonitor = show;
            UpdateSize(show ? monitorH : 0);
            if (show) _monitor.SetRoutes(() => _engine.Routes);
            if (save) SaveConfig();
        }

        void UpdateSize(int? monitorH = null)
        {
            int top = Theme.S(36) + _cards.GetPreferredSize(new Size(CardWidth + Theme.S(20), 0)).Height;
            int mh = monitorH ?? (_monitorOn ? _monitorHost.Height : 0);
            if (_monitorOn)
            {
                MinimumSize = Size.Empty;
                ClientSize = new Size(Math.Max(ClientSize.Width, Theme.S(600)), top + mh);
                MinimumSize = new Size(Width - ClientSize.Width + CardWidth + Theme.S(20), Height - ClientSize.Height + top + Theme.S(160));
            }
            else
            {
                MinimumSize = Size.Empty;
                ClientSize = new Size(CardWidth + Theme.S(20), top);
            }
        }

        void FitCards()
        {
            int w = Math.Max(CardWidth, _cards.ClientSize.Width - _cards.Padding.Horizontal);
            foreach (var c in _cards.Controls.OfType<RouteCard>())
            {
                c.Width = w;
                c.Height = c.GetPreferredSize(new Size(w, 0)).Height;
            }
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            if (_cards != null) FitCards();
        }

        void PlaceWindow()
        {
            var ui = _cfg.Ui;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var pt = new Point(ui.WindowX, ui.WindowY);
            if (ui.WindowX == int.MinValue || !Screen.AllScreens.Any(s => s.WorkingArea.Contains(new Point(pt.X + 40, pt.Y + 20))))
                pt = new Point(wa.Right - Width - Theme.S(24), wa.Top + Theme.S(80));
            Location = pt;
        }

        void OnDevicesChanged()
        {
            Task.Run(() =>
            {
                PortCatalog.Refresh();
                try { BeginInvoke((Action)(() => { foreach (var c in _cards.Controls.OfType<RouteCard>()) c.OnPortsChanged(); })); } catch { }
            });
            _engine.KickAll();
        }

        // ===================================================================
        protected override void WndProc(ref Message m)
        {
            const int WM_DEVICECHANGE = 0x0219;
            if (m.Msg == WM_DEVICECHANGE) { _deviceDebounce.Stop(); _deviceDebounce.Start(); }
            if (m.Msg == Program.WM_SHOWME) RestoreFromTray();
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && _cfg.Ui.MinimizeToTray)
            {
                Hide();
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    _tray.ShowBalloonTip(2500, "ReadRS232 vẫn đang chạy", "Nhấp vào biểu tượng ở khay để mở lại.", ToolTipIcon.Info);
                }
            }
        }
        bool _trayHintShown;

        public void RestoreFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        void ExitApp()
        {
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (WindowState == FormWindowState.Normal)
            {
                _cfg.Ui.WindowX = Left;
                _cfg.Ui.WindowY = Top;
                if (_monitorOn) _cfg.Ui.MonitorHeight = _monitorHost.Height;
            }
            SaveConfig();
            _tray.Visible = false;
            _engine.Dispose();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tray.Dispose(); _deviceDebounce.Dispose(); _tip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
