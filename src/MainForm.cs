using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace Ow2ServerPicker
{
    internal sealed class MainForm : Form
    {
        private readonly Catalog _catalog;
        private readonly string _catalogSource;

        private ListView _list;
        private RadioButton _modeAllow;
        private RadioButton _modeBlock;
        private Label _pathLabel;
        private Button _applyButton;
        private Button _clearButton;
        private Button _pingButton;
        private Label _summary;
        private StatusStrip _status;
        private ToolStripStatusLabel _statusText;

        private string _gamePath;
        private bool _populating;
        private bool _pinging;
        private bool _uiReady;

        public MainForm(Catalog catalog, string catalogSource)
        {
            _catalog = catalog;
            _catalogSource = catalogSource;
            BuildUi();
            Populate();
            _gamePath = OverwatchLocator.Find();
            UpdatePathLabel();
            RefreshStatus();
            UpdateSummary();
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            Text = "Overwatch 2 Server Picker";
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(760, 640);
            MinimumSize = new Size(640, 520);
            StartPosition = FormStartPosition.CenterScreen;

            Label heading = new Label
            {
                Text = "Choose which datacenters Overwatch is allowed to reach.",
                Dock = DockStyle.Top,
                Height = 26,
                Padding = new Padding(12, 8, 12, 0),
                Font = new Font("Segoe UI", 9.75f, FontStyle.Bold)
            };

            _modeAllow = new RadioButton
            {
                Text = "Play only on checked servers  (block everything else)",
                Checked = true,
                AutoSize = true,
                Location = new Point(14, 4)
            };
            _modeBlock = new RadioButton
            {
                Text = "Block checked servers  (leave the rest alone)",
                AutoSize = true,
                Location = new Point(14, 26)
            };
            _modeAllow.CheckedChanged += delegate { UpdateSummary(); };
            _modeBlock.CheckedChanged += delegate { UpdateSummary(); };

            Panel modePanel = new Panel { Dock = DockStyle.Top, Height = 52 };
            modePanel.Controls.Add(_modeAllow);
            modePanel.Controls.Add(_modeBlock);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = false,
                HideSelection = false,
                ShowGroups = true
            };
            _list.Columns.Add("Datacenter", 260);
            _list.Columns.Add("Code", 70);
            _list.Columns.Add("Ping", 70, HorizontalAlignment.Right);
            _list.Columns.Add("IP ranges", 80, HorizontalAlignment.Right);
            _list.ItemChecked += delegate(object sender, ItemCheckedEventArgs e)
            {
                Datacenter dc = e.Item.Tag as Datacenter;
                if (dc != null) dc.Selected = e.Item.Checked;
                if (!_populating && _uiReady) UpdateSummary();
            };

            Panel listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6) };
            listHost.Controls.Add(_list);

            FlowLayoutPanel selectBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(10, 4, 10, 0),
                FlowDirection = FlowDirection.LeftToRight
            };
            selectBar.Controls.Add(MakeButton("Select all", delegate { SetAll(true); }, 90));
            selectBar.Controls.Add(MakeButton("Deselect all", delegate { SetAll(false); }, 96));
            selectBar.Controls.Add(MakeButton("Invert", delegate { Invert(); }, 70));
            _pingButton = MakeButton("Ping all", delegate { StartPing(); }, 80);
            selectBar.Controls.Add(_pingButton);

            _summary = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(14, 4, 14, 0),
                ForeColor = SystemColors.GrayText
            };

            _pathLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(14, 6, 14, 0),
                AutoEllipsis = true
            };

            FlowLayoutPanel actionBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(10, 2, 10, 0),
                FlowDirection = FlowDirection.LeftToRight
            };
            _applyButton = MakeButton("Apply", delegate { Apply(); }, 110);
            _applyButton.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            actionBar.Controls.Add(_applyButton);
            _clearButton = MakeButton("Remove all blocks", delegate { ClearRules(); }, 130);
            actionBar.Controls.Add(_clearButton);
            actionBar.Controls.Add(MakeButton("Locate Overwatch.exe", delegate { BrowseForGame(); }, 150));

            _statusText = new ToolStripStatusLabel("");
            _status = new StatusStrip();
            _status.Items.Add(_statusText);

            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 122 };
            bottom.Controls.Add(actionBar);
            bottom.Controls.Add(_pathLabel);
            bottom.Controls.Add(_summary);

            Controls.Add(listHost);
            Controls.Add(selectBar);
            Controls.Add(modePanel);
            Controls.Add(heading);
            Controls.Add(bottom);
            Controls.Add(_status);
        }

        private static Button MakeButton(string text, EventHandler onClick, int width)
        {
            Button b = new Button { Text = text, Width = width, Height = 26, Margin = new Padding(3, 2, 3, 2) };
            b.Click += onClick;
            return b;
        }

        private void Populate()
        {
            _populating = true;
            _list.BeginUpdate();
            try
            {
                Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>();
                foreach (Datacenter dc in _catalog.Datacenters)
                {
                    ListViewGroup g;
                    if (!groups.TryGetValue(dc.Region, out g))
                    {
                        g = new ListViewGroup(dc.Region);
                        groups[dc.Region] = g;
                        _list.Groups.Add(g);
                    }

                    ListViewItem item = new ListViewItem(dc.Name, g);
                    item.SubItems.Add(dc.Code);
                    item.SubItems.Add(dc.PingTarget == null ? "-" : "?");
                    item.SubItems.Add(dc.Ranges.Count.ToString());
                    item.Tag = dc;
                    // Everything checked means "block nothing" - a no-op default, so an
                    // accidental Apply on first launch cannot lock the player out.
                    item.Checked = dc.Selected;
                    _list.Items.Add(item);
                }
            }
            finally
            {
                _list.EndUpdate();
                _populating = false;
            }
        }

        // ------------------------------------------------------------- selection

        private void SetAll(bool value)
        {
            _populating = true;
            foreach (ListViewItem i in _list.Items) i.Checked = value;
            _populating = false;
            UpdateSummary();
        }

        private void Invert()
        {
            _populating = true;
            foreach (ListViewItem i in _list.Items) i.Checked = !i.Checked;
            _populating = false;
            UpdateSummary();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Safe from here: the native handle exists, so item/Tag mapping is settled.
            _uiReady = true;
            UpdateSummary();
        }

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
            bool allowOnly = _modeAllow.Checked;
            kept = new List<Datacenter>();
            kill = new List<Datacenter>();

            List<Interval> keepRanges = new List<Interval>();
            List<Interval> blockRanges = new List<Interval>();

            // Iterating the catalog, not the ListView: the catalog is always complete,
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
                _summary.ForeColor = Color.Firebrick;
                _summary.Text = "Nothing is playable with this selection - every known datacenter "
                              + "would be blocked. Check at least one server.";
                _applyButton.Enabled = false;
                return;
            }

            _applyButton.Enabled = true;
            _summary.ForeColor = SystemColors.GrayText;
            _summary.Text = string.Format(
                "Playable: {0} datacenter(s).  Blocking {1} of {2} ({3:N0} addresses across {4} rule sets).",
                kept.Count, kill.Count, kept.Count + kill.Count,
                IpMath.TotalAddresses(blocked), Math.Max(1, (blocked.Count + 149) / 150));
        }

        // ------------------------------------------------------------------ ping

        private void StartPing()
        {
            if (_pinging) return;
            _pinging = true;
            _pingButton.Enabled = false;
            _statusText.Text = "Pinging...";

            List<ListViewItem> items = new List<ListViewItem>();
            foreach (ListViewItem i in _list.Items) items.Add(i);

            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (ListViewItem item in items)
                {
                    Datacenter dc = (Datacenter)item.Tag;
                    string text = "-";
                    if (!string.IsNullOrEmpty(dc.PingTarget))
                    {
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
                        text = best < 0 ? "n/a" : best + " ms";
                    }

                    string captured = text;
                    ListViewItem captureItem = item;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            captureItem.SubItems[2].Text = captured;
                        });
                    }
                    catch { return; }
                }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _pinging = false;
                        _pingButton.Enabled = true;
                        _statusText.Text = "Ping complete. Entries showing '-' have no probe address in servers.json.";
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
                    UpdatePathLabel();
                }
            }
        }

        private void UpdatePathLabel()
        {
            if (string.IsNullOrEmpty(_gamePath))
            {
                _pathLabel.ForeColor = Color.DarkOrange;
                _pathLabel.Text = "Overwatch.exe not found. Rules would apply to the whole machine - "
                                + "click \"Locate Overwatch.exe\" to scope them to the game instead.";
            }
            else
            {
                _pathLabel.ForeColor = SystemColors.GrayText;
                _pathLabel.Text = "Rules apply only to: " + _gamePath;
            }
        }

        private void Apply()
        {
            List<Datacenter> kept, kill;
            List<Interval> blocked = ComputeBlockSet(out kept, out kill);

            if (blocked.Count == 0)
            {
                MessageBox.Show(this, "Nothing to block with this selection.", "Overwatch 2 Server Picker",
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
                string summary = "Overwatch 2 Server Picker - playable: " + string.Join(", ", keptNames.ToArray());
                int rules = FirewallManager.Apply(blocked, _gamePath, summary);
                RefreshStatus();
                MessageBox.Show(this,
                    string.Format(
                        "Created {0} firewall rule(s).\r\n\r\nPlayable datacenters: {1}\r\n\r\n"
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
                _statusText.Text = string.Format(
                    "{0}  |  catalog {1} ({2} datacenters, updated {3})",
                    active == 0 ? "No blocks active" : active + " block rule(s) active",
                    Path.GetFileName(_catalogSource), _catalog.Datacenters.Count, _catalog.Updated);
            }
            catch (Exception ex)
            {
                _statusText.Text = "Firewall status unavailable: " + ex.Message;
            }
        }
    }
}
