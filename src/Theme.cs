using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ow2ServerPicker
{
    /// <summary>
    /// The app's visual language. Deliberately dark and low-chroma so the only things
    /// carrying colour are the ping figures and the single primary action.
    ///
    /// Sizing rule: never hardcode a pixel width for anything containing text. Fonts are
    /// point-based and grow with DPI, so fixed boxes clip their own labels on any display
    /// above 100%. Widths come from TextRenderer.MeasureText; fixed gaps go through S().
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Bg         = Color.FromArgb(0x15, 0x17, 0x1B);
        public static readonly Color Surface    = Color.FromArgb(0x1D, 0x20, 0x25);
        public static readonly Color Raised     = Color.FromArgb(0x25, 0x29, 0x30);
        public static readonly Color Hover      = Color.FromArgb(0x2C, 0x31, 0x39);
        public static readonly Color Border     = Color.FromArgb(0x30, 0x35, 0x3D);
        public static readonly Color BorderSoft = Color.FromArgb(0x25, 0x2A, 0x31);

        public static readonly Color Text       = Color.FromArgb(0xEA, 0xEC, 0xEF);
        public static readonly Color TextDim    = Color.FromArgb(0x8D, 0x95, 0xA0);
        public static readonly Color TextFaint  = Color.FromArgb(0x5C, 0x64, 0x6E);

        public static readonly Color Accent     = Color.FromArgb(0xE8, 0x9B, 0x3C);
        public static readonly Color AccentLift = Color.FromArgb(0xF4, 0xAD, 0x55);
        public static readonly Color AccentInk  = Color.FromArgb(0x1A, 0x13, 0x06);

        public static readonly Color Good       = Color.FromArgb(0x63, 0xC3, 0x81);
        public static readonly Color Mid        = Color.FromArgb(0xD8, 0xA6, 0x4A);
        public static readonly Color Far        = Color.FromArgb(0xD9, 0x6B, 0x5E);

        public static readonly Font H1      = new Font("Segoe UI Semibold", 13.5f);
        public static readonly Font Body    = new Font("Segoe UI", 9.75f);
        public static readonly Font Semi    = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font Small   = new Font("Segoe UI", 8.75f);
        public static readonly Font Eyebrow = new Font("Segoe UI Semibold", 8f);
        // Tabular figures so ping and range counts line up down the column.
        public static readonly Font Mono    = new Font("Consolas", 9.75f);

        private static float _scale = 1f;

        public static void Init(Control c)
        {
            using (Graphics g = c.CreateGraphics()) _scale = g.DpiX / 96f;
        }

        /// <summary>Scales a 96-DPI pixel constant to the current display.</summary>
        public static int S(int px)
        {
            return (int)Math.Round(px * _scale);
        }

        public static int TextWidth(string s, Font f)
        {
            return TextRenderer.MeasureText(s, f).Width;
        }

        // Column geometry, as insets from the right edge. Rows and header share these.
        public static int PadLeft    { get { return S(16); } }
        public static int CheckSize  { get { return S(18); } }
        public static int NameLeft   { get { return S(46); } }
        public static int CodeRight  { get { return S(200); } }
        public static int PingRight  { get { return S(104); } }
        public static int RangeRight { get { return S(20); } }
        public static int RowHeight  { get { return S(38); } }

        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            if (radius <= 0 || d > r.Width || d > r.Height) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color PingColour(long ms)
        {
            if (ms < 0) return TextFaint;
            if (ms <= 60) return Good;
            if (ms <= 130) return Mid;
            return Far;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>
        /// Repaints the non-client title bar dark. Attribute 20 is the documented id on
        /// Windows 10 2004+; 19 was the pre-release id on 1809-1909. Both are tried and any
        /// failure is ignored, since an unsupported build simply keeps the light title bar.
        /// </summary>
        public static void DarkTitleBar(IntPtr handle)
        {
            int on = 1;
            try { if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                      DwmSetWindowAttribute(handle, 19, ref on, sizeof(int)); }
            catch { }
        }
    }

    /// <summary>Panel that paints without flicker; used for anything that redraws on hover or scroll.</summary>
    internal class SmoothPanel : Panel
    {
        public SmoothPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
    }

    internal class SmoothFlow : FlowLayoutPanel
    {
        public SmoothFlow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint, true);
        }
    }

    internal enum BtnKind { Primary, Solid, Ghost }

    /// <summary>
    /// Owner-drawn button that sizes itself to its own label. WinForms' FlatStyle cannot
    /// produce anti-aliased rounded corners or a real hover ramp, and that difference is
    /// most of why stock dialogs look dated.
    /// </summary>
    internal sealed class FlatBtn : Control
    {
        private bool _hot;
        private bool _down;
        public readonly BtnKind Kind;

        public FlatBtn(string text, BtnKind kind)
        {
            Kind = kind;
            Font = kind == BtnKind.Primary ? Theme.Semi : Theme.Body;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, Theme.S(8), 0);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            Text = text; // triggers the first fit
        }

        /// <summary>Extra room each side of the label. Primary actions get a wider gutter.</summary>
        private int PadX
        {
            get { return Kind == BtnKind.Ghost ? Theme.S(14) : Theme.S(20); }
        }

        public override string Text
        {
            get { return base.Text; }
            set { base.Text = value; FitToText(); Invalidate(); }
        }

        private void FitToText()
        {
            if (Font == null) return;
            Width = Theme.TextWidth(Text, Font) + PadX * 2;
            if (Height <= 0) Height = Theme.S(32);
        }

        /// <summary>Call after changing Height so the label keeps its gutter.</summary>
        public FlatBtn WithHeight(int h)
        {
            Height = Theme.S(h);
            FitToText();
            return this;
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, ink, edge;

            if (!Enabled)
            {
                fill = Theme.Surface; ink = Theme.TextFaint; edge = Theme.BorderSoft;
            }
            else if (Kind == BtnKind.Primary)
            {
                fill = _hot && !_down ? Theme.AccentLift : Theme.Accent;
                ink = Theme.AccentInk;
                edge = fill;
            }
            else if (Kind == BtnKind.Solid)
            {
                fill = _down ? Theme.Border : (_hot ? Theme.Hover : Theme.Raised);
                ink = Theme.Text;
                edge = Theme.Border;
            }
            else
            {
                fill = _hot ? Theme.Raised : Color.Transparent;
                ink = _hot ? Theme.Text : Theme.TextDim;
                edge = _hot ? Theme.Border : Theme.BorderSoft;
            }

            using (GraphicsPath path = Theme.Rounded(r, Theme.S(6)))
            {
                if (fill != Color.Transparent)
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                using (Pen p = new Pen(edge)) g.DrawPath(p, path);
            }

            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, _down ? 1 : 0, Width, Height), ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Two-option segmented control - clearer than a pair of radio buttons and far less dated.</summary>
    internal sealed class Segmented : Control
    {
        private readonly string[] _options;
        private int _hot = -1;
        private int _index;

        public event EventHandler SelectedChanged;

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (_index == value) return;
                _index = value;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        public Segmented(params string[] options)
        {
            _options = options;
            Height = Theme.S(38);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            // Width follows the widest label measured in the *bold* face, since the
            // selected segment renders semibold and must not reflow when selection moves.
            int widest = 0;
            foreach (string o in options)
            {
                int w = Theme.TextWidth(o, Theme.Semi);
                if (w > widest) widest = w;
            }
            Width = (widest + Theme.S(34)) * options.Length;
        }

        private int HitTest(int x)
        {
            int w = Width / _options.Length;
            int i = w == 0 ? 0 : x / w;
            return i < 0 ? 0 : (i >= _options.Length ? _options.Length - 1 : i);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.X);
            if (h != _hot) { _hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { SelectedIndex = HitTest(e.X); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent == null ? Theme.Bg : Parent.BackColor);

            Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Rounded(outer, Theme.S(8)))
            {
                using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }

            int seg = Width / _options.Length;
            int inset = Theme.S(3);
            for (int i = 0; i < _options.Length; i++)
            {
                Rectangle r = new Rectangle(i * seg + inset, inset, seg - inset * 2, Height - inset * 2 - 1);
                Color ink = Theme.TextDim;
                if (i == _index)
                {
                    using (GraphicsPath p = Theme.Rounded(r, Theme.S(6)))
                    using (SolidBrush b = new SolidBrush(Theme.Raised)) g.FillPath(b, p);
                    ink = Theme.Accent;
                }
                else if (i == _hot) ink = Theme.Text;

                TextRenderer.DrawText(g, _options[i], i == _index ? Theme.Semi : Font, r, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>
    /// Slim dark scrollbar. The native one renders in the system light theme and was the
    /// loudest remaining piece of stock chrome against this palette, so the list scrolls
    /// itself and draws this instead.
    /// </summary>
    internal sealed class ThinScrollBar : Control
    {
        private int _value;
        private int _content;
        private int _view;
        private bool _dragging;
        private int _grabOffset;
        private bool _hot;

        public event EventHandler ValueChanged;

        public ThinScrollBar()
        {
            Width = Theme.S(10);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public int Maximum
        {
            get { return Math.Max(0, _content - _view); }
        }

        public bool Needed
        {
            get { return Maximum > 0; }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (v > Maximum) v = Maximum;
                if (v == _value) return;
                _value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        public void Configure(int contentHeight, int viewHeight)
        {
            _content = contentHeight;
            _view = viewHeight;
            if (_value > Maximum) Value = Maximum;
            Visible = Needed;
            Invalidate();
        }

        private Rectangle Thumb()
        {
            if (!Needed) return Rectangle.Empty;
            int track = Height - Theme.S(8);
            int h = Math.Max(Theme.S(28), (int)((float)_view / _content * track));
            int y = Maximum == 0 ? 0 : (int)((float)_value / Maximum * (track - h));
            return new Rectangle(Theme.S(2), Theme.S(4) + y, Width - Theme.S(4), h);
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle t = Thumb();
            if (t.Contains(e.Location)) { _dragging = true; _grabOffset = e.Y - t.Y; }
            else if (Needed)
            {
                int track = Height - Theme.S(8) - t.Height;
                if (track > 0) Value = (int)((float)(e.Y - t.Height / 2) / track * Maximum);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging)
            {
                Rectangle t = Thumb();
                int track = Height - Theme.S(8) - t.Height;
                if (track > 0) Value = (int)((float)(e.Y - _grabOffset - Theme.S(4)) / track * Maximum);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            if (!Needed) return;

            Rectangle t = Thumb();
            Color c = _dragging ? Theme.TextDim : (_hot ? Theme.TextFaint : Theme.Border);
            using (GraphicsPath p = Theme.Rounded(t, t.Width / 2))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }
    }
}
