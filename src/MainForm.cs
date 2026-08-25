using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Ow2ServerPicker
{
    internal sealed class MainForm : Form, IMessageFilter
    {
        private const int WmMouseWheel = 0x020A;

        private readonly Catalog _catalog;
        private readonly string _catalogSource;
        private readonly List<ServerRow> _rows = new List<ServerRow>();

        private Segmented _mode;
        private SmoothPanel _viewport;
        private SmoothFlow _listFlow;
        private ThinScrollBar _scroll;
        private ListHeader _header;
        private readonly Dictionary<string, SectionHeader> _sections =
            new Dictionary<string, SectionHeader>();
        private Label _summaryMain;
        private Label _summarySub;
        private Label _scopeLabel;
        private Label _footer;
        private FlatBtn _apply;
        private FlatBtn _pingBtn;
        private FlatBtn _locate;

        private string _gamePath;
        private string _gamePathSource;
        private bool _pinging;
        private string _restoredNote;

        public MainForm(Catalog catalog, string catalogSource)
        {
            _catalog = catalog;
            _catalogSource = catalogSource;

            Theme.Init(this);
            BuildUi();
            Populate();

            Located found = OverwatchLocator.Locate();
            if (found != null) { _gamePath = found.Path; _gamePathSource = found.Source; }
            UpdateScope();
            RestoreFromFirewall();
            RefreshStatus();
            UpdateSummary();

            Application.AddMessageFilter(this);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            base.OnFormClosed(e);
        }

        // ------------------------------------------------------------------ chrome

        private void BuildUi()
        {
            Text = "Overwatch 2 Server Picker  " + VersionShort();
            LoadAppIcon();
            // Sizing is driven by measured text and Theme.S(), so the framework must not
            // also scale things - that double-application is what clipped the old labels.
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            ClientSize = new Size(Theme.S(860), Theme.S(760));
            MinimumSize = new Size(Theme.S(720), Theme.S(600));
            StartPosition = FormStartPosition.CenterScreen;

            // One AutoSize-rows table: every band gets exactly the height its content needs,
            // and only the list stretches. This is what removes the clipping entirely.
            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Bg,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(Theme.S(24), Theme.S(20), Theme.S(24), Theme.S(14))
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildHeading(), 0, 0);
            root.Controls.Add(BuildMode(), 0, 1);
            root.Controls.Add(BuildToolbar(), 0, 2);
            root.Controls.Add(BuildList(), 0, 3);
            root.Controls.Add(BuildSummary(), 0, 4);
            root.Controls.Add(BuildActions(), 0, 5);

            _footer = new Label
            {
                Dock = DockStyle.Bottom,
                Height = Theme.S(30),
                BackColor = Theme.Surface,
                ForeColor = Theme.TextFaint,
                Font = Theme.Small,
                Padding = new Padding(Theme.S(24), 0, Theme.S(24), 0),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Controls.Add(root);
            Controls.Add(_footer);
        }

        /// <summary>
        /// Version plus the binary's own timestamp. The timestamp answers "am I running the
        /// build I just made?", which the version number alone cannot - it survives a file
        /// copy, so an install folder that was never updated shows its real age.
        /// </summary>
        private static string VersionLine()
        {
            try
            {
                Assembly a = Assembly.GetExecutingAssembly();
                string v = a.GetName().Version.ToString(3);
                string built = "";
                try
                {
                    string loc = a.Location;
                    if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                        built = File.GetLastWriteTime(loc).ToString(" (built d MMM yyyy HH:mm)");
                }
                catch { }
                return "v" + v + built;
            }
            catch { return ""; }
        }

        private static string VersionShort()
        {
            try { return "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
            catch { return ""; }
        }

        /// <summary>
        /// /win32icon gives the exe its Explorer icon, but WinForms still shows its own
        /// default in the title bar unless Form.Icon is set, so the same .ico is embedded
        /// as a managed resource and loaded here. Absent in test builds, hence the guard.
        /// </summary>
        private void LoadAppIcon()
        {
            try
            {
                using (Stream s = System.Reflection.Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream("app.ico"))
                    if (s != null) Icon = new Icon(s);
            }
            catch { }
        }

        /// <summary>Stacks labels top-down, each taking its own measured height.</summary>
        private static FlowLayoutPanel Stack(int bottomPad)
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Bg,
                Margin = new Padding(0, 0, 0, bottomPad)
            };
        }

        private static Label Line(string text, Font font, Color ink)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = ink,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Theme.S(2))
            };
        }

        private Control BuildHeading()
        {
            FlowLayoutPanel host = Stack(Theme.S(14));
            host.Controls.Add(Line("Choose where you're willing to play", Theme.H1, Theme.Text));
            host.Controls.Add(Line(
                "Checked datacenters stay reachable. Everything else is blocked for Overwatch only.",
                Theme.Body, Theme.TextDim));
            return host;
        }

        private Control BuildMode()
        {
            FlowLayoutPanel host = Stack(Theme.S(12));
            _mode = new Segmented("Play only on checked", "Block checked");
            _mode.Margin = Padding.Empty;
            _mode.SelectedChanged += delegate { UpdateSummary(); };
            host.Controls.Add(_mode);
            return host;
        }

        private Control BuildToolbar()
        {
            SmoothPanel host = new SmoothPanel
            {
                Dock = DockStyle.Top,
                BackColor = Theme.Bg,
                Margin = new Padding(0, 0, 0, Theme.S(8))
            };

            FlowLayoutPanel left = new FlowLayoutPanel
            {
                Location = new Point(0, 0),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Theme.Bg
            };

            string[][] actions =
            {
                new[] { "Select all", "all" },
                new[] { "Deselect all", "none" },
                new[] { "Invert", "invert" },
            };
            foreach (string[] a in actions)
            {
                FlatBtn b = new FlatBtn(a[0], BtnKind.Ghost).WithHeight(30);
                string what = a[1];
                b.Click += delegate
                {
                    if (what == "all") SetAll(true);
                    else if (what == "none") SetAll(false);
                    else Invert();
                };
                left.Controls.Add(b);
            }

            _pingBtn = new FlatBtn("Ping all", BtnKind.Solid).WithHeight(30);
            _pingBtn.Click += delegate { StartPing(); };

            host.Height = _pingBtn.Height;
            host.Controls.Add(left);
            host.Controls.Add(_pingBtn);
            host.Resize += delegate { _pingBtn.Location = new Point(host.Width - _pingBtn.Width, 0); };
            return host;
        }

        private Control BuildList()
        {
            SmoothPanel frame = new SmoothPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Surface,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, 0, Theme.S(14))
            };
            frame.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(p, 0, 0, frame.Width - 1, frame.Height - 1);
            };

            // AutoScroll is deliberately off: the native scrollbar renders in the system
            // light theme. The viewport is scrolled by shifting the flow panel instead.
            _viewport = new SmoothPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
            _listFlow = new SmoothFlow
            {
                Location = new Point(0, 0),
                BackColor = Theme.Surface,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 0, Theme.S(6))
            };

            _scroll = new ThinScrollBar { Dock = DockStyle.Right };
            _scroll.ValueChanged += delegate { _listFlow.Top = -_scroll.Value; };

            _viewport.Controls.Add(_listFlow);
            _viewport.Resize += delegate { LayoutList(); };

            _header = new ListHeader { Dock = DockStyle.Top };
            _header.SortChanged += delegate { RebuildList(); };

            ToolTip tip = new ToolTip();
            tip.SetToolTip(_header,
                "Click a column to sort. Click again to reverse, a third time to group by region.");

            frame.Controls.Add(_viewport);
            frame.Controls.Add(_scroll);
            frame.Controls.Add(_header);
            return frame;
        }

        private Control BuildSummary()
        {
            FlowLayoutPanel host = Stack(Theme.S(12));
            _summaryMain = Line("", Theme.Semi, Theme.Text);
            _summarySub = Line("", Theme.Small, Theme.TextDim);
            _scopeLabel = Line("", Theme.Small, Theme.TextFaint);
            host.Controls.Add(_summaryMain);
            host.Controls.Add(_summarySub);
            host.Controls.Add(_scopeLabel);
            return host;
        }

        private Control BuildActions()
        {
            SmoothPanel host = new SmoothPanel { Dock = DockStyle.Top, BackColor = Theme.Bg };

            _apply = new FlatBtn("Apply", BtnKind.Primary).WithHeight(38);
            _apply.Location = new Point(0, 0);
            _apply.Click += delegate { Apply(); };

            FlatBtn clear = new FlatBtn("Remove all blocks", BtnKind.Solid).WithHeight(38);
            clear.Location = new Point(_apply.Width + Theme.S(10), 0);
            clear.Click += delegate { ClearRules(); };

            _locate = new FlatBtn("Locate Overwatch.exe", BtnKind.Ghost).WithHeight(38);
            _locate.Click += delegate { BrowseForGame(); };

            host.Height = _apply.Height;
            host.Controls.Add(_apply);
            host.Controls.Add(clear);
            host.Controls.Add(_locate);
            host.Resize += delegate { _locate.Location = new Point(host.Width - _locate.Width, 0); };
            return host;
        }

        // ------------------------------------------------------------------- rows

        private void Populate()
        {
            foreach (Datacenter dc in _catalog.Datacenters)
            {
                ServerRow row = new ServerRow(dc);
                row.CheckedChanged += delegate { UpdateSummary(); };
                _rows.Add(row);

                if (!_sections.ContainsKey(dc.Region))
                    _sections[dc.Region] = new SectionHeader(dc.Region);
            }
            RebuildList();
        }

        /// <summary>
        /// Rebuilds the list for the current sort. Region captions only make sense in the
        /// default order - once a sort is active the point is to compare across regions,
        /// so the list goes flat.
        ///
        /// Rows and section headers are reused rather than recreated: Controls.Clear does
        /// not dispose them, so rebuilding is just a reparent and per-row state such as a
        /// measured ping survives.
        /// </summary>
        private void RebuildList()
        {
            _listFlow.SuspendLayout();
            _listFlow.Controls.Clear();

            if (_header.Key == SortKey.None)
            {
                string region = null;
                foreach (Datacenter dc in _catalog.Datacenters)   // catalog is already region-ordered
                {
                    if (dc.Region != region)
                    {
                        region = dc.Region;
                        _listFlow.Controls.Add(_sections[region]);
                    }
                    _listFlow.Controls.Add(RowFor(dc));
                }
            }
            else
            {
                List<ServerRow> sorted = new List<ServerRow>(_rows);
                Sorting.Sort(sorted, _header.Key, _header.Descending);
                foreach (ServerRow r in sorted) _listFlow.Controls.Add(r);
            }

            _listFlow.ResumeLayout();
            LayoutList();
            _scroll.Value = 0;
        }

        private ServerRow RowFor(Datacenter dc)
        {
            foreach (ServerRow r in _rows) if (r.Dc == dc) return r;
            return null;
        }

        /// <summary>
        /// Makes the opening state match the firewall rather than defaulting to
        /// everything-checked. The firewall is the source of truth on purpose: a settings
        /// file would drift the moment rules were changed by this app running elevated
        /// elsewhere, removed by hand, or cleared by uninstall.
        ///
        /// Restored selections are always shown in "play only on checked" terms, because
        /// that is what the rules actually encode - the set that stayed reachable. Which of
        /// the two modes originally produced them is not recorded and does not matter, since
        /// both reduce to the same block set.
        /// </summary>
        private void RestoreFromFirewall()
        {
            FirewallManager.ActiveState state;
            try { state = FirewallManager.ReadActive(); }
            catch { return; }              // status line already reports firewall trouble

            if (state.RuleCount == 0) return;   // nothing applied: leave the all-checked default

            _mode.SelectedIndex = 0;
            _restoredNote = null;

            if (state.PlayableCodes != null)
            {
                Dictionary<string, bool> playable = new Dictionary<string, bool>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (string code in state.PlayableCodes) playable[code] = true;

                int matched = 0;
                foreach (ServerRow row in _rows)
                {
                    bool keep = playable.ContainsKey(row.Dc.Code);
                    if (keep) matched++;
                    row.SetChecked(keep);
                }

                // Codes in the rules that this catalog no longer knows about mean the two
                // have diverged; say so instead of quietly showing a smaller selection.
                if (matched != state.PlayableCodes.Count)
                    _restoredNote = string.Format(
                        "Restored from {0} active rule(s); {1} of {2} saved datacenter(s) are not in this catalog.",
                        state.RuleCount, state.PlayableCodes.Count - matched, state.PlayableCodes.Count);
                else
                    _restoredNote = string.Format("Restored {0} playable datacenter(s) from {1} active rule(s).",
                        matched, state.RuleCount);
                return;
            }

            // No usable description - fall back to addresses. A datacenter counts as
            // playable when none of its space is blocked.
            int kept = 0;
            foreach (ServerRow row in _rows)
            {
                List<Interval> survives = IpMath.Subtract(row.Dc.Ranges, state.Blocked);
                bool keep = IpMath.TotalAddresses(survives) == IpMath.TotalAddresses(row.Dc.Ranges);
                if (keep) kept++;
                row.SetChecked(keep);
            }
            _restoredNote = string.Format(
                "Restored {0} playable datacenter(s) from {1} rule(s) by address; "
                + "overlapping datacenters may be approximate.", kept, state.RuleCount);
        }

        /// <summary>Widths follow the viewport; the scrollbar is told how much content there is.</summary>
        private void LayoutList()
        {
            if (_viewport == null || _listFlow == null) return;
            int w = _viewport.ClientSize.Width;
            if (w <= 0) return;

            _listFlow.SuspendLayout();
            foreach (Control c in _listFlow.Controls) c.Width = w;
            _listFlow.Width = w;
            _listFlow.ResumeLayout();

            _scroll.Configure(_listFlow.Height, _viewport.ClientSize.Height);
            _listFlow.Top = -_scroll.Value;
        }

        private void SetAll(bool value)
        {
            foreach (ServerRow r in _rows) r.SetChecked(value);
            UpdateSummary();
        }

        private void Invert()
        {
            foreach (ServerRow r in _rows) r.SetChecked(!r.Dc.Selected);
            UpdateSummary();
        }

        /// <summary>Routes the wheel to the list whenever the pointer is over it, focus or not.</summary>
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmMouseWheel || _viewport == null || !_scroll.Needed) return false;

            int lparam = (int)(m.LParam.ToInt64() & 0xFFFFFFFF);
            Point screen = new Point((short)(lparam & 0xFFFF), (short)((lparam >> 16) & 0xFFFF));
            if (!_viewport.RectangleToScreen(_viewport.ClientRectangle).Contains(screen)) return false;

            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            _scroll.Value -= delta / 120 * Theme.RowHeight * 2;
            return true;
        }

        // -------------------------------------------------------------- selection

        /// <summary>
        /// Turns the current selection into the exact set of intervals to block.
        ///
        /// Both modes reduce to the same operation: everything we do not want, minus
        /// everything we do. The subtraction is what makes overlapping datacenters safe -
        /// Singapore's ranges literally contain some of Sydney's, so blocking Singapore
        /// wholesale would take Sydney down with it.
        /// </summary>
        private List<Interval> ComputeBlockSet(out List<Datacenter> kept, out List<Datacenter> kill)
        {
            bool allowOnly = _mode.SelectedIndex == 0;
            kept = new List<Datacenter>();
            kill = new List<Datacenter>();

            List<Interval> keepRanges = new List<Interval>();
            List<Interval> blockRanges = new List<Interval>();

            // Iterating the catalog, not the visual rows: the catalog is always complete,
            // so a datacenter can never be silently dropped from the calculation.
            foreach (Datacenter dc in _catalog.Datacenters)
            {
                bool block = allowOnly ? !dc.Selected : dc.Selected;
                if (block) { kill.Add(dc); blockRanges.AddRange(dc.Ranges); }
                else { kept.Add(dc); keepRanges.AddRange(dc.Ranges); }
            }
            return IpMath.Subtract(blockRanges, keepRanges);
        }

        private void UpdateSummary()
        {
            List<Datacenter> kept, kill;
            List<Interval> blocked = ComputeBlockSet(out kept, out kill);

            if (kept.Count == 0)
            {
                _summaryMain.ForeColor = Theme.Far;
                _summaryMain.Text = "Nothing would be playable";
                _summarySub.Text = "Every known datacenter would be blocked. Check at least one server.";
                _apply.Enabled = false;
                return;
            }

            _apply.Enabled = true;
            _summaryMain.ForeColor = Theme.Text;

            if (blocked.Count == 0)
            {
                _summaryMain.Text = "Nothing will be blocked";
                _summarySub.Text = "All " + kept.Count + " datacenters stay reachable — applying now is a no-op.";
                return;
            }

            List<string> names = new List<string>();
            foreach (Datacenter dc in kept) names.Add(dc.Code);
            string list = names.Count <= 6
                ? string.Join(", ", names.ToArray())
                : string.Join(", ", names.GetRange(0, 6).ToArray()) + " +" + (names.Count - 6) + " more";

            _summaryMain.Text = "Playable: " + list;
            _summarySub.Text = string.Format(
                "Blocking {0} of {1} datacenters — {2:N0} addresses across {3} rule(s).",
                kill.Count, kept.Count + kill.Count,
                IpMath.TotalAddresses(blocked), (blocked.Count + 149) / 150);
        }

        private void UpdateScope()
        {
            if (string.IsNullOrEmpty(_gamePath))
            {
                _scopeLabel.ForeColor = Theme.Mid;
                _scopeLabel.Text = "Overwatch.exe not found — rules would apply machine-wide. "
                                 + "Use \"Locate Overwatch.exe\" to scope them to the game.";
            }
            else
            {
                _scopeLabel.ForeColor = Theme.TextFaint;
                // Naming the source matters: users see a Battle.net shortcut pointing at
                // "Overwatch Launcher.exe" and reasonably wonder why this says _retail_.
                _scopeLabel.Text = string.IsNullOrEmpty(_gamePathSource)
                    ? "Scoped to " + _gamePath
                    : "Scoped to " + _gamePath + "   (found via " + _gamePathSource + ")";
            }
        }

        // ------------------------------------------------------------------ ping

        private void StartPing()
        {
            if (_pinging) return;
            _pinging = true;
            _pingBtn.Enabled = false;
            _pingBtn.Text = "Pinging…";

            List<ServerRow> rows = new List<ServerRow>(_rows);
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (ServerRow row in rows)
                {
                    Datacenter dc = row.Dc;
                    if (string.IsNullOrEmpty(dc.PingTarget)) continue;

                    long best = -1;
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        try
                        {
                            using (Ping p = new Ping())
                            {
                                PingReply reply = p.Send(dc.PingTarget, 1500);
                                if (reply != null && reply.Status == IPStatus.Success)
                                    if (best < 0 || reply.RoundtripTime < best) best = reply.RoundtripTime;
                            }
                        }
                        catch { }
                    }

                    long result = best;
                    ServerRow target = row;
                    try { BeginInvoke((MethodInvoker)delegate { target.SetPing(result); }); }
                    catch { return; }
                }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _pinging = false;
                        _pingBtn.Enabled = true;
                        _pingBtn.Text = "Ping all";
                        // Fresh numbers under a stale ping order would be actively wrong.
                        if (_header.Key == SortKey.Ping) RebuildList();
                        _footer.Text = "Ping done.  – means no probe address in servers.json; "
                                     + "n/a means the probe did not answer ICMP.";
                    });
                }
                catch { }
            });
        }

        // ---------------------------------------------------------------- actions

        private void BrowseForGame()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Select Overwatch.exe";
                dlg.Filter = "Overwatch executable|Overwatch.exe|All executables|*.exe";
                if (_gamePath != null && File.Exists(_gamePath))
                    dlg.InitialDirectory = Path.GetDirectoryName(_gamePath);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _gamePath = dlg.FileName;
                    _gamePathSource = "chosen by you";
                    UpdateScope();
                }
            }
        }

        private void Apply()
        {
            List<Datacenter> kept, kill;
            List<Interval> blocked = ComputeBlockSet(out kept, out kill);

            if (blocked.Count == 0)
            {
                MessageBox.Show(this, "Nothing to block with this selection.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(_gamePath))
            {
                DialogResult r = MessageBox.Show(this,
                    "Overwatch.exe has not been located, so these rules will apply to every program "
                    + "on this machine.\r\n\r\nMany Overwatch datacenters are hosted on Google Cloud, so a "
                    + "machine-wide block can also affect other software using those addresses.\r\n\r\n"
                    + "Continue anyway?",
                    "No game path set", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            List<string> keptNames = new List<string>();
            foreach (Datacenter dc in kept) keptNames.Add(dc.Code);

            try
            {
                Cursor = Cursors.WaitCursor;
                int rules = FirewallManager.Apply(blocked, _gamePath,
                    "Overwatch 2 Server Picker - playable: " + string.Join(", ", keptNames.ToArray()),
                    _catalog.GameUdpPorts);
                RefreshStatus();
                MessageBox.Show(this,
                    string.Format("Created {0} firewall rule(s).\r\n\r\nPlayable: {1}\r\n\r\n"
                        + "Fully quit and relaunch Overwatch for this to take effect.",
                        rules, string.Join(", ", keptNames.ToArray())),
                    "Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not apply the rules:\r\n\r\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void ClearRules()
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                int removed = FirewallManager.RemoveAll();
                RefreshStatus();
                MessageBox.Show(this,
                    removed == 0
                        ? "There were no rules from this app to remove."
                        : string.Format("Removed {0} rule(s). Every datacenter is reachable again.", removed),
                    "Removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not remove the rules:\r\n\r\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void RefreshStatus()
        {
            try
            {
                int active = FirewallManager.ListOurRuleNames().Count;
                if (!string.IsNullOrEmpty(_restoredNote))
                {
                    _footer.Text = _restoredNote + "   ·   " + VersionLine();
                    _restoredNote = null;   // shown once; later refreshes report live status
                    return;
                }
                _footer.Text = string.Format("{0}   ·   {1} datacenters, catalog {2}   ·   {3}",
                    active == 0 ? "No blocks active" : active + " block rule(s) active",
                    _catalog.Datacenters.Count, _catalog.Updated, VersionLine());
            }
            catch (Exception ex)
            {
                _footer.Text = "Firewall status unavailable: " + ex.Message;
            }
        }
    }
}
