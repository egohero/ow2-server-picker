using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Ow2ServerPicker
{
    /// <summary>A region caption inside the server list.</summary>
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

    /// <summary>Column captions for the server list, sharing Theme's column geometry.</summary>
    internal sealed class ListHeader : Control
    {
        public ListHeader()
        {
            Height = Theme.S(34);
            Margin = Padding.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Surface);

            TextRenderer.DrawText(g, "DATACENTER", Theme.Eyebrow,
                new Rectangle(Theme.NameLeft, 0, Theme.S(240), Height), Theme.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            DrawRight(g, "CODE", Theme.CodeRight);
            DrawRight(g, "PING", Theme.PingRight);
            DrawRight(g, "RANGES", Theme.RangeRight);

            using (Pen p = new Pen(Theme.Border))
                g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        private void DrawRight(Graphics g, string text, int rightInset)
        {
            Rectangle r = new Rectangle(0, 0, Width - rightInset, Height);
            TextRenderer.DrawText(g, text, Theme.Eyebrow, r, Theme.TextFaint,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>
    /// One selectable datacenter. Drawn by hand rather than using a ListView row: the native
    /// control cannot be themed past a point, and its checkbox hit-testing was the source of a
    /// startup crash earlier in this project's history.
    /// </summary>
    internal sealed class ServerRow : Control
    {
        public readonly Datacenter Dc;
        private bool _hot;
        private long _ping = -2; // -2 = never measured, -1 = no reply

        public event EventHandler CheckedChanged;

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
