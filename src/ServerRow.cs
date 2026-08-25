using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Ow2ServerPicker
{
    /// <summary>A region caption inside the server list. Only shown when unsorted.</summary>
    internal sealed class SectionHeader : Control
    {
        public SectionHeader(string title)
        {
            Text = title;
            Height = Theme.S(30);
            Margin = Padding.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);
            Rectangle r = new Rectangle(Theme.PadLeft, 0, Width - Theme.PadLeft, Height - Theme.S(6));
            TextRenderer.DrawText(g, Text.ToUpperInvariant(), Theme.Eyebrow, r, Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);
            using (Pen p = new Pen(Theme.BorderSoft))
                g.DrawLine(p, Theme.PadLeft, Height - 1, Width - Theme.RangeRight, Height - 1);
        }
    }

    /// <summary>
    /// Clickable column captions, sharing Theme's column geometry with ServerRow.
    ///
    /// Each header cycles through three states: ascending, descending, then back to the
    /// default region grouping. The third state matters - once a sort flattens the list,
    /// there would otherwise be no way back to the grouped view.
    /// </summary>
    internal sealed class ListHeader : Control
    {
        private struct Col
        {
            public string Label;
            public SortKey Key;
            public int RightInset;
            public bool LeftAligned;
        }

        public SortKey Key = SortKey.None;
        public bool Descending;

        public event EventHandler SortChanged;

        private SortKey _hot = SortKey.None;

        public ListHeader()
        {
            Height = Theme.S(34);
            Margin = Padding.Empty;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private Col[] Columns()
        {
            return new[]
            {
                new Col { Label = "DATACENTER", Key = SortKey.Name,   RightInset = 0, LeftAligned = true },
                new Col { Label = "CODE",       Key = SortKey.Code,   RightInset = Theme.CodeRight },
                new Col { Label = "PING",       Key = SortKey.Ping,   RightInset = Theme.PingRight },
                new Col { Label = "RANGES",     Key = SortKey.Ranges, RightInset = Theme.RangeRight },
            };
        }

        /// <summary>The clickable band for a column: its label plus room for the arrow.</summary>
        private Rectangle Band(Col c)
        {
            int pad = Theme.S(8);
            int arrow = Theme.S(14);
            int labelW = Theme.TextWidth(c.Label, Theme.Eyebrow);

            if (c.LeftAligned)
            {
                int w = labelW + arrow + pad;
                return new Rectangle(Theme.NameLeft - pad, 0, w, Height);
            }
            int right = Width - c.RightInset;
            int width = labelW + arrow + pad;
            return new Rectangle(right - width + pad, 0, width, Height);
        }

        private bool TryHit(int x, out Col hit)
        {
            hit = default(Col);
            foreach (Col c in Columns())
            {
                Rectangle b = Band(c);
                if (x >= b.Left && x <= b.Right) { hit = c; return true; }
            }
            return false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Col c;
            SortKey k = TryHit(e.X, out c) ? c.Key : SortKey.None;
            if (k != _hot) { _hot = k; Invalidate(); }
            Cursor = k == SortKey.None ? Cursors.Default : Cursors.Hand;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hot = SortKey.None;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Col c;
            if (TryHit(e.X, out c))
            {
                if (Key != c.Key) { Key = c.Key; Descending = false; }
                else if (!Descending) { Descending = true; }
                else { Key = SortKey.None; Descending = false; }

                Invalidate();
                if (SortChanged != null) SortChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);

            foreach (Col c in Columns())
            {
                bool active = Key == c.Key;
                Color ink = active ? Theme.Accent : (_hot == c.Key ? Theme.TextDim : Theme.TextFaint);
                int labelW = Theme.TextWidth(c.Label, Theme.Eyebrow);

                if (c.LeftAligned)
                {
                    TextRenderer.DrawText(g, c.Label, Theme.Eyebrow,
                        new Rectangle(Theme.NameLeft, 0, labelW + Theme.S(4), Height), ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    if (active) DrawArrow(g, Theme.NameLeft + labelW + Theme.S(6), ink);
                }
                else
                {
                    int right = Width - c.RightInset;
                    TextRenderer.DrawText(g, c.Label, Theme.Eyebrow,
                        new Rectangle(0, 0, right, Height), ink,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    if (active) DrawArrow(g, right - labelW - Theme.S(13), ink);
                }
            }

            using (Pen p = new Pen(Theme.Border))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        private void DrawArrow(Graphics g, int x, Color ink)
        {
            int w = Theme.S(7);
            int h = Theme.S(4);
            int cy = Height / 2;
            Point[] pts = Descending
                ? new[] { new Point(x, cy - h / 2), new Point(x + w, cy - h / 2), new Point(x + w / 2, cy + h) }
                : new[] { new Point(x, cy + h / 2), new Point(x + w, cy + h / 2), new Point(x + w / 2, cy - h) };
            using (SolidBrush b = new SolidBrush(ink)) g.FillPolygon(b, pts);
        }
    }

    /// <summary>
    /// One selectable datacenter. Drawn by hand rather than using a ListView row: the native
    /// control cannot be themed past a point, and its checkbox hit-testing was the source of a
    /// startup crash earlier in this project's history.
    /// </summary>
    internal sealed class ServerRow : Control, ISortableRow
    {
        public readonly Datacenter Dc;
        private bool _hot;
        private long _ping = -2; // -2 = never measured, -1 = no reply

        public event EventHandler CheckedChanged;

        /// <summary>Round-trip in ms, or negative when there is no usable reading.</summary>
        public long PingMs { get { return _ping; } }

        public bool HasPing { get { return _ping >= 0; } }

        string ISortableRow.SortName { get { return Dc.Name; } }
        string ISortableRow.SortCode { get { return Dc.Code; } }
        int ISortableRow.SortRanges { get { return Dc.Ranges.Count; } }
        long ISortableRow.SortPing { get { return _ping; } }

        public ServerRow(Datacenter dc)
        {
            Dc = dc;
            Height = Theme.RowHeight;
            Margin = Padding.Empty;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetPing(long ms)
        {
            _ping = ms;
            Invalidate();
        }

        public void SetChecked(bool value)
        {
            Dc.Selected = value;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Dc.Selected = !Dc.Selected;
            Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        private string PingText()
        {
            if (string.IsNullOrEmpty(Dc.PingTarget)) return "–";
            if (_ping == -2) return "";
            if (_ping < 0) return "n/a";
            return _ping + " ms";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(_hot ? Theme.Hover : Theme.Surface);

            // Selected rows get an accent spine on the left edge.
            if (Dc.Selected)
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, 0, 0, Theme.S(3), Height);

            DrawCheck(g, new Rectangle(Theme.PadLeft, (Height - Theme.CheckSize) / 2,
                                       Theme.CheckSize, Theme.CheckSize));

            Color nameInk = Dc.Selected ? Theme.Text : Theme.TextDim;
            int nameWidth = Width - Theme.NameLeft - Theme.CodeRight - Theme.S(12);
            if (nameWidth < Theme.S(60)) nameWidth = Theme.S(60);
            TextRenderer.DrawText(g, Dc.Name, Dc.Selected ? Theme.Semi : Theme.Body,
                new Rectangle(Theme.NameLeft, 0, nameWidth, Height), nameInk,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding);

            DrawRight(g, Dc.Code, Theme.CodeRight, Theme.Mono, Theme.TextFaint);

            string ping = PingText();
            Color pingInk = ping == "n/a" || ping == "–" ? Theme.TextFaint : Theme.PingColour(_ping);
            DrawRight(g, ping, Theme.PingRight, Theme.Mono, pingInk);

            DrawRight(g, Dc.Ranges.Count.ToString(), Theme.RangeRight, Theme.Mono, Theme.TextFaint);
        }

        private void DrawRight(Graphics g, string text, int rightInset, Font font, Color ink)
        {
            TextRenderer.DrawText(g, text, font, new Rectangle(0, 0, Width - rightInset, Height), ink,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawCheck(Graphics g, Rectangle box)
        {
            using (GraphicsPath p = Theme.Rounded(box, Theme.S(4)))
            {
                if (Dc.Selected)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, p);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(_hot ? Theme.Raised : Theme.Surface)) g.FillPath(b, p);
                    using (Pen pen = new Pen(_hot ? Theme.TextFaint : Theme.Border, 1.4f)) g.DrawPath(pen, p);
                }
            }

            if (!Dc.Selected) return;

            // Tick drawn in proportion to the box so it stays centred at any DPI.
            float u = box.Width / 18f;
            using (Pen tick = new Pen(Theme.AccentInk, Math.Max(1.6f, 2f * u)))
            {
                tick.StartCap = LineCap.Round;
                tick.EndCap = LineCap.Round;
                tick.LineJoin = LineJoin.Round;
                g.DrawLines(tick, new[]
                {
                    new PointF(box.X + 4.5f * u, box.Y + 9.5f * u),
                    new PointF(box.X + 7.5f * u, box.Y + 12.5f * u),
                    new PointF(box.X + 13.5f * u, box.Y + 5.5f * u)
                });
            }
        }
    }
}
