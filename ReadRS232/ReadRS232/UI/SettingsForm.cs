using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using ReadRS232.Core;

namespace ReadRS232.UI
{
    /// <summary>
    /// Màn hình Cài đặt: bên trái là cây Tuyến → Nguồn / Xử lý / Đầu ra,
    /// bên phải là bảng thuộc tính + công cụ thử ngay tại chỗ.
    /// Sửa trên bản sao; chỉ áp dụng khi bấm Lưu.
    /// </summary>
    public sealed class SettingsForm : Form
    {
        public AppConfig Result { get; private set; }

        readonly AppConfig _cfg;
        readonly TreeView _tree;
        readonly PropertyGrid _grid;
        readonly Label _title, _hint;
        readonly Panel _tools;
        readonly Button _btnAddOutput, _btnSource, _btnDelete, _btnUp, _btnDown, _btnClone;
        readonly ToolTip _tip = new ToolTip();

        public SettingsForm(AppConfig cfg, string selectRouteId = null)
        {
            _cfg = cfg.Clone();
            Text = "Cài đặt — ReadRS232";
            Theme.Apply(this);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Theme.S(980), Theme.S(660));
            MinimumSize = new Size(Theme.S(760), Theme.S(480));
            KeyPreview = true;

            // ================= trái: cây + thanh công cụ =================
            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(320), Padding = new Padding(Theme.S(12), Theme.S(12), Theme.S(6), Theme.S(12)), BackColor = Theme.Bg };
            var tb = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Bg, Padding = new Padding(0, 0, 0, Theme.S(8)) };
            var btnAddRoute = Small("+ Tuyến", "Thêm tuyến mới (1 thiết bị nguồn)");
            _btnAddOutput = Small("+ Đầu ra ▾", "Thêm nơi nhận dữ liệu cho tuyến đang chọn");
            _btnSource = Small("Đổi nguồn ▾", "Đổi loại nguồn: COM / TCP");
            _btnClone = Small("Nhân bản", "Nhân bản mục đang chọn");
            _btnUp = Small("↑", "Lên");
            _btnDown = Small("↓", "Xuống");
            _btnDelete = Small("Xoá", "Xoá mục đang chọn");
            tb.Controls.AddRange(new Control[] { btnAddRoute, _btnAddOutput, _btnSource, _btnClone, _btnUp, _btnDown, _btnDelete });

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                FullRowSelect = true,
                HideSelection = false,
                ShowLines = false,
                ShowPlusMinus = false,
                ShowRootLines = false,
                ItemHeight = Theme.S(28),
                Indent = Theme.S(18),
                DrawMode = TreeViewDrawMode.OwnerDrawAll,
                Font = Theme.UI
            };
            _tree.DrawNode += DrawNode;
            _tree.AfterSelect += (s, e) => ShowSelected();
            _tree.BeforeCollapse += (s, e) => e.Cancel = true;
            left.Controls.Add(_tree);
            left.Controls.Add(tb);

            // ================= phải: tiêu đề + PropertyGrid + công cụ =================
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(6), Theme.S(12), Theme.S(12), Theme.S(12)), BackColor = Theme.Bg };
            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(54), BackColor = Theme.Bg };
            _title = Theme.Label("", new Font("Segoe UI Semibold", 13f));
            _title.Location = new Point(0, 0);
            _hint = Theme.Label("", Theme.Small, Theme.Muted);
            _hint.Location = new Point(Theme.S(2), Theme.S(30));
            _hint.AutoSize = false;
            _hint.Width = Theme.S(600);
            _hint.Height = Theme.S(20);
            _hint.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            _hint.AutoEllipsis = true;
            head.Controls.Add(_title);
            head.Controls.Add(_hint);
            head.Resize += (s, e) => _hint.Width = head.Width - Theme.S(4);

            _grid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                ToolbarVisible = false,
                PropertySort = PropertySort.Categorized,
                BackColor = Theme.Bg,
                ViewBackColor = Theme.Surface,
                ViewForeColor = Theme.Text,
                LineColor = Theme.Surface2,
                CategoryForeColor = Theme.Accent,
                CategorySplitterColor = Theme.Surface2,
                HelpBackColor = Theme.Surface2,
                HelpForeColor = Theme.Muted,
                HelpBorderColor = Theme.Border,
                ViewBorderColor = Theme.Border,
                CommandsBackColor = Theme.Bg,
                CommandsForeColor = Theme.Text,
                DisabledItemForeColor = Theme.Muted,
                SelectedItemWithFocusBackColor = Color.FromArgb(0x1F, 0x4A, 0x73),
                SelectedItemWithFocusForeColor = Color.White,
                Font = Theme.UI
            };
            _grid.PropertyValueChanged += (s, e) => { RefreshNodeTexts(); _toolsRefresh?.Invoke(); };

            _tools = new Panel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = Theme.Bg, Padding = new Padding(0, Theme.S(10), 0, 0) };
            right.Controls.Add(_grid);
            right.Controls.Add(_tools);
            right.Controls.Add(head);

            // ================= dưới: nút =================
            var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 6, BackColor = Theme.Surface, Padding = new Padding(Theme.S(12), Theme.S(10), Theme.S(12), Theme.S(10)) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var bExport = Theme.Button("Xuất file…");
            var bImport = Theme.Button("Nhập file…");
            var bReset = Theme.Button("Mặc định");
            var bCancel = Theme.Button("Huỷ");
            var bSave = Theme.Button("Lưu và áp dụng", primary: true);
            foreach (var b in new[] { bExport, bImport, bReset, bCancel }) b.Margin = new Padding(0, 0, Theme.S(8), 0);
            bSave.Margin = new Padding(0);
            bottom.Controls.Add(bExport, 0, 0);
            bottom.Controls.Add(bImport, 1, 0);
            bottom.Controls.Add(bReset, 2, 0);
            bottom.Controls.Add(new Panel { Height = 1, BackColor = Theme.Surface }, 3, 0);
            bottom.Controls.Add(bCancel, 4, 0);
            bottom.Controls.Add(bSave, 5, 0);

            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(bottom);

            // ================= sự kiện =================
            btnAddRoute.Click += (s, e) => AddRoute();
            _btnAddOutput.Click += (s, e) => ShowMenu(_btnAddOutput, OutputCatalog.Types.Select(t => (t.Title, (Action)(() => AddOutput(t.Create())))));
            _btnSource.Click += (s, e) => ShowMenu(_btnSource, OutputCatalog.Sources.Select(t => (t.Title, (Action)(() => ChangeSource(t.Create())))));
            _btnDelete.Click += (s, e) => DeleteSelected();
            _btnUp.Click += (s, e) => MoveSelected(-1);
            _btnDown.Click += (s, e) => MoveSelected(+1);
            _btnClone.Click += (s, e) => CloneSelected();
            bSave.Click += (s, e) => { Result = _cfg; DialogResult = DialogResult.OK; };
            bCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
            bExport.Click += (s, e) => Export();
            bImport.Click += (s, e) => Import();
            bReset.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Xoá toàn bộ cấu hình hiện tại và quay về mặc định?", "Mặc định", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                var d = AppConfig.CreateDefault();
                _cfg.Routes = d.Routes;
                BuildTree(null);
            };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };

            BuildTree(selectRouteId);
        }

        Button Small(string text, string tip)
        {
            var b = Theme.Button(text);
            b.Padding = new Padding(Theme.S(6), Theme.S(1), Theme.S(6), Theme.S(1));
            b.Margin = new Padding(0, 0, Theme.S(4), Theme.S(4));
            _tip.SetToolTip(b, tip);
            return b;
        }

        void ShowMenu(Control anchor, IEnumerable<(string Title, Action Do)> items)
        {
            var m = Theme.Menu();
            foreach (var it in items) m.Items.Add(Theme.Item(it.Title, (s, e) => it.Do()));
            m.Show(anchor, new Point(0, anchor.Height));
        }

        // ===================================================================
        //  CÂY
        // ===================================================================
        void BuildTree(object select)
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            TreeNode toSelect = null;
            foreach (var r in _cfg.Routes)
            {
                var rn = new TreeNode { Tag = r };
                rn.Nodes.Add(new TreeNode { Tag = new SourceRef(r) });
                rn.Nodes.Add(new TreeNode { Tag = r.Processing });
                foreach (var o in r.Outputs) rn.Nodes.Add(new TreeNode { Tag = o });
                _tree.Nodes.Add(rn);
                if (select is string id && r.Id == id) toSelect = rn;
                foreach (TreeNode c in rn.Nodes)
                    if (ReferenceEquals(c.Tag, select) || (c.Tag is SourceRef sr && ReferenceEquals(sr.Route.Source, select))) toSelect = c;
                if (ReferenceEquals(r, select)) toSelect = rn;
            }
            _tree.Nodes.Add(new TreeNode { Tag = _cfg.Ui });
            RefreshNodeTexts();
            _tree.ExpandAll();
            _tree.EndUpdate();
            _tree.SelectedNode = toSelect ?? (_tree.Nodes.Count > 1 ? _tree.Nodes[0] : _tree.Nodes[0]);
            ShowSelected();
        }

        /// <summary>Bọc nguồn để node biết thuộc tuyến nào (đổi nguồn = thay object).</summary>
        sealed class SourceRef
        {
            public readonly RouteConfig Route;
            public SourceRef(RouteConfig r) { Route = r; }
        }

        void RefreshNodeTexts()
        {
            foreach (TreeNode rn in _tree.Nodes)
            {
                rn.Text = NodeText(rn.Tag);
                foreach (TreeNode c in rn.Nodes) c.Text = NodeText(c.Tag);
            }
            _tree.Invalidate();
            UpdateButtons();
        }

        static string NodeText(object tag)
        {
            switch (tag)
            {
                case RouteConfig r: return r.Name + (r.Enabled ? "" : "  (đang tắt)");
                case SourceRef s: return "Nguồn: " + s.Route.Source.Describe();
                case ProcessingConfig p: return "Xử lý: " + DescribeProcessing(p);
                case OutputConfig o: return (o.Enabled ? "" : "(tắt) ") + o.Describe();
                case UiConfig _: return "Giao diện & khởi động";
                default: return tag?.ToString() ?? "";
            }
        }

        static string DescribeProcessing(ProcessingConfig p)
        {
            var parts = new List<string>();
            if (p.IsNumber)
            {
                if (p.Multiply != 1) parts.Add("×" + p.Multiply);
                if (p.Offset != 0) parts.Add((p.Offset > 0 ? "+" : "") + p.Offset);
                if (p.Decimals >= 0) parts.Add(p.Decimals + " số lẻ");
            }
            if (p.StableCount > 1) parts.Add("ổn định " + p.StableCount);
            if (p.SkipDuplicates) parts.Add("bỏ trùng");
            return parts.Count == 0 ? (p.ExtractMode == ExtractMode.WholeFrame ? "giữ nguyên" : "lấy số") : string.Join(", ", parts);
        }

        void DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            if (e.Bounds.Height <= 0) return;
            var g = e.Graphics;
            bool sel = (e.State & TreeNodeStates.Selected) != 0;
            var row = new Rectangle(0, e.Bounds.Y, _tree.ClientSize.Width, e.Bounds.Height);
            using (var bg = new SolidBrush(sel ? Color.FromArgb(0x1F, 0x4A, 0x73) : _tree.BackColor)) g.FillRectangle(bg, row);

            bool top = e.Node.Parent == null;
            int x = Theme.S(10) + e.Node.Level * Theme.S(18);
            string icon; Color ic;
            switch (e.Node.Tag)
            {
                case RouteConfig r: icon = ""; ic = r.Enabled ? Theme.Accent : Theme.Muted; break;
                case SourceRef _: icon = ""; ic = Theme.Rx; break;
                case ProcessingConfig _: icon = ""; ic = Theme.Warn; break;
                case OutputConfig o: icon = ""; ic = o.Enabled ? Theme.Ok : Theme.Muted; break;
                default: icon = ""; ic = Theme.Muted; break;
            }
            TextRenderer.DrawText(g, icon, Theme.Icon, new Point(x, e.Bounds.Y + (e.Bounds.Height - Theme.Icon.Height) / 2), ic);
            x += Theme.S(24);
            var font = top ? Theme.Bold : Theme.UI;
            var color = (e.Node.Tag is OutputConfig oc && !oc.Enabled) || (e.Node.Tag is RouteConfig rc && !rc.Enabled) ? Theme.Muted : Theme.Text;
            TextRenderer.DrawText(g, e.Node.Text, font, new Rectangle(x, e.Bounds.Y, row.Width - x - 4, e.Bounds.Height), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        RouteConfig SelectedRoute
        {
            get
            {
                var n = _tree.SelectedNode;
                if (n == null) return null;
                if (n.Tag is RouteConfig r) return r;
                return n.Parent?.Tag as RouteConfig;
            }
        }

        void UpdateButtons()
        {
            var n = _tree.SelectedNode;
            bool hasRoute = SelectedRoute != null;
            _btnAddOutput.Enabled = hasRoute;
            _btnSource.Enabled = hasRoute;
            bool movable = n != null && (n.Tag is RouteConfig || n.Tag is OutputConfig);
            _btnUp.Enabled = _btnDown.Enabled = movable;
            _btnDelete.Enabled = movable;
            _btnClone.Enabled = movable;
        }

        // ===================================================================
        //  HIỂN THỊ MỤC ĐANG CHỌN
        // ===================================================================
        Action _toolsRefresh;

        void ShowSelected()
        {
            var tag = _tree.SelectedNode?.Tag;
            object target = tag is SourceRef sr ? sr.Route.Source : tag;
            _grid.SelectedObject = target;
            _toolsRefresh = null;
            _tools.Controls.Clear();

            switch (tag)
            {
                case RouteConfig _:
                    _title.Text = "Tuyến";
                    _hint.Text = "Mỗi tuyến = 1 nguồn dữ liệu → xử lý → gửi tới nhiều đầu ra cùng lúc. Có thể tạo nhiều tuyến cho nhiều thiết bị.";
                    break;
                case SourceRef s:
                    _title.Text = "Nguồn dữ liệu";
                    _hint.Text = "Nơi đọc dữ liệu từ thiết bị. Bấm \"Đổi nguồn\" để chuyển giữa COM và TCP.";
                    break;
                case ProcessingConfig p:
                    _title.Text = "Xử lý dữ liệu";
                    _hint.Text = "Tách khung → lấy giá trị → tính toán → lọc. Gõ thử dữ liệu mẫu bên dưới để xem kết quả ngay.";
                    BuildProcessingTester(p);
                    break;
                case WindowOutputConfig w:
                    _title.Text = w.TypeTitle;
                    _hint.Text = "Bấm \"Chọn ô bằng chuột\" rồi click vào ô nhập của app kia — app tự lấy tên tiến trình, class và AutomationId.";
                    BuildWindowTools(w);
                    break;
                case ForwardOutputConfig f:
                    _title.Text = f.TypeTitle;
                    _hint.Text = "Dùng cặp com0com: app này ghi vào 1 đầu, app khác mở đầu còn lại. Chế độ Thô + Hai chiều = chia sẻ cổng hoàn toàn.";
                    break;
                case OutputConfig o:
                    _title.Text = o.TypeTitle;
                    _hint.Text = "Mẫu dữ liệu: {value} {value:0.00} {frame} {hex} {time:HH:mm:ss} {route} {n}   ·   \\r \\n \\t \\xHH";
                    break;
                case UiConfig _:
                    _title.Text = "Giao diện & khởi động";
                    _hint.Text = "File cấu hình: " + Program.ConfigPath;
                    break;
            }
            UpdateButtons();
        }

        // ---------- công cụ thử xử lý (regex tester) ----------
        void BuildProcessingTester(ProcessingConfig p)
        {
            var box = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Theme.Surface, Padding = new Padding(Theme.S(12)) };
            box.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var input = new TextBox { Dock = DockStyle.Fill, Text = _lastSample };
            Theme.StyleText(input);
            input.Font = Theme.Mono;
            var output = Theme.Label("", Theme.Mono, Theme.Text);
            output.AutoSize = true;
            output.MaximumSize = new Size(Theme.S(560), 0);
            box.Controls.Add(Theme.Label("Dữ liệu mẫu", Theme.Small, Theme.Muted), 0, 0);
            box.Controls.Add(input, 1, 0);
            box.Controls.Add(Theme.Label("Kết quả", Theme.Small, Theme.Muted), 0, 1);
            box.Controls.Add(output, 1, 1);
            _tools.Controls.Add(box);

            void Run()
            {
                _lastSample = input.Text;
                try
                {
                    var proc = new ValueProcessor(p);
                    var lines = new List<string>();
                    var framer = new Framer(p, proc.Encoding);
                    framer.Frame += f =>
                    {
                        var r = proc.Process(f);
                        string src = TextUtil.Visualize(r.Text ?? "");
                        lines.Add(r.Ok ? $"\"{src}\"  →  {r.Value}" : $"\"{src}\"  ✗ {r.Reason}");
                    };
                    framer.Push(proc.Encoding.GetBytes(TextUtil.Unescape(input.Text)));
                    framer.Flush();   // xả phần còn lại như khi hết khoảng lặng
                    framer.Dispose();
                    output.Text = lines.Count == 0 ? "(chưa có khung nào)" : string.Join("\n", lines.Take(8));
                    output.ForeColor = lines.Any(l => l.Contains("→")) ? Theme.Ok : Theme.Warn;
                }
                catch (Exception ex) { output.Text = ex.Message; output.ForeColor = Theme.Err; }
            }
            input.TextChanged += (s, e) => Run();
            _toolsRefresh = Run;
            Run();
        }
        static string _lastSample = @"ST,GS,+  12.50 kg\r\n";

        // ---------- công cụ cho đầu ra "Điền vào app" ----------
        void BuildWindowTools(WindowOutputConfig w)
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Bg, WrapContents = true };
            var pick = Theme.Button("🎯  Chọn ô bằng chuột", primary: true);
            var test = Theme.Button("Thử điền");
            var sample = new TextBox { Width = Theme.S(90), Text = "123.45", Margin = new Padding(Theme.S(8), Theme.S(5), Theme.S(4), 0) };
            Theme.StyleText(sample);
            var result = Theme.Label("", Theme.UI, Theme.Muted);
            result.Margin = new Padding(Theme.S(8), Theme.S(7), 0, 0);
            pick.Margin = new Padding(0, 0, Theme.S(8), 0);
            row.Controls.AddRange(new Control[] { pick, sample, test, result });
            _tools.Controls.Add(row);

            pick.Click += (s, e) =>
            {
                // Không dùng Hide(): ẩn hộp thoại modal sẽ đóng luôn nó. Dời tạm ra ngoài màn hình.
                var owner = Owner;
                var myPos = Location;
                var ownerPos = owner?.Location ?? Point.Empty;
                Location = new Point(-32000, -32000);
                if (owner != null) owner.Location = new Point(-32000, -32000);
                var picker = new PickerOverlay(res =>
                {
                    if (owner != null) owner.Location = ownerPos;
                    Location = myPos;
                    Native.ForceForeground(Handle);
                    Activate();
                    if (res == null) { result.Text = "Đã huỷ chọn"; return; }
                    if (string.Equals(res.ProcessName, System.Diagnostics.Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
                    { result.Text = "Đó là chính app này 🙂"; result.ForeColor = Theme.Warn; return; }
                    w.ProcessName = res.ProcessName;
                    w.ControlClassContains = res.Root == res.Control ? "" : res.ClassPart;
                    w.ControlIndex = Math.Max(0, res.Index);
                    w.AutomationId = res.AutomationId ?? "";
                    w.WindowTitleContains = "";
                    _grid.Refresh();
                    RefreshNodeTexts();
                    result.Text = "✓ " + res;
                    result.ForeColor = Theme.Ok;
                });
                picker.Start();
            };

            test.Click += (s, e) =>
            {
                result.Text = "Đang thử…";
                result.ForeColor = Theme.Muted;
                string text = sample.Text;
                var cfg = w;
                var t = new Thread(() =>
                {
                    string msg; bool ok;
                    try { Native.EnsureMessageQueue(); msg = "✓ Đã điền " + Core.WindowTarget.Fill(cfg, text); ok = true; }
                    catch (Exception ex) { msg = "✗ " + ex.Message; ok = false; }
                    BeginInvoke((Action)(() =>
                    {
                        Activate();
                        result.Text = msg;
                        result.ForeColor = ok ? Theme.Ok : Theme.Err;
                    }));
                }) { IsBackground = true };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            };
        }

        // ===================================================================
        //  THAO TÁC
        // ===================================================================
        void AddRoute()
        {
            var r = RouteConfig.CreateNew("Thiết bị " + (_cfg.Routes.Count + 1));
            _cfg.Routes.Add(r);
            BuildTree(r.Source);
        }

        void AddOutput(OutputConfig o)
        {
            var r = SelectedRoute;
            if (r == null) return;
            r.Outputs.Add(o);
            BuildTree(o);
        }

        void ChangeSource(EndpointConfig src)
        {
            var r = SelectedRoute;
            if (r == null) return;
            r.Source = src;
            BuildTree(src);
        }

        void DeleteSelected()
        {
            var n = _tree.SelectedNode;
            if (n == null) return;
            if (n.Tag is RouteConfig r)
            {
                if (MessageBox.Show(this, $"Xoá tuyến \"{r.Name}\" cùng toàn bộ đầu ra của nó?", "Xoá", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _cfg.Routes.Remove(r);
                BuildTree(null);
            }
            else if (n.Tag is OutputConfig o)
            {
                var route = (RouteConfig)n.Parent.Tag;
                route.Outputs.Remove(o);
                BuildTree(route);
            }
        }

        void MoveSelected(int dir)
        {
            var n = _tree.SelectedNode;
            if (n?.Tag is RouteConfig r) MoveIn(_cfg.Routes, r, dir);
            else if (n?.Tag is OutputConfig o) MoveIn(((RouteConfig)n.Parent.Tag).Outputs, o, dir);
            else return;
            BuildTree(n.Tag);
        }

        static void MoveIn<T>(List<T> list, T item, int dir)
        {
            int i = list.IndexOf(item), j = i + dir;
            if (i < 0 || j < 0 || j >= list.Count) return;
            list.RemoveAt(i);
            list.Insert(j, item);
        }

        void CloneSelected()
        {
            var n = _tree.SelectedNode;
            // nhân bản bằng cách serialize qua XML (giữ đúng kiểu con)
            var tmp = new AppConfig { Routes = new List<RouteConfig>() };
            if (n?.Tag is RouteConfig r)
            {
                tmp.Routes.Add(r);
                var copy = tmp.Clone().Routes[0];
                copy.Id = Guid.NewGuid().ToString("N");
                copy.Name = r.Name + " (bản sao)";
                _cfg.Routes.Insert(_cfg.Routes.IndexOf(r) + 1, copy);
                BuildTree(copy);
            }
            else if (n?.Tag is OutputConfig o)
            {
                var route = (RouteConfig)n.Parent.Tag;
                var holder = RouteConfig.CreateNew("x");
                holder.Outputs.Add(o);
                tmp.Routes.Add(holder);
                var copy = tmp.Clone().Routes[0].Outputs[0];
                route.Outputs.Insert(route.Outputs.IndexOf(o) + 1, copy);
                BuildTree(copy);
            }
        }

        void Export()
        {
            using (var d = new SaveFileDialog { Filter = "Cấu hình ReadRS232 (*.xml)|*.xml", FileName = "ReadRS232-config.xml" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { _cfg.Save(d.FileName); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void Import()
        {
            using (var d = new OpenFileDialog { Filter = "Cấu hình ReadRS232 (*.xml)|*.xml|Tất cả|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    AppConfig c;
                    using (var fs = File.OpenRead(d.FileName)) c = (AppConfig)new XmlSerializer(typeof(AppConfig)).Deserialize(fs);
                    c = c.Clone();
                    _cfg.Routes = c.Routes;
                    _cfg.Ui = c.Ui;
                    BuildTree(null);
                }
                catch (Exception ex) { MessageBox.Show(this, "File không hợp lệ:\n" + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
