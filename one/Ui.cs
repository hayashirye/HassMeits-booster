// ============================================================================
//  无畏契约 CPU 高频优化器  ·  界面
//  自绘深色主题。所有控件都是双缓冲 + 抗锯齿手绘，不依赖任何第三方库。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Vcb
{
    // ────────────────────────────────────────────────────────────────────
    //  主题
    // ────────────────────────────────────────────────────────────────────
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(13, 15, 19);
        public static readonly Color Side = Color.FromArgb(18, 21, 27);
        public static readonly Color Card = Color.FromArgb(23, 26, 33);
        // 卡片表面做成上亮下暗的渐变，模拟光从上方来 —— 深色界面里这比投影管用，
        // 因为背景本身就接近纯黑，画了阴影也看不出来。
        public static readonly Color CardTop = Color.FromArgb(30, 34, 43);
        public static readonly Color CardBot = Color.FromArgb(20, 23, 29);
        public static readonly Color CardHi = Color.FromArgb(31, 35, 44);
        public static readonly Color Border = Color.FromArgb(38, 44, 56);
        public static readonly Color Text = Color.FromArgb(232, 235, 241);
        public static readonly Color Dim = Color.FromArgb(140, 149, 164);
        public static readonly Color Faint = Color.FromArgb(95, 103, 117);
        public static readonly Color Accent = Color.FromArgb(255, 70, 85);
        public static readonly Color AccentHi = Color.FromArgb(255, 100, 113);
        public static readonly Color Green = Color.FromArgb(52, 211, 153);
        public static readonly Color Yellow = Color.FromArgb(245, 176, 65);
        public static readonly Color Red = Color.FromArgb(239, 88, 88);
        public static readonly Color Blue = Color.FromArgb(96, 165, 250);

        private static string _family = null;
        public static string Family
        {
            get
            {
                if (_family != null) return _family;
                string[] want = new string[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "SimSun" };
                try
                {
                    using (InstalledFontCollection c = new InstalledFontCollection())
                    {
                        List<string> have = new List<string>();
                        foreach (FontFamily f in c.Families) have.Add(f.Name);
                        foreach (string w in want) if (have.Contains(w)) { _family = w; return _family; }
                    }
                }
                catch { }
                _family = "Arial";
                return _family;
            }
        }

        private static readonly Dictionary<string, Font> _cache = new Dictionary<string, Font>();
        public static Font F(float size, FontStyle style)
        {
            string k = size.ToString() + "|" + (int)style;
            Font f;
            if (_cache.TryGetValue(k, out f)) return f;
            try { f = new Font(Family, size, style); }
            catch { f = new Font(FontFamily.GenericSansSerif, size, style); }
            _cache[k] = f;
            return f;
        }
        // ★ 表格/日志这些文本框原来用 Consolas。Consolas 里没有汉字，
        //   中文会回退到宋体 —— 于是同一张表里英文是 Consolas、中文是宋体，
        //   跟界面别处的微软雅黑放在一起就是三种字体。现在统一走微软雅黑。
        //   代价：雅黑是比例字体（实测 i=32px、W=104px、汉字=56px），
        //   表格不能再靠「汉字算两格」对齐，所以下面的 PadW 改成按像素补空格。
        public static Font Mono(float size, FontStyle style)
        {
            return F(size, style);
        }
    }

    internal static class Rounded
    {
        public static GraphicsPath Path(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            if (radius < 1) { p.AddRectangle(r); return p; }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  补间动画
    //  WinForms 没有合成器，每一帧都是 GDI+ 在 UI 线程上重画 —— 这跟浏览器
    //  不一样。所以这里守两条规矩：
    //    ① 每帧只让「正在动的那一个控件」失效，绝不 Invalidate 整个窗体；
    //    ② 没有活着的补间就把定时器停掉，空闲时开销严格为零。
    //  另外跟着系统的「在 Windows 中显示动画」无障碍开关走（关掉就全部瞬变）。
    // ────────────────────────────────────────────────────────────────────
    internal static class Anim
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint act, uint p, out int v, uint ini);
        private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

        private const int FrameMs = 15;                 // ≈66 帧/秒
        private static System.Windows.Forms.Timer _timer;
        private static readonly List<Tween> _live = new List<Tween>();
        private static int _sys = -1;

        private class Tween
        {
            public object Owner;
            public float From, To;
            public int Ms, Elapsed;
            public Action<float> Frame;
            public Action Done;
            public Func<float, float> Ease;
            public bool Dead;
        }

        // 系统关掉了动画就跟着关。读一次就够，改设置要重开程序才生效。
        public static bool On
        {
            get
            {
                if (_sys < 0)
                {
                    _sys = 1;
                    try
                    {
                        int v;
                        if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out v, 0)) _sys = (v != 0) ? 1 : 0;
                    }
                    catch { }
                }
                return _sys == 1;
            }
        }

        public static float Out(float t) { float u = 1f - t; return 1f - u * u * u; }
        public static float InOut(float t)
        {
            return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3f) / 2f;
        }
        // 轻微过冲，用于「选中标记弹出来」这类需要一点手感的场合
        public static float Back(float t)
        {
            float c1 = 1.70158f, c3 = c1 + 1f, u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static void To(object owner, float from, float to, int ms, Action<float> frame)
        {
            To(owner, from, to, ms, frame, null, Out);
        }

        public static void To(object owner, float from, float to, int ms,
                              Action<float> frame, Action done, Func<float, float> ease)
        {
            Cancel(owner);
            // 动画被系统关掉、时长为零、或者本来就已经到位 —— 直接给终值，
            // 不留一个永远不动的补间在列表里空转。
            if (!On || ms <= 0 || Math.Abs(to - from) < 0.001f)
            {
                try { frame(to); } catch { }
                if (done != null) { try { done(); } catch { } }
                return;
            }
            Tween tw = new Tween();
            tw.Owner = owner;
            tw.From = from; tw.To = to; tw.Ms = ms;
            tw.Frame = frame; tw.Done = done;
            tw.Ease = ease == null ? (Func<float, float>)Out : ease;
            _live.Add(tw);
            EnsureTimer();
        }

        public static void Cancel(object owner)
        {
            for (int i = 0; i < _live.Count; i++) if (_live[i].Owner == owner) _live[i].Dead = true;
        }

        public static void CancelAll()
        {
            for (int i = 0; i < _live.Count; i++) _live[i].Dead = true;
        }

        public static bool Busy(object owner)
        {
            for (int i = 0; i < _live.Count; i++)
                if (!_live[i].Dead && _live[i].Owner == owner) return true;
            return false;
        }

        private static void EnsureTimer()
        {
            if (_timer == null)
            {
                _timer = new System.Windows.Forms.Timer();
                _timer.Interval = FrameMs;
                _timer.Tick += OnTick;
            }
            if (!_timer.Enabled) _timer.Start();
        }

        private static void OnTick(object s, EventArgs e)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Tween tw = _live[i];
                if (tw.Dead) { _live.RemoveAt(i); continue; }
                tw.Elapsed += FrameMs;
                float t = tw.Ms <= 0 ? 1f : (float)tw.Elapsed / tw.Ms;
                bool last = t >= 1f;
                if (last) t = 1f;
                try { tw.Frame(tw.From + (tw.To - tw.From) * tw.Ease(t)); }
                catch { tw.Dead = true; }
                if (last)
                {
                    _live.RemoveAt(i);
                    if (tw.Done != null) { try { tw.Done(); } catch { } }
                }
            }
            if (_live.Count == 0 && _timer != null) _timer.Stop();
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  深色文本框
    //  WinForms 的 TextBox 滚动条默认走浅色系统主题，在深色界面上会露出
    //  两条白边。挂上 DarkMode_Explorer 主题即可变深。SetWindowTheme 是
    //  非公开 API，但自 Win10 1809 起一直有效，失败也只是保持原样。
    // ────────────────────────────────────────────────────────────────────
    internal class DarkBox : TextBox
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr h, string app, string idlist);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetWindowTheme(Handle, "DarkMode_Explorer", null); }
            catch { }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  圆角卡片容器
    // ────────────────────────────────────────────────────────────────────
    internal class Card : Panel
    {
        public int Radius = 12;
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width, Height);
            using (SolidBrush b = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Bg))
                g.FillRectangle(b, r);
            Rectangle inner = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Rounded.Path(inner, Radius))
            {
                // ★ 深色界面里卡片「浮起来」靠的不是投影 —— 底色本来就是近黑，
                //   画了阴影也看不见。真正管用的是「表面比底色亮」+「上亮下暗」
                //   模拟光从上方照下来。渐变矩形高度 +1 是为了避开 GDI+ 最后
                //   一行取不到色标的毛病。
                using (LinearGradientBrush b = new LinearGradientBrush(
                           new Rectangle(inner.X, inner.Y, inner.Width, inner.Height + 1),
                           Theme.CardTop, Theme.CardBot, 90f))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Border)) g.DrawPath(pen, p);
            }
            // 顶边一道高光，卡片立刻有了厚度
            int x0 = inner.X + Radius, x1 = inner.Right - Radius;
            if (x1 > x0)
            {
                using (Pen pen = new Pen(Color.FromArgb(30, 255, 255, 255)))
                    g.DrawLine(pen, x0, inner.Y + 1, x1, inner.Y + 1);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  按钮
    // ────────────────────────────────────────────────────────────────────
    internal class Btn : Control
    {
        public bool Primary = false;
        private float _hotF, _downF;       // 悬停/按下的插值量（0→1），绘制只看它们
        // ★ Anim.To 会先取消同一个 owner 上所有在跑的补间。所以「一个控件」
        //   不能直接当 owner —— 悬停和按下会互相取消。每个属性一个专属钥匙。
        private readonly object _kHot = new object();
        private readonly object _kDown = new object();
        public Btn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = Theme.F(10.5f, FontStyle.Bold);
        }
        // 悬停/按下都走补间：颜色是插出来的，不是「啪」一下跳过去的。
        // 时长刻意压得短（按下 70ms、抬起 90ms、进出 110ms）—— 界面上的反馈
        // 一旦超过 150ms 就会显得迟钝，那不是「顺滑」而是「拖」。
        protected override void OnMouseEnter(EventArgs e)
        {
            Anim.To(_kHot, _hotF, 1f, 110, v => { _hotF = v; Invalidate(); });
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            Anim.To(_kHot, _hotF, 0f, 110, v => { _hotF = v; Invalidate(); });
            Anim.To(_kDown, _downF, 0f, 90, v => { _downF = v; Invalidate(); });
            base.OnMouseLeave(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Anim.To(_kDown, _downF, 1f, 70, v => { _downF = v; Invalidate(); });
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            Anim.To(_kDown, _downF, 0f, 90, v => { _downF = v; Invalidate(); });
            base.OnMouseUp(e);
        }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color fill, border, fg;
            bool grad = false;
            Color gTop = Color.Empty, gBot = Color.Empty;
            if (!Enabled) { fill = Theme.Card; border = Theme.Border; fg = Theme.Faint; }
            else if (Primary)
            {
                // 主按钮用渐变 + 亮边，比纯色红「重」得多，也更立体。
                // 三个色阶（常态 / 悬停 / 按下）之间按 _hotF、_downF 插值。
                grad = true;
                gTop = Anim.Mix(Anim.Mix(Color.FromArgb(246, 82, 96), Color.FromArgb(255, 96, 110), _hotF),
                                Color.FromArgb(196, 44, 54), _downF);
                gBot = Anim.Mix(Anim.Mix(Color.FromArgb(212, 44, 60), Color.FromArgb(226, 52, 68), _hotF),
                                Color.FromArgb(166, 32, 42), _downF);
                fill = gBot;
                border = Color.FromArgb(120, 255, 150, 160);
                fg = Color.White;
            }
            else
            {
                fill = Anim.Mix(Anim.Mix(Theme.Card, Color.FromArgb(34, 39, 48), _hotF),
                                Color.FromArgb(28, 32, 40), _downF);
                border = Anim.Mix(Theme.Border, Color.FromArgb(76, 84, 100), _hotF);
                fg = Anim.Mix(Theme.Dim, Theme.Text, _hotF);
            }

            // 按下时整体下沉 1px：不加这一下，光靠颜色变化总觉得按钮「没被按到」
            // ★ 和表格同一个坑：TextRenderer.DrawText 走 GDI，不认 GDI+ 的
            //   TranslateTransform。用变换的话只有底色下沉、字不动。
            //   所以把位移算进矩形里，两边才会一起走。
            int sink = (_downF > 0.5f) ? 1 : 0;
            Rectangle rb = new Rectangle(r.X, r.Y + sink, r.Width, r.Height);

            using (GraphicsPath p = Rounded.Path(rb, 9))
            {
                if (grad)
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(
                               new Rectangle(rb.X, rb.Y, rb.Width, rb.Height + 1), gTop, gBot, 90f))
                        g.FillPath(b, p);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                }
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
            // 顶边高光：按钮上沿「吃到光」，凸起来
            if (Enabled && rb.Width > 24)
            {
                using (Pen pen = new Pen(Color.FromArgb(Primary ? 60 : 22, 255, 255, 255)))
                    g.DrawLine(pen, rb.X + 9, rb.Y + 1, rb.Right - 9, rb.Y + 1);
            }
            TextRenderer.DrawText(g, Text, Font, rb, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  模式选择卡
    // ────────────────────────────────────────────────────────────────────
    internal class ModeCard : Control
    {
        public ModeProfile Mode;
        private bool _sel;
        public bool Selected
        {
            get { return _sel; }
            set
            {
                if (_sel == value) return;
                _sel = value;
                Anim.To(_kSel, _selF, value ? 1f : 0f, 200, v => { _selF = v; Invalidate(); });
                // 勾选标记单独走一条回弹曲线（0 → 约 1.09 → 1）：圆「咚」地
                // 弹出来，勾再跟着划上去。少了这一下，选中只是「换了个颜色」。
                if (value) Anim.To(_kPop, 0f, 1f, 300, v => { _popF = v; Invalidate(); }, null, Anim.Back);
                else Anim.To(_kPop, _popF, 0f, 140, v => { _popF = v; Invalidate(); });
            }
        }
        private float _selF, _hotF, _popF;      // 选中 / 悬停 / 勾选回弹 的插值量
        private readonly object _kSel = new object();
        private readonly object _kPop = new object();
        private readonly object _kHot = new object();
        public ModeCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e)
        {
            Anim.To(_kHot, _hotF, 1f, 130, v => { _hotF = v; Invalidate(); });
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            Anim.To(_kHot, _hotF, 0f, 130, v => { _hotF = v; Invalidate(); });
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            // 三个状态（常态 / 悬停 / 选中）之间一律插值，不再用 if 跳变。
            // 顺序很重要：先悬停再选中，这样从「悬停者」变成「选中者」是连续的。
            Color fillTop = Anim.Mix(Anim.Mix(Color.FromArgb(26, 30, 37), Color.FromArgb(32, 37, 46), _hotF),
                                     Color.FromArgb(58, 30, 35), _selF);
            Color fillBot = Anim.Mix(Anim.Mix(Color.FromArgb(18, 21, 26), Color.FromArgb(24, 27, 34), _hotF),
                                     Color.FromArgb(38, 22, 26), _selF);
            Color border = Anim.Mix(Anim.Mix(Theme.Border, Color.FromArgb(72, 80, 96), _hotF),
                                    Theme.Accent, _selF);

            using (GraphicsPath p = Rounded.Path(r, 10))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(
                           new Rectangle(r.X, r.Y, r.Width, r.Height + 1), fillTop, fillBot, 90f))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(border, 1f + 0.6f * _selF)) g.DrawPath(pen, p);
                if (_selF > 0.001f)
                {
                    using (Pen pen = new Pen(Color.FromArgb((int)(70 * _selF), 255, 255, 255)))
                        g.DrawLine(pen, r.X + 10, r.Y + 1, r.Right - 10, r.Y + 1);
                }
            }

            // 右上角的选中标记：实心圆 + 白勾，比一个光点更像「已选」。
            // 半径跟着 _popF 长，超过 1 就是回弹的过冲。
            if (_popF > 0.001f)
            {
                float s = _popF;
                const int d = 13;
                float cx = r.Right - 24 + d / 2f, cy = 13 + d / 2f;
                float rad = d / 2f * s;
                int alpha = (int)(255 * (s > 1f ? 1f : s));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, Theme.Accent)))
                    g.FillEllipse(b, cx - rad, cy - rad, rad * 2, rad * 2);
                if (s > 0.55f)      // 等圆长到差不多大了再划勾
                {
                    float k = (s - 0.55f) / 0.45f;
                    if (k > 1f) k = 1f;
                    using (Pen pen = new Pen(Color.FromArgb((int)(255 * k), Color.White), 1.7f))
                    {
                        float x0 = cx - rad * 0.42f, y0 = cy + rad * 0.06f;
                        float x1 = cx - rad * 0.08f, y1 = cy + rad * 0.42f;
                        float x2 = cx + rad * 0.48f, y2 = cy - rad * 0.34f;
                        g.DrawLine(pen, x0, y0, x1, y1);
                        g.DrawLine(pen, x1, y1, x2, y2);
                    }
                }
            }

            int pad = 14;
            Rectangle tr = new Rectangle(pad, pad, r.Width - pad * 2, 24);
            TextRenderer.DrawText(g, Mode.Name, Theme.F(12f, FontStyle.Bold), tr, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            Rectangle dr = new Rectangle(pad, pad + 26, r.Width - pad * 2, r.Height - pad - 30);
            TextRenderer.DrawText(g, Mode.Desc, Theme.F(8.5f, FontStyle.Regular), dr,
                Anim.Mix(Theme.Dim, Color.FromArgb(200, 205, 214), _selF),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  左侧导航项
    // ────────────────────────────────────────────────────────────────────
    internal class NavBtn : Control
    {
        private bool _sel;
        // 选中不是「跳」过去的：药丸底色、左侧强调条、文字颜色共用同一个
        // 插值量，所以它们是一起长出来的，而不是各变各的。
        public bool Selected
        {
            get { return _sel; }
            set
            {
                if (_sel == value) return;
                _sel = value;
                Anim.To(_kSel, _selF, value ? 1f : 0f, 180, v => { _selF = v; Invalidate(); });
            }
        }
        public string Glyph = "";
        private float _selF, _hotF;             // 选中 / 悬停 的插值量
        private readonly object _kSel = new object();
        private readonly object _kHot = new object();
        public NavBtn()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Height = 42;
        }
        protected override void OnMouseEnter(EventArgs e)
        {
            Anim.To(_kHot, _hotF, 1f, 120, v => { _hotF = v; Invalidate(); });
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            Anim.To(_kHot, _hotF, 0f, 120, v => { _hotF = v; Invalidate(); });
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            float pill = _selF > _hotF ? _selF : _hotF;      // 谁亮用谁
            if (pill > 0.001f)
            {
                using (GraphicsPath p = Rounded.Path(r, 8))
                {
                    if (_selF > 0.001f)
                    {
                        // 悬停底色 (28,32,40) 与选中底色（红调）之间插值，
                        // 于是「鼠标扫过」和「被选中」是同一套视觉语言。
                        Color top = Anim.Mix(Color.FromArgb(28, 32, 40), Color.FromArgb(52, 28, 33), _selF);
                        Color bot = Anim.Mix(Color.FromArgb(28, 32, 40), Color.FromArgb(34, 21, 25), _selF);
                        Color solid = Anim.Mix(Color.FromArgb(28, 32, 40), Color.FromArgb(30, 24, 28), _selF);
                        int a = (int)(255 * pill);
                        if (a > 255) a = 255;
                        using (LinearGradientBrush b = new LinearGradientBrush(
                                   new Rectangle(r.X, r.Y, r.Width, r.Height + 1),
                                   Color.FromArgb(a, top), Color.FromArgb(a, bot), 90f))
                            g.FillPath(b, p);
                        if (_selF < 0.999f)
                            using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * (1f - _selF) * _hotF), solid)))
                                g.FillPath(b, p);
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * _hotF), 28, 32, 40)))
                            g.FillPath(b, p);
                    }
                }
            }
            // 左侧强调条：上下留白 + 圆角，比一根方的竖线精致。
            // 高度和透明度都跟着 _selF 长出来 —— 这是整条导航里最抓眼的一下。
            if (_selF > 0.001f)
            {
                int full = Height - 21;
                int h = (int)Math.Round(full * _selF);
                int off = (Height - h) / 2;
                if (h >= 2)
                {
                    using (GraphicsPath p = Rounded.Path(new Rectangle(0, off, 3, h), 2))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * _selF), Theme.Accent)))
                        g.FillPath(b, p);
                }
            }

            Rectangle gr = new Rectangle(16, 0, 26, Height);
            TextRenderer.DrawText(g, Glyph, Theme.F(11f, FontStyle.Regular), gr,
                Anim.Mix(Anim.Mix(Theme.Faint, Theme.Dim, _hotF), Theme.Accent, _selF),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            // 字重不能插值，等过半再换 —— 太快换会像闪了一下
            bool bold = _selF > 0.5f;
            Rectangle tr = new Rectangle(46, 0, Width - 54, Height);
            TextRenderer.DrawText(g, Text, Theme.F(10f, bold ? FontStyle.Bold : FontStyle.Regular), tr,
                Anim.Mix(Anim.Mix(Theme.Dim, Theme.Text, _hotF), Theme.Text, _selF),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  自绘表格
    //  以前三处表格都是把文本拼好塞进 TextBox —— 那看起来像调试输出，不像
    //  产品。这里改成自己画：表头吸顶、隔行底色、数字右对齐、判定列画成
    //  彩色药丸、占比画成比例条。页面只管往 Rows 里填数据。
    //
    //  行的配色（Kinds）约定：0 绿 / 1 红 / 2 灰 / 3 琥珀
    // ────────────────────────────────────────────────────────────────────
    internal class Table : Panel
    {
        public string[] Head = new string[0];
        public int[] W = new int[0];          // 列宽；<=0 的列平分剩余宽度
        public bool[] Align = new bool[0];    // 该列右对齐（true = 右对齐）
        public int BadgeCol = -1;             // 这一列画成彩色药丸
        public int BarCol = -1;               // 这一列画成比例条（内容是百分比数字）
        public int RowH = 30;
        public int HeadH = 34;

        public readonly List<string[]> Rows = new List<string[]>();
        public readonly List<int> Kinds = new List<int>();

        private int _scroll;
        private bool _dragBar;
        private int _dragY, _dragScroll;
        private ToolTip _tip;
        private int _tipRow = -1;
        // 比例条与药丸的「生长」进度：0 → 1。刚采完样时让数字自己长出来，
        // 比一次性拍到屏幕上更容易看出哪根条更长。
        private float _barF = 1f;
        private readonly object _kBar = new object();
        // 鼠标所在行的高亮，淡入淡出而不是整行突然变色
        private int _hoverRow = -1;
        private float _hoverF;
        private readonly object _kHover = new object();

        public Table()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            // ★ 不用 Panel 自带的 AutoScroll：列宽是按客户区算的，
            //   而竖滚动条一出现客户区就变窄 —— 排完版再变窄就多出一根横滚动条，
            //   而且系统滚动条是浅灰的，在深色界面上很扎眼。自己画。
            AutoScroll = false;
        }

        public void Clear() { Rows.Clear(); Kinds.Clear(); _scroll = 0; }
        public void Add(int kind, params string[] cells) { Rows.Add(cells); Kinds.Add(kind); }

        private int ContentH { get { return HeadH + RowH * Rows.Count + 6; } }
        private int MaxScroll { get { int m = ContentH - Height; return m < 0 ? 0 : m; } }
        // 有滚动条时右边让出 10px，避免文字压在滚动条底下
        private int UsableW { get { return ClientSize.Width - (ContentH > Height ? 10 : 0); } }

        // 采完样调这个：数字不是「啪」一下全出现的，比例条和药丸从 0 长到
        // 实际大小，眼睛才有机会比较哪根条更长。460ms 是试出来的 —— 再短
        // 就只是闪一下，再长会跟「点完按钮等结果」的节奏打架。
        public void Finish()
        {
            Anim.To(_kBar, 0f, 1f, 460, v => { _barF = v; Invalidate(); });
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (MaxScroll > 0)
            {
                _scroll -= e.Delta / 120 * RowH * 3;
                if (_scroll < 0) _scroll = 0;
                if (_scroll > MaxScroll) _scroll = MaxScroll;
                Invalidate();
            }
            base.OnMouseWheel(e);
        }
        private int TrackY { get { return HeadH; } }
        private int TrackH { get { return Math.Max(1, Height - HeadH - 4); } }
        private int ThumbH { get { return Math.Max(28, TrackH * Height / Math.Max(1, ContentH)); } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (MaxScroll > 0 && e.X >= Width - 12 && e.Y >= TrackY)
            {
                _dragBar = true; _dragY = e.Y; _dragScroll = _scroll;
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragBar)
            {
                int span = TrackH - ThumbH;
                if (span > 0)
                {
                    _scroll = _dragScroll + (e.Y - _dragY) * MaxScroll / span;
                    if (_scroll < 0) _scroll = 0;
                    if (_scroll > MaxScroll) _scroll = MaxScroll;
                    Invalidate();
                }
                base.OnMouseMove(e);
                return;
            }
            // 鼠标在哪一行（表头区、滚动条上、或行号越界都算「不在任何行上」）
            int row = -1;
            if (e.X < Width - 12 && e.Y >= HeadH && RowH > 0)
            {
                row = (e.Y - HeadH + _scroll) / RowH;
                if (row < 0 || row >= Rows.Count) row = -1;
            }
            // 行高亮跟着鼠标淡入淡出。只在「换了行」时才起补间 —— 每移动一个
            // 像素都 Cancel 再重开的话，补间永远停在起点，看起来就是没反应。
            if (row != _hoverRow)
            {
                if (row >= 0)
                {
                    _hoverRow = row;
                    Anim.To(_kHover, 0f, 1f, 120, v => { _hoverF = v; Invalidate(); });
                }
                else
                {
                    // 移出所有行时先把高亮淡掉再撤行号；立刻置 -1 会「啪」地消失。
                    // 淡出途中又移回某一行的话，上面那次 To 会把这条取消掉，
                    // done 就不会执行（Anim 里 Dead 的补间直接丢弃，不回调）。
                    Anim.To(_kHover, _hoverF, 0f, 120, v => { _hoverF = v; Invalidate(); },
                            delegate { _hoverRow = -1; Invalidate(); }, null);
                }
            }
            // 窄列里的长文本会被省略号截掉，鼠标停在哪一行就把整行原文弹出来
            if (row != _tipRow)
            {
                _tipRow = row;
                if (_tip != null) _tip.Hide(this);
                if (row >= 0)
                {
                    string[] c = Rows[row];
                    StringBuilder t = new StringBuilder();
                    for (int i = 0; i < Head.Length && i < c.Length; i++)
                    {
                        if (c[i] == null || c[i].Length == 0) continue;
                        if (t.Length > 0) t.Append("\r\n");
                        if (Head[i].Length > 0) t.Append(Head[i]).Append("：");
                        t.Append(c[i]);
                    }
                    if (t.Length > 0)
                    {
                        if (_tip == null) _tip = new ToolTip();
                        _tip.Show(t.ToString(), this, e.X + 16, e.Y + 20, 20000);
                    }
                }
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            _tipRow = -1;
            if (_tip != null) _tip.Hide(this);
            base.OnMouseLeave(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { _dragBar = false; Invalidate(); base.OnMouseUp(e); }

        // 细长圆角滚动条，配色跟着深色主题走
        private void DrawScroll(Graphics g)
        {
            if (MaxScroll <= 0) return;
            int x = Width - 7;
            Rectangle track = new Rectangle(x, TrackY, 4, TrackH);
            using (GraphicsPath p = Rounded.Path(track, 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 35, 44)))
                g.FillPath(b, p);
            int th = ThumbH;
            int ty = TrackY + (TrackH - th) * _scroll / MaxScroll;
            Rectangle thumb = new Rectangle(x, ty, 4, th);
            using (GraphicsPath p = Rounded.Path(thumb, 2))
            using (SolidBrush b = new SolidBrush(_dragBar ? Color.FromArgb(126, 136, 154) : Color.FromArgb(76, 84, 100)))
                g.FillPath(b, p);
        }

        private int[] ColX()
        {
            int n = Head.Length;
            int[] xs = new int[n + 1];
            if (n == 0) return xs;
            int pad = 16, fixedW = 0, flexCnt = 0;
            for (int i = 0; i < n; i++) { if (W.Length > i && W[i] > 0) fixedW += W[i]; else flexCnt++; }
            int avail = UsableW - pad * 2;
            int flexW = flexCnt > 0 ? Math.Max(40, (avail - fixedW) / flexCnt) : 0;
            int x = pad;
            for (int i = 0; i < n; i++)
            {
                xs[i] = x;
                x += (W.Length > i && W[i] > 0) ? W[i] : flexW;
            }
            xs[n] = x;
            return xs;
        }

        private int KindOf(int r) { return r < Kinds.Count ? Kinds[r] : 2; }

        private static Color BadgeBg(int k)
        {
            if (k == 0) return Color.FromArgb(26, 66, 47);
            if (k == 1) return Color.FromArgb(72, 28, 33);
            if (k == 3) return Color.FromArgb(70, 56, 22);
            return Color.FromArgb(36, 40, 48);
        }
        private static Color BadgeFg(int k)
        {
            if (k == 0) return Color.FromArgb(104, 222, 156);
            if (k == 1) return Color.FromArgb(255, 128, 138);
            if (k == 3) return Color.FromArgb(242, 196, 96);
            return Color.FromArgb(140, 148, 162);
        }
        private static Color BarHi(int k)
        {
            if (k == 0) return Color.FromArgb(112, 222, 158);
            if (k == 1) return Color.FromArgb(255, 116, 126);
            if (k == 3) return Color.FromArgb(246, 194, 92);
            return Color.FromArgb(128, 140, 158);
        }
        private static Color BarLo(int k)
        {
            if (k == 0) return Color.FromArgb(44, 158, 100);
            if (k == 1) return Color.FromArgb(210, 46, 62);
            if (k == 3) return Color.FromArgb(206, 142, 34);
            return Color.FromArgb(84, 94, 110);
        }

        private void DrawBadge(Graphics g, Rectangle cr, string text, int kind)
        {
            if (string.IsNullOrEmpty(text)) return;
            Font f = Theme.F(8.5f, FontStyle.Bold);
            Size sz = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding);
            int w = sz.Width + 20, h = 20;
            if (w > cr.Width - 4) w = cr.Width - 4;
            Rectangle br = new Rectangle(cr.X + (cr.Width - w) / 2, cr.Y + (cr.Height - h) / 2, w, h);
            using (GraphicsPath p = Rounded.Path(br, h / 2))
            {
                using (SolidBrush b = new SolidBrush(BadgeBg(kind))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(64, BadgeFg(kind)))) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, text, f, br, BadgeFg(kind),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private void DrawBar(Graphics g, Rectangle cr, string text, int kind, float prog)
        {
            int bw = 54, bh = 6;
            Rectangle track = new Rectangle(cr.X, cr.Y + (cr.Height - bh) / 2, bw, bh);
            using (GraphicsPath p = Rounded.Path(track, 3))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(32, 37, 46)))
                g.FillPath(b, p);

            double pct;
            double.TryParse(text, out pct);
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            int fw = (int)Math.Round(bw * pct / 100.0);
            if (fw > 0)
            {
                if (fw < 6) fw = 6;
                fw = (int)Math.Round(fw * prog);     // 条从 0 长到实际长度
                if (fw < 1) fw = 1;
                Rectangle fr = new Rectangle(track.X, track.Y, fw, bh);
                using (GraphicsPath p = Rounded.Path(fr, 3))
                using (LinearGradientBrush b = new LinearGradientBrush(
                           new Rectangle(fr.X, fr.Y, Math.Max(fr.Width, 1), fr.Height + 1),
                           BarHi(kind), BarLo(kind), 0f))
                    g.FillPath(b, p);
            }
            Rectangle tr = new Rectangle(cr.X + bw + 10, cr.Y, cr.Width - bw - 10, cr.Height);
            TextRenderer.DrawText(g, text, Theme.F(9.2f, FontStyle.Bold), tr, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);          // 透明底 → 让卡片渐变透上来
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int n = Head.Length;
            int[] xs = ColX();

            // ── 数据行 ──
            // ★★ TextRenderer.DrawText 走的是 GDI，不认 GDI+ 的 TranslateTransform
            //    和 SetClip。而这里的背景、药丸、比例条全走 GDI+ —— 一旦用变换来
            //    滚动或做滑入，就成了「背景动了、字没动」。
            //    所以偏移量一律算进矩形里，GDI 和 GDI+ 才会一起走。
            // 每行错开一点出场。错开量按行数摊：总错开占前一半时间，剩下的
            // 留给每行自己跑完 —— 这样无论 1 行还是 30 行，最后一行都恰好在
            // _barF = 1 时归位（_barF 静止在 1f 时，rp 会被钳到 1，不产生动画）。
            int rc = Rows.Count;
            float stagger = rc > 1 ? 0.5f / rc : 0f;
            float span = 1f - stagger * (rc - 1);
            if (span < 0.05f) span = 0.05f;
            for (int r = 0; r < Rows.Count; r++)
            {
                float rp = (_barF - r * stagger) / span;
                if (rp <= 0f) rp = 0f;
                if (rp >= 1f) rp = 1f;
                float rpE = Anim.Out(rp);

                int band = HeadH + r * RowH - _scroll;        // 这一行该在的位置
                if (band + RowH < 0 || band > Height + RowH) continue;   // 看不见就不画
                int y = band;
                if (rpE < 0.999f) y += (int)Math.Round((1f - rpE) * 9f); // 出场时从下方 9px 滑上来

                string[] row = Rows[r];
                int kd = KindOf(r);
                if (r % 2 == 1)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(18, 255, 255, 255)))
                        g.FillRectangle(b, 0, y, Width, RowH);
                }
                // 鼠标所在行加一层高亮
                if (r == _hoverRow && _hoverF > 0.001f)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(16 * _hoverF), 255, 255, 255)))
                        g.FillRectangle(b, 0, y, Width, RowH);
                }
                for (int i = 0; i < n && i < row.Length; i++)
                {
                    Rectangle cr = new Rectangle(xs[i], y, xs[i + 1] - xs[i], RowH);
                    if (i == BadgeCol) { DrawBadge(g, cr, row[i], kd); continue; }
                    if (i == BarCol) { DrawBar(g, cr, row[i], kd, rpE); continue; }
                    TextRenderer.DrawText(g, row[i], Theme.F(i == 0 ? 9.3f : 9.2f, FontStyle.Regular), cr,
                        i == 0 ? Theme.Text : (i == n - 1 ? Theme.Dim : Color.FromArgb(196, 203, 214)),
                        (Align.Length > i && Align[i] ? TextFormatFlags.Right : TextFormatFlags.Left)
                        | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                }
            }

            // ── 表头：吸顶，最后画，盖住滚过去的行 ──
            if (n > 0)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(17, 20, 25)))
                    g.FillRectangle(b, 0, 0, Width, HeadH);
                for (int i = 0; i < n; i++)
                {
                    Rectangle cr = new Rectangle(xs[i], 0, xs[i + 1] - xs[i], HeadH);
                    // 药丸列的表头居中对齐，否则「判定」会紧贴在前一列的数字后面
                    TextFormatFlags tf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
                    if (i == BadgeCol) tf |= TextFormatFlags.HorizontalCenter;
                    else if (Align.Length > i && Align[i]) tf |= TextFormatFlags.Right;
                    else tf |= TextFormatFlags.Left;
                    TextRenderer.DrawText(g, Head[i], Theme.F(8.7f, FontStyle.Bold), cr, Theme.Faint, tf);
                }
                using (Pen pen = new Pen(Theme.Border))
                    g.DrawLine(pen, 8, HeadH - 1, Width - 8, HeadH - 1);
            }

            DrawScroll(g);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  主窗口
    // ────────────────────────────────────────────────────────────────────
    internal class MainForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        private Panel _side, _content;
        private NavBtn[] _nav = new NavBtn[5];
        private Panel[] _pages = new Panel[5];
        private int _page = 0;

        private Label _stTitle, _stBig, _stSub, _stScheme;
        private Card _statusCard, _modeCard, _outCard, _benchCard, _logCard, _checkCard, _benchNote;
        private ModeCard[] _mc = new ModeCard[4];
        private Btn _btnApply, _btnRestore, _btnBench, _checkBtn, _logRefresh, _logOpen;
        private TextBox _out, _log;
        private Label _checkInfo, _checkNote, _bgNote;
        private Table _powTbl, _bgTbl;
        private Label _benchBig, _benchSub, _benchDetail;
        private Label _adminBadge;
        private CpuInfo _cpu;
        private string _modeKey = "competitive";

        // ── 多游戏支持 ────────────────────────────────────────────────
        private List<GameProfile> _games;
        private string _gameKey = "valorant";
        private Label _gameTitle, _gameSub;
        private Btn[] _gameTab = new Btn[2];
        private Card _gameCard, _cfgCard, _guardCard;
        private Label _gExe, _gPref, _gAdvice;
        private Btn _btnGpuPref, _btnCfgOpen, _btnGameRefresh, _btnGuard;
        private Label _guardState, _guardNote;
        private bool _guardOn;
        private Label _pinState;
        private Btn _btnPin;
        private bool _pinOn;
        private Label _cfgInfo, _cfgNote;
        private ToolTip _cfgTip;
        private Table _cfgTbl;

        public MainForm()
        {
            Text = "游戏 CPU 高频优化器";
            ClientSize = new Size(1020, 700);
            MinimumSize = new Size(940, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(9.5f, FontStyle.Regular);
            DoubleBuffered = true;

            // 表格对齐要量真实像素宽度。字体只影响「几格」的换算比例，
            // 不影响补几个空格（字号变大时内容和目标一起变大），所以一个字体够用。
            TextPad.UiFont = Theme.F(9f, FontStyle.Regular);

            _cpu = CpuInfo.Detect();
            _games = Games.Build();

            BuildSide();
            BuildPages();
            BuildStatus();

            Resize += delegate { DoLayout(); };
            Load += delegate
            {
                DoLayout();
                DarkTitleBar();
                ApplyGameChrome();
                ShowPage(0);
                RunCheck(false);
            };
        }

        private void DarkTitleBar()
        {
            try
            {
                int v = 1;
                DwmSetWindowAttribute(Handle, 20, ref v, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
                DwmSetWindowAttribute(Handle, 19, ref v, sizeof(int)); // 旧版编号，Win10 1809 用
            }
            catch { }
        }

        // ── 左侧栏 ───────────────────────────────────────────────────
        private void BuildSide()
        {
            _side = new Panel();
            _side.BackColor = Theme.Side;
            _side.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, _side.Width - 1, 0, _side.Width - 1, _side.Height);
            };
            Controls.Add(_side);

            _gameTitle = new Label();
            _gameTitle.Font = Theme.F(13f, FontStyle.Bold);
            _gameTitle.ForeColor = Theme.Text;
            _gameTitle.BackColor = Color.Transparent;
            _gameTitle.AutoSize = true;
            _gameTitle.Location = new Point(20, 22);
            _side.Controls.Add(_gameTitle);

            _gameSub = new Label();
            _gameSub.Font = Theme.F(8.5f, FontStyle.Regular);
            _gameSub.ForeColor = Theme.Faint;
            _gameSub.BackColor = Color.Transparent;
            _gameSub.AutoSize = true;
            _gameSub.Location = new Point(21, 48);
            _side.Controls.Add(_gameSub);

            // 游戏切换：两个并列的小按钮，选中的那个是红底
            for (int i = 0; i < 2; i++)
            {
                Btn b = new Btn();
                b.Text = _games[i].Tab.Length > 0 ? _games[i].Tab : _games[i].Name;
                b.Tag = _games[i].Key;
                b.Click += delegate(object s, EventArgs e) { SwitchGame((string)((Btn)s).Tag); };
                _side.Controls.Add(b);
                _gameTab[i] = b;
            }

            string[] names = new string[] { "主页", "体检", "性能测试", "游戏专项", "日志" };
            string[] glyphs = new string[] { "◆", "◈", "◉", "▣", "≡" };
            for (int i = 0; i < 5; i++)
            {
                NavBtn b = new NavBtn();
                b.Text = names[i];
                b.Glyph = glyphs[i];
                b.Width = 176;
                b.Tag = i;
                b.Click += delegate(object s, EventArgs e) { ShowPage((int)((NavBtn)s).Tag); };
                _side.Controls.Add(b);
                _nav[i] = b;
            }

            _adminBadge = new Label();
            _adminBadge.Font = Theme.F(8f, FontStyle.Regular);
            _adminBadge.BackColor = Color.Transparent;
            _adminBadge.AutoSize = false;
            _adminBadge.TextAlign = ContentAlignment.MiddleLeft;
            _side.Controls.Add(_adminBadge);

            Label cpu = new Label();
            cpu.Text = _cpu.Name;
            cpu.Font = Theme.F(7.8f, FontStyle.Regular);
            cpu.ForeColor = Theme.Faint;
            cpu.BackColor = Color.Transparent;
            cpu.AutoSize = false;
            cpu.Name = "cpuLabel";
            _side.Controls.Add(cpu);
        }

        // ── 四个页面 ─────────────────────────────────────────────────
        private void BuildPages()
        {
            _content = new Panel();
            _content.BackColor = Theme.Bg;
            Controls.Add(_content);

            for (int i = 0; i < 5; i++)
            {
                Panel p = new Panel();
                p.BackColor = Theme.Bg;
                p.Visible = false;
                _content.Controls.Add(p);
                _pages[i] = p;
            }

            BuildDash(_pages[0]);
            BuildCheck(_pages[1]);
            BuildBench(_pages[2]);
            BuildGame(_pages[3]);
            BuildLog(_pages[4]);
        }

        private void BuildDash(Panel host)
        {
            _statusCard = new Card();
            host.Controls.Add(_statusCard);

            _stTitle = new Label();
            _stTitle.Text = "优化状态";
            _stTitle.Font = Theme.F(8.5f, FontStyle.Regular);
            _stTitle.ForeColor = Theme.Faint;
            _stTitle.BackColor = Theme.Card;
            _stTitle.AutoSize = true;
            _statusCard.Controls.Add(_stTitle);

            _stBig = new Label();
            _stBig.Text = "正在检测…";
            _stBig.Font = Theme.F(21f, FontStyle.Bold);
            _stBig.ForeColor = Theme.Dim;
            _stBig.BackColor = Theme.Card;
            _stBig.AutoSize = true;
            _statusCard.Controls.Add(_stBig);

            _stSub = new Label();
            _stSub.Text = "";
            _stSub.Font = Theme.F(9f, FontStyle.Regular);
            _stSub.ForeColor = Theme.Dim;
            _stSub.BackColor = Theme.Card;
            _stSub.AutoSize = true;
            _statusCard.Controls.Add(_stSub);

            _stScheme = new Label();
            _stScheme.Text = "";
            _stScheme.Font = Theme.Mono(8f, FontStyle.Regular);
            _stScheme.ForeColor = Theme.Faint;
            _stScheme.BackColor = Theme.Card;
            _stScheme.AutoSize = true;
            _statusCard.Controls.Add(_stScheme);

            _modeCard = new Card();
            host.Controls.Add(_modeCard);

            List<ModeProfile> modes = Modes.Build();
            for (int i = 0; i < modes.Count && i < 4; i++)
            {
                ModeCard m = new ModeCard();
                m.Mode = modes[i];
                m.Tag = modes[i].Key;
                m.Click += delegate(object s, EventArgs e)
                {
                    _modeKey = (string)((ModeCard)s).Tag;
                    RefreshModeSelection();
                    UpdateStatusLine();
                };
                _modeCard.Controls.Add(m);
                _mc[i] = m;
            }
            RefreshModeSelection();

            _btnApply = new Btn();
            _btnApply.Text = "一键优化";
            _btnApply.Primary = true;
            _btnApply.Click += delegate { DoApply(); };
            host.Controls.Add(_btnApply);

            _btnRestore = new Btn();
            _btnRestore.Text = "还原原始设置";
            _btnRestore.Click += delegate { DoRestore(); };
            host.Controls.Add(_btnRestore);

            _outCard = new Card();
            host.Controls.Add(_outCard);

            _out = new DarkBox();
            _out.Multiline = true;
            _out.ReadOnly = true;
            _out.ScrollBars = ScrollBars.Vertical;
            _out.BorderStyle = BorderStyle.None;
            _out.BackColor = Theme.Card;
            _out.ForeColor = Theme.Text;
            _out.Font = Theme.Mono(8.5f, FontStyle.Regular);
            _out.WordWrap = false;
            _out.Text = "还没有操作。\r\n\r\n先在上面选一个模式，然后点「一键优化」。\r\n结果会显示在这里。";
            _outCard.Controls.Add(_out);
        }

        private void RefreshModeSelection()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_mc[i] == null) continue;
                _mc[i].Selected = (_mc[i].Mode.Key == _modeKey);
                _mc[i].Invalidate();
            }
        }

        private void BuildCheck(Panel host)
        {
            _checkCard = new Card();
            host.Controls.Add(_checkCard);

            _checkInfo = new Label();
            _checkInfo.AutoSize = false;
            _checkInfo.BackColor = Color.Transparent;
            _checkInfo.ForeColor = Theme.Dim;
            _checkInfo.Font = Theme.F(9.4f, FontStyle.Regular);
            _checkCard.Controls.Add(_checkInfo);

            _powTbl = new Table();
            _powTbl.Head = new string[] { "设置项", "交流 AC", "电池 DC", "判定" };
            _powTbl.W = new int[] { 0, 80, 80, 92 };
            _powTbl.Align = new bool[] { false, true, true, false };
            _powTbl.BadgeCol = 3;
            _powTbl.RowH = 25;
            _powTbl.HeadH = 28;
            _checkCard.Controls.Add(_powTbl);

            _checkNote = new Label();
            _checkNote.AutoSize = false;
            _checkNote.BackColor = Color.Transparent;
            _checkNote.ForeColor = Theme.Text;
            _checkNote.Font = Theme.F(9.4f, FontStyle.Regular);
            _checkCard.Controls.Add(_checkNote);

            _bgTbl = new Table();
            _bgTbl.Head = new string[] { "占单核", "进程", "说明" };
            _bgTbl.W = new int[] { 118, 196, 0 };
            _bgTbl.Align = new bool[] { false, false, false };
            _bgTbl.BarCol = 0;
            _bgTbl.RowH = 26;
            _bgTbl.HeadH = 30;
            _checkCard.Controls.Add(_bgTbl);

            _bgNote = new Label();
            _bgNote.AutoSize = false;
            _bgNote.BackColor = Color.Transparent;
            _bgNote.ForeColor = Theme.Dim;
            _bgNote.Font = Theme.F(9.2f, FontStyle.Regular);
            _checkCard.Controls.Add(_bgNote);

            _checkBtn = new Btn();
            _checkBtn.Text = "重新检测";
            _checkBtn.Click += delegate { RunCheck(true); };
            host.Controls.Add(_checkBtn);
        }

        // 把 check 的文本切成表格行。格式由 Core.cs 的 Report 决定：名字占 28 格，
        // 交流/电池各 8 字符，最后是判定。名字里可能带空格（「能效偏好 EPP」），
        // 所以从右边数三个字段，剩下的全算名字。
        private static void FillPowerTable(Table t, string text, out string info, out string note)
        {
            t.Clear();
            info = ""; note = "";
            string schemeLine = "", guidLine = "", summary = "", verdict = "";
            bool body = false;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string ln = raw.TrimEnd();
                if (ln.Length == 0) continue;
                if (ln.StartsWith("---")) { body = !body; continue; }
                if (body)
                {
                    string[] p = ln.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 4) continue;
                    int n = p.Length;
                    string v = p[n - 1];
                    // 0 绿 符合 / 1 红 不符合 / 3 琥珀 参考 / 2 灰 本机无此项
                    int kind = v == "符合" ? 0 : (v.IndexOf("不符合") >= 0 ? 1 : (v.IndexOf("参考") >= 0 ? 3 : 2));
                    t.Add(kind, string.Join(" ", p, 0, n - 3), p[n - 3], p[n - 2], v);
                    continue;
                }
                if (ln.StartsWith("当前电源方案：")) schemeLine = ln;
                else if (ln.StartsWith("方案 GUID：")) guidLine = ln;
                else if (ln.StartsWith("符合 ") && ln.IndexOf(" 项") > 0) summary = ln;
                else if (ln.IndexOf("优化已生效") >= 0 || ln.StartsWith("有 ")) verdict = ln;
            }
            t.Finish();
            info = schemeLine + "　·　" + (guidLine.Length > 10 ? guidLine.Substring(10) : guidLine);
            note = summary + (verdict.Length > 0 ? "\r\n" + verdict : "");
        }

        private void BuildBench(Panel host)
        {
            _benchCard = new Card();
            host.Controls.Add(_benchCard);

            _benchBig = new Label();
            _benchBig.Text = "还没测过";
            _benchBig.Font = Theme.F(20f, FontStyle.Bold);
            _benchBig.ForeColor = Theme.Dim;
            _benchBig.BackColor = Theme.Card;
            _benchBig.AutoSize = true;
            _benchCard.Controls.Add(_benchBig);

            _benchSub = new Label();
            _benchSub.Text = "点下面的按钮，实测这颗 CPU 到底能出多少力。";
            _benchSub.Font = Theme.F(9f, FontStyle.Regular);
            _benchSub.ForeColor = Theme.Dim;
            _benchSub.BackColor = Theme.Card;
            _benchSub.AutoSize = true;
            _benchCard.Controls.Add(_benchSub);

            _benchDetail = new Label();
            _benchDetail.Text = "";
            _benchDetail.Font = Theme.Mono(8.5f, FontStyle.Regular);
            _benchDetail.ForeColor = Theme.Faint;
            _benchDetail.BackColor = Theme.Card;
            _benchDetail.AutoSize = true;
            _benchCard.Controls.Add(_benchDetail);

            _btnBench = new Btn();
            _btnBench.Text = "开始性能测试（约 8 秒）";
            _btnBench.Primary = true;
            _btnBench.Click += delegate { DoBench(); };
            host.Controls.Add(_btnBench);

            _benchNote = new Card();
            host.Controls.Add(_benchNote);
            Label nl = new Label();
            nl.Text = "这个测试不看任何系统计数器 —— 它在固定时间里数 CPU 一共算了多少次开方，\r\n" +
                      "所以计数器坏掉、读数被钉死都不影响它。同一台机器上数字越大，表示出力越足。\r\n\r\n" +
                      "注意：这个数字每次会有 ±20% 的天然抖动。想比较两个设置谁更好，\r\n" +
                      "请在同一状态下连测三次取中间值，只测一次很容易被噪声骗。";
            nl.Font = Theme.F(8.8f, FontStyle.Regular);
            nl.ForeColor = Theme.Dim;
            nl.BackColor = Theme.Card;
            nl.AutoSize = true;
            _benchNote.Controls.Add(nl);
        }

        // ── 游戏专项页 ───────────────────────────────────────────────
        private void BuildGame(Panel host)
        {
            _gameCard = new Card();
            host.Controls.Add(_gameCard);

            Label t = new Label();
            t.Text = "主程序与显卡绑定";
            t.Font = Theme.F(9.5f, FontStyle.Bold);
            t.ForeColor = Theme.Text;
            t.BackColor = Color.Transparent;
            t.AutoSize = true;
            t.Location = new Point(20, 16);
            _gameCard.Controls.Add(t);

            _gExe = new Label();
            _gExe.Font = Theme.Mono(8.2f, FontStyle.Regular);
            _gExe.ForeColor = Theme.Dim;
            _gExe.BackColor = Color.Transparent;
            _gExe.AutoSize = false;
            _gExe.TextAlign = ContentAlignment.TopLeft;
            _gameCard.Controls.Add(_gExe);

            _gPref = new Label();
            _gPref.Font = Theme.F(10f, FontStyle.Bold);
            _gPref.ForeColor = Theme.Text;
            _gPref.BackColor = Color.Transparent;
            _gPref.AutoSize = true;
            _gameCard.Controls.Add(_gPref);

            _btnGpuPref = new Btn();
            _btnGpuPref.Text = "登记为高性能独显";
            _btnGpuPref.Primary = true;
            _btnGpuPref.Click += delegate { DoGpuPref(); };
            _gameCard.Controls.Add(_btnGpuPref);

            _gAdvice = new Label();
            _gAdvice.Font = Theme.F(8.5f, FontStyle.Regular);
            _gAdvice.ForeColor = Theme.Faint;
            _gAdvice.BackColor = Color.Transparent;
            _gAdvice.AutoSize = true;
            _gameCard.Controls.Add(_gAdvice);

            // ── 后台守护 ──
            _guardCard = new Card();
            host.Controls.Add(_guardCard);

            Label t3 = new Label();
            t3.Text = "后台守护";
            t3.Font = Theme.F(9.5f, FontStyle.Bold);
            t3.ForeColor = Theme.Text;
            t3.BackColor = Color.Transparent;
            t3.AutoSize = true;
            t3.Location = new Point(20, 16);
            _guardCard.Controls.Add(t3);

            _guardState = new Label();
            _guardState.Font = Theme.F(10f, FontStyle.Bold);
            _guardState.ForeColor = Theme.Text;
            _guardState.BackColor = Color.Transparent;
            _guardState.AutoSize = true;
            _guardCard.Controls.Add(_guardState);

            _guardNote = new Label();
            _guardNote.Font = Theme.F(8.5f, FontStyle.Regular);
            _guardNote.ForeColor = Theme.Faint;
            _guardNote.BackColor = Color.Transparent;
            _guardNote.AutoSize = true;
            _guardCard.Controls.Add(_guardNote);

            _btnGuard = new Btn();
            _btnGuard.Text = "开启守护";
            _btnGuard.Click += delegate { DoGuard(); };
            _guardCard.Controls.Add(_btnGuard);

            // ── 后台程序压制（同一张卡片的下半段）──
            Label t3b = new Label();
            t3b.Text = "后台程序压制";
            t3b.Font = Theme.F(9.5f, FontStyle.Bold);
            t3b.ForeColor = Theme.Text;
            t3b.BackColor = Color.Transparent;
            t3b.AutoSize = true;
            t3b.Location = new Point(20, 102);
            _guardCard.Controls.Add(t3b);

            _pinState = new Label();
            _pinState.Font = Theme.F(8.5f, FontStyle.Regular);
            _pinState.ForeColor = Theme.Faint;
            _pinState.BackColor = Color.Transparent;
            _pinState.AutoSize = true;
            _guardCard.Controls.Add(_pinState);

            _btnPin = new Btn();
            _btnPin.Text = "立即压制";
            _btnPin.Click += delegate { DoPin(); };
            _guardCard.Controls.Add(_btnPin);

            // ── 配置文件检查 ──
            _cfgCard = new Card();
            host.Controls.Add(_cfgCard);

            Label t2 = new Label();
            t2.Text = "配置文件检查";
            t2.Font = Theme.F(9.5f, FontStyle.Bold);
            t2.ForeColor = Theme.Text;
            t2.BackColor = Color.Transparent;
            t2.AutoSize = true;
            t2.Location = new Point(20, 16);
            _cfgCard.Controls.Add(t2);

            _btnGameRefresh = new Btn();
            _btnGameRefresh.Text = "重新检查";
            _btnGameRefresh.Click += delegate { LoadGame(); };
            _cfgCard.Controls.Add(_btnGameRefresh);

            _btnCfgOpen = new Btn();
            _btnCfgOpen.Text = "打开配置文件";
            _btnCfgOpen.Click += delegate { OpenCfg(); };
            _cfgCard.Controls.Add(_btnCfgOpen);

            // ★ 这里必须是两行高：Label 的 GDI 换行规则是「中日韩字符之间可以断行、
            //   拉丁字母串不能断」，而配置路径正是一段没有空格的拉丁串。
            //   于是「【港服】」占第一行、整条路径被挤到第二行 —— 高度只给一行的话
            //   第二行会被【默默裁掉】，屏幕上只剩一个「【港服】」，不报错也不进日志。
            _cfgInfo = new Label();
            _cfgInfo.AutoSize = false;
            _cfgInfo.AutoEllipsis = true;
            _cfgInfo.BackColor = Color.Transparent;
            _cfgInfo.ForeColor = Theme.Dim;
            _cfgInfo.Font = Theme.F(9.2f, FontStyle.Regular);
            _cfgCard.Controls.Add(_cfgInfo);
            _cfgTip = new ToolTip();
            _cfgTip.SetToolTip(_cfgInfo, "");

            _cfgTbl = new Table();
            _cfgTbl.Head = new string[] { "检查项", "实际值", "判定", "说明" };
            _cfgTbl.W = new int[] { 106, 130, 82, 0 };
            _cfgTbl.Align = new bool[] { false, false, false, false };
            _cfgTbl.BadgeCol = 2;
            _cfgTbl.RowH = 28;
            _cfgTbl.HeadH = 30;
            _cfgCard.Controls.Add(_cfgTbl);

            _cfgNote = new Label();
            _cfgNote.AutoSize = false;
            _cfgNote.BackColor = Color.Transparent;
            _cfgNote.ForeColor = Theme.Dim;
            _cfgNote.Font = Theme.F(9.2f, FontStyle.Regular);
            _cfgCard.Controls.Add(_cfgNote);
        }

        private void BuildLog(Panel host)
        {
            _logCard = new Card();
            host.Controls.Add(_logCard);

            _log = new DarkBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Both;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Theme.Card;
            _log.ForeColor = Theme.Text;
            _log.Font = Theme.Mono(8.5f, FontStyle.Regular);
            _log.WordWrap = false;
            _logCard.Controls.Add(_log);

            _logRefresh = new Btn();
            _logRefresh.Text = "刷新";
            _logRefresh.Click += delegate { LoadLog(); };
            host.Controls.Add(_logRefresh);

            _logOpen = new Btn();
            _logOpen.Text = "打开所在文件夹";
            _logOpen.Click += delegate
            {
                try { Process.Start("explorer.exe", "/select,\"" + Engine.LogPath + "\""); }
                catch { }
            };
            host.Controls.Add(_logOpen);
        }

        private void BuildStatus()
        {
            bool admin = Engine.IsAdmin();
            _adminBadge.Text = admin ? "●  管理员权限" : "●  普通权限（优化时会弹 UAC）";
            _adminBadge.ForeColor = admin ? Theme.Green : Theme.Yellow;
        }

        // ── 布局 ─────────────────────────────────────────────────────
        private void DoLayout()
        {
            int sw = 210;
            _side.Bounds = new Rectangle(0, 0, sw, ClientSize.Height);
            _content.Bounds = new Rectangle(sw, 0, ClientSize.Width - sw, ClientSize.Height);

            for (int i = 0; i < 5; i++)
            {
                _nav[i].Bounds = new Rectangle(16, 124 + i * 46, 176, 42);
                _pages[i].Bounds = new Rectangle(0, 0, _content.Width, _content.Height);
            }

            for (int i = 0; i < 2; i++)
                _gameTab[i].Bounds = new Rectangle(18 + i * 88, 78, 84, 30);

            _adminBadge.Bounds = new Rectangle(18, _side.Height - 62, 180, 20);
            Control[] cl = _side.Controls.Find("cpuLabel", false);
            if (cl.Length > 0) cl[0].Bounds = new Rectangle(18, _side.Height - 40, 180, 30);

            int W = _content.Width, H = _content.Height;
            int pad = 22;

            // 主页
            _statusCard.Bounds = new Rectangle(pad, pad, W - pad * 2, 118);
            _stTitle.Location = new Point(20, 16);
            _stBig.Location = new Point(18, 36);
            _stSub.Location = new Point(21, 76);
            _stScheme.Location = new Point(21, 95);

            _modeCard.Bounds = new Rectangle(pad, pad + 118 + 14, W - pad * 2, 152);
            int mw = (_modeCard.Width - 20 * 2 - 12 * 3) / 4;
            for (int i = 0; i < 4; i++)
                if (_mc[i] != null) _mc[i].Bounds = new Rectangle(20 + i * (mw + 12), 18, mw, 116);

            int by = _modeCard.Bottom + 14;
            _btnApply.Bounds = new Rectangle(pad, by, 190, 46);
            _btnRestore.Bounds = new Rectangle(pad + 202, by, 160, 46);
            _outCard.Bounds = new Rectangle(pad, by + 46 + 14, W - pad * 2, H - (by + 46 + 14) - pad);
            _out.Bounds = new Rectangle(14, 12, _outCard.Width - 28, _outCard.Height - 24);

            // 体检
            _checkBtn.Bounds = new Rectangle(pad, pad, 140, 42);
            _checkCard.Bounds = new Rectangle(pad, pad + 56, W - pad * 2, H - pad * 2 - 56);
            // 电源表按 10 行留高（Catalog 目前 10 项），剩下的高度全给后台采样表，
            // 它自己带滚动条，行多了也能看全。
            int cw = _checkCard.Width, ch = _checkCard.Height;
            int powH = _powTbl.HeadH + _powTbl.RowH * 10 + 6;
            _checkInfo.Bounds = new Rectangle(18, 12, cw - 36, 20);
            _powTbl.Bounds = new Rectangle(2, 38, cw - 4, powH);
            _checkNote.Bounds = new Rectangle(18, 38 + powH + 8, cw - 36, 38);
            int bgY = 38 + powH + 8 + 44;
            _bgTbl.Bounds = new Rectangle(2, bgY, cw - 4, Math.Max(60, ch - bgY - 34));
            _bgNote.Bounds = new Rectangle(18, bgY + Math.Max(60, ch - bgY - 34) + 2, cw - 36, 22);

            // 性能
            _benchCard.Bounds = new Rectangle(pad, pad, W - pad * 2, 150);
            _benchBig.Location = new Point(20, 20);
            _benchSub.Location = new Point(21, 62);
            _benchDetail.Location = new Point(21, 88);
            _btnBench.Bounds = new Rectangle(pad, pad + 150 + 14, 260, 46);
            _benchNote.Bounds = new Rectangle(pad, pad + 150 + 14 + 46 + 14, W - pad * 2, H - (pad + 150 + 14 + 46 + 14) - pad);
            if (_benchNote.Controls.Count > 0) _benchNote.Controls[0].Location = new Point(20, 18);

            // 日志
            _logRefresh.Bounds = new Rectangle(pad, pad, 110, 42);
            _logOpen.Bounds = new Rectangle(pad + 122, pad, 160, 42);
            _logCard.Bounds = new Rectangle(pad, pad + 56, W - pad * 2, H - pad * 2 - 56);
            _log.Bounds = new Rectangle(14, 12, _logCard.Width - 28, _logCard.Height - 24);

            // 游戏专项
            _gameCard.Bounds = new Rectangle(pad, pad, W - pad * 2, 176);
            _gExe.Bounds = new Rectangle(21, 46, _gameCard.Width - 250, 40);
            _gPref.Location = new Point(21, 92);
            _btnGpuPref.Bounds = new Rectangle(_gameCard.Width - 200, 84, 180, 40);
            _gAdvice.Location = new Point(21, 132);

            _guardCard.Bounds = new Rectangle(pad, pad + 176 + 14, W - pad * 2, 200);
            _guardState.Bounds = new Rectangle(21, 44, _guardCard.Width - 230, 22);
            _guardNote.Bounds = new Rectangle(21, 70, _guardCard.Width - 42, 24);
            _btnGuard.Bounds = new Rectangle(_guardCard.Width - 180, 24, 160, 40);
            // AutoSize 的标签一定要配 MaximumSize，否则文字一长就横着冲出卡片外；
            // 而 MaximumSize 的高度不够时 WinForms 会直接【裁掉】后面的行，不报错。
            _pinState.MaximumSize = new Size(_guardCard.Width - 230, 64);
            _pinState.Location = new Point(21, 120);
            _btnPin.Bounds = new Rectangle(_guardCard.Width - 180, 100, 160, 40);
            _cfgCard.Bounds = new Rectangle(pad, pad + 176 + 14 + 200 + 14, W - pad * 2,
                H - (pad + 176 + 14 + 156 + 14) - pad);
            _btnGameRefresh.Bounds = new Rectangle(_cfgCard.Width - 116, 12, 96, 32);
            _btnCfgOpen.Bounds = new Rectangle(_cfgCard.Width - 116 - 126, 12, 116, 32);
            int ccw = _cfgCard.Width, cch = _cfgCard.Height;
            _cfgInfo.Bounds = new Rectangle(18, 46, ccw - 36, 36);
            _cfgTbl.Bounds = new Rectangle(2, 86, ccw - 4, Math.Max(60, cch - 86 - 30));
            _cfgNote.Bounds = new Rectangle(18, cch - 26, ccw - 36, 20);
        }

        private void ShowPage(int i)
        {
            int old = _page;
            _page = i;
            for (int k = 0; k < 5; k++)
            {
                _pages[k].Visible = (k == i);
                _nav[k].Selected = (k == i);
                _nav[k].Invalidate();
            }
            SlideIn(_pages[i], i > old ? 1 : (i < old ? -1 : 0));
            if (i == 1) RunCheck(false);
            if (i == 3) LoadGame();
            if (i == 4) LoadLog();
        }

        // 换页时新页从侧面滑进来。位移只有 26px —— 再大就会变成「等它飘过来」，
        // 而不是「已经到下一页了」。方向跟着导航顺序走：往下点从右来，往回点从左来。
        private void SlideIn(Control np, int dir)
        {
            if (np == null || dir == 0 || !np.IsHandleCreated) return;   // 构造期不动画
            Anim.Cancel(np);
            np.Left = 26 * dir;
            // 这里的 v 已经过 ease-out，1-v 就是「还剩多少没滑完」
            Anim.To(np, 0f, 1f, 200, delegate(float v)
            {
                np.Left = (int)Math.Round(26 * dir * (1f - v));
            });
        }

        // ── 业务动作 ─────────────────────────────────────────────────
        private string RunHeadless(string cmd, string arg)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "vcb-" + Guid.NewGuid().ToString("N") + ".txt");
            string args = cmd + (arg == null ? "" : " " + arg) + " --out \"" + tmp + "\"";
            try
            {
                if (Engine.IsAdmin())
                {
                    string text = Program.Headless(cmd, arg);
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                }
                else
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, args);
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                    using (Process p = Process.Start(psi))
                    {
                        p.WaitForExit();
                    }
                }
                if (File.Exists(tmp)) { string s = File.ReadAllText(tmp, Encoding.UTF8); try { File.Delete(tmp); } catch { } return s; }
                return "（没有拿到输出。可能是提权被取消，或子进程异常退出。）";
            }
            catch (Exception ex)
            {
                return "操作失败：" + ex.Message + "\r\n（如果是在 UAC 弹窗上点了「否」，这是正常的。）";
            }
        }

        private void Busy(bool on, string what)
        {
            _btnApply.Enabled = !on;
            _btnRestore.Enabled = !on;
            _btnBench.Enabled = !on;
            if (on) { _stBig.Text = what; _stBig.ForeColor = Theme.Yellow; }
        }

        private void DoApply()
        {
            ModeProfile m = Modes.Find(_modeKey);
            if (MessageBox.Show(
                    "即将应用「" + m.Name + "」模式。\r\n\r\n" +
                    "· 会先把当前所有设置的原始值备份下来（可一键还原）\r\n" +
                    "· 只修改「当前正在使用的」电源方案，不会新建或切换方案\r\n" +
                    "· 需要管理员权限\r\n\r\n" +
                    "继续吗？", "确认优化",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Busy(true, "正在优化…");
            _out.Text = "正在执行，请稍等…";
            Application.DoEvents();

            string text = RunHeadless("apply", m.Key);
            _out.Text = text.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            Engine.Log("界面：应用 " + m.Name);
            Busy(false, "");
            RunCheck(false);
        }

        private void DoRestore()
        {
            if (MessageBox.Show(
                    "即将把所有电源设置还原成优化之前的值。\r\n\r\n继续吗？", "确认还原",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            Busy(true, "正在还原…");
            _out.Text = "正在执行，请稍等…";
            Application.DoEvents();

            string text = RunHeadless("restore", null);
            _out.Text = text.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
            Engine.Log("界面：还原");
            Busy(false, "");
            RunCheck(false);
        }

        private bool _bgBusy;

        private void RunCheck(bool force)
        {
            string text;
            try { text = Program.Headless("check", null); }
            catch (Exception ex) { text = "检测失败：" + ex.Message; }

            string info, note;
            FillPowerTable(_powTbl, text, out info, out note);
            _checkInfo.Text = info;
            _checkNote.Text = note;

            _bgTbl.Clear();
            _bgTbl.Add(2, "…", "正在采样后台程序（6 秒）", "别关窗口");
            _bgTbl.Finish();
            _bgNote.Text = "";

            // 顺带更新主页状态
            bool allOk = text.IndexOf("优化已生效") >= 0;
            bool someBad = text.IndexOf("不符合") >= 0 && text.IndexOf("不符合 0 项") < 0;
            string scheme = Engine.SchemeNameOf(PowerCfg.ActiveSchemeGuid());
            _stScheme.Text = scheme + "  ·  " + PowerCfg.ActiveSchemeGuid();
            _stSub.Text = "当前电源方案：" + scheme;

            if (someBad)
            {
                _stBig.Text = "未优化";
                _stBig.ForeColor = Theme.Yellow;
                _stTitle.Text = "优化状态 · 检测到设置不符合游戏目标";
            }
            else if (allOk)
            {
                _stBig.Text = "已优化";
                _stBig.ForeColor = Theme.Green;
                _stTitle.Text = "优化状态 · 设置已写入当前生效的方案";
            }
            else
            {
                _stBig.Text = "状态未知";
                _stBig.ForeColor = Theme.Dim;
                _stTitle.Text = "优化状态";
            }
            UpdateStatusLine();
            RunBgSample();
        }

        /// <summary>
        /// 后台抢占采样要 6 秒，绝不能占着 UI 线程（那样切到体检页会卡死 6 秒）。
        /// 先填好电源表，采样在后台线程跑完再填采样表。
        /// 这里直接调 SysAudit.Sample 拿结构化数据 —— 比以前把文本再解析一遍干净。
        /// </summary>
        private void RunBgSample()
        {
            if (_bgBusy) return;
            _bgBusy = true;
            int cpus = Environment.ProcessorCount;
            Thread bt = new Thread(delegate()
            {
                List<BgRow> rows = null;
                string err = null;
                try { rows = SysAudit.Sample(6000); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((MethodInvoker)delegate()
                    {
                        try
                        {
                            _bgTbl.Clear();
                            if (err != null) _bgTbl.Add(1, "—", "采样失败", err);
                            else if (rows.Count == 0)
                            {
                                _bgTbl.Add(2, "0.0%", "—", "没有程序占用超过 0.5% 单核");
                                _bgNote.Text = "判读：很干净，后台基本没抢东西。";
                            }
                            else
                            {
                                double total = 0;
                                int hot = 0;
                                foreach (BgRow r in rows)
                                {
                                    total += r.Pct;
                                    if (r.Note.Length > 0 && r.Pct >= 3.0) hot++;
                                    // 1 红（抢得凶）/ 3 琥珀（值得看一眼）/ 2 灰（无所谓）
                                    int kind = r.Pct >= 40 ? 1 : (r.Pct >= 10 ? 3 : 2);
                                    _bgTbl.Add(kind, r.Pct.ToString("0.0") + "%", r.Name,
                                               r.Note.Length > 0 ? r.Note : "—");
                                }
                                _bgNote.Text = "合计 " + total.ToString("0.0") + "% 单核（占全部 "
                                             + cpus + " 核的 " + (total / cpus).ToString("0.0") + "%）　·　"
                                             + (total < 15 ? "很干净，后台基本没抢东西。"
                                                : total < 40 ? "有点杂，" + hot + " 个程序值得一提，能关的都关掉。"
                                                : "抢得厉害，" + hot + " 个程序在明显吃 CPU，先处理它们再谈帧率。");
                            }
                            _bgTbl.Finish();
                        }
                        catch { }
                        _bgBusy = false;
                    });
                }
                catch { _bgBusy = false; }
            });
            bt.IsBackground = true;
            bt.Start();
        }

        private void UpdateStatusLine()
        {
            ModeProfile m = Modes.Find(_modeKey);
            if (_stSub.Text.Length > 0 && _stSub.Text.IndexOf("｜") < 0)
                _stSub.Text = _stSub.Text + "｜当前选中：" + m.Name + " 模式";
            else
            {
                int i = _stSub.Text.IndexOf("｜");
                if (i >= 0) _stSub.Text = _stSub.Text.Substring(0, i);
                _stSub.Text = _stSub.Text + "｜当前选中：" + m.Name + " 模式";
            }
        }

        private void DoBench()
        {
            _btnBench.Enabled = false;
            _benchBig.Text = "正在测试…";
            _benchBig.ForeColor = Theme.Yellow;
            _benchSub.Text = "让 22 个线程一起算开方，别动鼠标，大约 8 秒。";
            _benchDetail.Text = "";
            Application.DoEvents();

            Thread t = new Thread(delegate()
            {
                BenchResult r = Bench.Full();
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _btnBench.Enabled = true;
                        if (!r.Ok)
                        {
                            _benchBig.Text = "测试失败";
                            _benchBig.ForeColor = Theme.Red;
                            _benchSub.Text = r.Error;
                            return;
                        }
                        double ratio = r.Single > 0 ? (double)r.All / r.Single : 0;
                        _benchBig.Text = Fmt(r.All) + " 次/秒";
                        _benchBig.ForeColor = Theme.Green;
                        _benchSub.Text = "全核吞吐量（" + r.Threads + " 线程同时算）";
                        _benchDetail.Text =
                            "单线程吞吐      " + Fmt(r.Single) + " 次/秒\r\n" +
                            "全核吞吐        " + Fmt(r.All) + " 次/秒\r\n" +
                            "全核 / 单线程   " + ratio.ToString("0.00") + " 倍   （逻辑核 " + r.Threads + " 个）\r\n" +
                            "推算实时频率    " + r.EstMHz.ToString("0") + " MHz   （标称 × 性能%，不是直接读数）\r\n" +
                            "标称频率        " + r.PeakMHz.ToString("0") + " MHz\r\n" +
                            "核心停泊        " + r.Parked + " / " + r.LogicalSeen + " 个逻辑核被停泊";
                        Engine.Log("性能测试：" + Fmt(r.All) + " 次/秒");
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private static string Fmt(long v)
        {
            return v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ── 游戏专项 ─────────────────────────────────────────────────
        private void SwitchGame(string key)
        {
            if (key == null || key == _gameKey) return;
            _gameKey = key;
            ApplyGameChrome();
            if (_page == 3) LoadGame();
        }

        private void ApplyGameChrome()
        {
            GameProfile g = Games.Find(_gameKey);
            _gameTitle.Text = g.Name;
            _gameSub.Text = g.Sub + " · CPU 高频优化器";
            for (int i = 0; i < 2; i++)
            {
                _gameTab[i].Primary = (_games[i].Key == _gameKey);
                _gameTab[i].Invalidate();
            }
        }

        private void LoadGame()
        {
            GameProfile g = Games.Find(_gameKey);
            string exe = Games.FoundExe(g);

            if (exe.Length == 0)
            {
                _gExe.Text = "没找到主程序。找过这些位置：\r\n" + string.Join("\r\n", g.Exes);
                _gExe.ForeColor = Theme.Red;
            }
            else
            {
                _gExe.Text = exe;
                _gExe.ForeColor = Theme.Dim;
            }

            string raw = GpuPref.Get(exe);
            bool hi = GpuPref.IsHigh(raw);
            _gPref.Text = "显卡绑定：" + GpuPref.Describe(raw);
            _gPref.ForeColor = hi ? Theme.Green : Theme.Yellow;
            _btnGpuPref.Enabled = (exe.Length > 0);
            _btnGpuPref.Text = hi ? "已登记（可重设）" : "登记为高性能独显";
            _gAdvice.Text = "建议档位：" + Modes.Find(g.RecMode).Name + "　·　" + g.Tip;

            string head;
            List<GameCheck> list = GameAudit.Run(g, out head);
            _cfgInfo.Text = head;
            _cfgTip.SetToolTip(_cfgInfo, head);
            _cfgTbl.Clear();
            int bad = 0, warn = 0;
            foreach (GameCheck c in list)
            {
                // 0 绿 符合 / 1 红 不符合 / 3 琥珀 注意
                int kind = c.Verdict == "不符合" ? 1 : (c.Verdict == "注意" ? 3 : 0);
                _cfgTbl.Add(kind, c.Name, c.Value, c.Verdict, c.Advice);
                if (c.Verdict == "不符合") bad++;
                else if (c.Verdict == "注意") warn++;
            }
            _cfgTbl.Finish();
            if (list.Count == 0) _cfgNote.Text = "（没有可检查的项）";
            else if (bad == 0 && warn == 0) _cfgNote.Text = "全部符合 —— 这个配置文件没在拖后腿。";
            else _cfgNote.Text = "不符合 " + bad + " 项，值得注意 " + warn + " 项。";

            RefreshGuard();
        }

        /// <summary>刷新「后台守护」卡片的状态显示（只读查询，不改任何东西）。</summary>
        private void RefreshGuard()
        {
            try { _guardOn = AutoStart.IsInstalled(); } catch { _guardOn = false; }
            _guardState.Text = _guardOn ? "已开启 · 开机静默启动" : "未开启";
            _guardState.ForeColor = _guardOn ? Theme.Green : Theme.Dim;
            _btnGuard.Text = _guardOn ? "关闭守护" : "开启守护";
            if (_guardNote.Text.Length == 0)
                _guardNote.Text = _guardOn
                    ? "游戏启动时自动优化，退出后自动还原 —— 全程不弹 UAC。"
                    : "开启后开机静默常驻托盘：检测到游戏启动就优化，游戏退出就还原。";
            RefreshPin();
        }

        /// <summary>刷新「后台程序压制」那一段（只读，看记录文件 + 问系统要亲和性）。</summary>
        private void RefreshPin()
        {
            string[] names = CorePin.PinnedNames();
            _pinOn = names.Length > 0;
            if (_pinOn)
            {
                _pinState.Text = "已压制 " + names.Length
                    + " 类，只跑能效核（P 核腾给游戏，加速照常）：\r\n"
                    + string.Join("、", names);
                _pinState.ForeColor = Theme.Green;
                _btnPin.Text = "放回全部核心";
            }
            else
            {
                _pinState.Text = "未压制。压了之后雷神、壁纸这些常驻程序只跑能效核，"
                    + "加速功能照常，只是不再跟游戏抢 P 核。";
                _pinState.ForeColor = Theme.Faint;
                _btnPin.Text = "立即压制";
            }
        }

        /// <summary>手动压缩/还原后台程序。跟守护自动做的是同一件事，用同一份记录文件。</summary>
        private void DoPin()
        {
            _btnPin.Enabled = false;
            string text;
            try
            {
                if (_pinOn)
                {
                    int n;
                    text = CorePin.Restore(out n);
                    Engine.Log("[压制] 放回全部核心，" + n + " 个实例");
                }
                else
                {
                    int n;
                    text = CorePin.Apply(out n);
                    Engine.Log("[压制] 压到能效核，" + n + " 个实例");
                }
            }
            catch (Exception ex) { text = "操作失败：" + ex.Message; }
            finally { _btnPin.Enabled = true; }

            bool was = _pinOn;
            RefreshPin();
            string one = text.Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " ").Trim();
            if (one.Length > 48) one = one.Substring(0, 48) + "…";
            _pinState.Text = one;
            _pinState.ForeColor = was ? Theme.Faint : Theme.Green;
        }

        /// <summary>
        /// 安装/卸载开机自启动。用的是计划任务 + 最高权限，所以这一步需要管理员
        /// （RunHeadless 会自动提权，弹一次 UAC，仅此一次）。
        /// </summary>
        private void DoGuard()
        {
            bool installed = AutoStart.IsInstalled();
            _btnGuard.Enabled = false;
            string text;
            try { text = RunHeadless(installed ? "uninstall" : "install", null); }
            finally { _btnGuard.Enabled = true; }

            RefreshGuard();
            string one = text.Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " ").Trim();
            if (one.Length > 108) one = one.Substring(0, 108) + "…";
            _guardNote.Text = one;
            _guardNote.ForeColor = _guardOn ? Theme.Green : Theme.Yellow;
        }

        private void DoGpuPref()
        {
            GameProfile g = Games.Find(_gameKey);
            string exe = Games.FoundExe(g);
            if (exe.Length == 0)
            {
                MessageBox.Show("没找到 " + g.Name + " 的主程序，无法登记。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (GpuPref.Register(exe))
            {
                Engine.Log("[显卡绑定] " + g.Name + " -> " + exe + " 已登记为高性能");
                LoadGame();
            }
            else
            {
                MessageBox.Show("写注册表失败。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenCfg()
        {
            GameProfile g = Games.Find(_gameKey);
            string cfg = Games.ResolveConfig(g);
            if (cfg.Length == 0) cfg = g.ConfigPath;
            try
            {
                if (File.Exists(cfg))
                    Process.Start("notepad.exe", "\"" + cfg + "\"");
                else
                    Process.Start("explorer.exe", "/select,\"" + cfg + "\"");
            }
            catch { }
        }

        // 表格对齐。界面里走 TextPad 的像素算法（雅黑是比例字体，
        // 数格子对不齐）；命令行那条路由 TextPad 自己回退成按格计数。
        private static string PadW(string s, int w)
        {
            return TextPad.To(s, w);
        }

        private void LoadLog()
        {
            try
            {
                if (File.Exists(Engine.LogPath)) _log.Text = File.ReadAllText(Engine.LogPath, Encoding.UTF8);
                else _log.Text = "还没有日志。";
            }
            catch (Exception ex) { _log.Text = "读日志失败：" + ex.Message; }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  把 Flutter 主界面叫出来
    //  托盘「显示主界面」和命令行 `ValorantBoost.exe show` 共用这一段。
    //
    //  以前托盘那一下走的是 Process.Start(AutoStart.ExePath())，而
    //  AutoStart.ExePath() 返回 Assembly.GetExecutingAssembly().Location，也就是
    //  ValorantBoost.exe 它自己 —— 不带参数进去，Main 会走到
    //  Application.Run(new MainForm())，于是弹出来的是老的 WinForms 窗口，
    //  和 Flutter 那套完全是两个界面。现在这里改成拉 Flutter 那一版。
    // ────────────────────────────────────────────────────────────────────
    internal static class UiLaunch
    {
        // 主界面（Flutter 版）的进程名与计划任务名。
        // ★ UiTaskName 必须和 installer\Vcb.cs 的 Vcb.UiTaskName 以及
        //   installer\Installer.cs 里 RegisterUiTask 用的名字一致 —— 引擎不引用
        //   安装器的源码，所以这里是第二份，改一处必须改两处。
        //   那个任务的实际命令是 <安装目录>\valorant_boost.exe，RunLevel=HighestAvailable，
        //   所以 schtasks /run 拉起来的界面是管理员身份、不弹 UAC。
        public const string UiProcName = "valorant_boost";
        public const string UiTaskName = "ValorantBoostUI";

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        private const int SW_RESTORE = 9;

        /// <summary>
        /// 界面没开就开，开着就叫到前面。返回一句人话说明这次干了什么
        /// （给日志和命令行的 --out 用）。
        /// </summary>
        public static string Show()
        {
            // ① 已经在跑 → 叫到前面。
            //    没有这一步的话，用户把窗口最小化或压到别的窗口后面之后就再也
            //    点不出来了 —— ValorantBoostUI 任务设了 IgnoreNew，重复触发是空操作。
            //
            //    ★ 这里用【进程名】找窗口而不是 FindWindow 按标题找：标题
            //      「游戏 CPU 高频优化器」里的中文要经过源码编码才落进 exe
            //      （build-engine.ps1 用 /codepage:65001 读无 BOM 的 UTF-8），
            //      一旦编码错位 FindWindow 会【无声地】永远找不到窗口。
            //      进程名是纯 ASCII，没有这个风险。
            try
            {
                foreach (Process p in Process.GetProcessesByName(UiProcName))
                {
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;   // 还在启动、窗口没建好
                    bool wasMin = IsIconic(h);
                    if (wasMin) ShowWindow(h, SW_RESTORE);
                    SetForegroundWindow(h);
                    return "界面已经在跑（pid " + p.Id + "），已"
                        + (wasMin ? "从最小化还原并" : "") + "叫到前面。";
                }
            }
            catch { }

            // ② 没在跑 → 走计划任务，和桌面快捷方式同一条路：管理员身份、零 UAC。
            try
            {
                string o;
                if (AutoStart.Run("schtasks.exe", "/run /tn \"" + UiTaskName + "\"", out o) == 0)
                    return "界面没在跑，已通过计划任务 " + UiTaskName + " 拉起（管理员身份，不弹 UAC）。";
            }
            catch { }

            // ③ 兜底：绿色版没注册过计划任务，界面 exe 就躺在引擎旁边或上一级。
            try
            {
                string ui = FindUiExe();
                if (ui.Length > 0)
                {
                    ProcessStartInfo psi = new ProcessStartInfo(ui);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    return "计划任务不可用（绿色版？），已直接启动：" + ui;
                }
            }
            catch (Exception ex) { return "启动界面失败：" + ex.Message; }

            return "找不到界面程序，也没能触发计划任务 " + UiTaskName + "。";
        }

        private static string FindUiExe()
        {
            string[] cands = new string[] {
                Path.Combine(Engine.Dir, "valorant_boost.exe"),
                Path.Combine(Engine.Dir, @"..\valorant_boost.exe"),
            };
            foreach (string c in cands)
            {
                try { if (File.Exists(c)) return Path.GetFullPath(c); }
                catch { }
            }
            return "";
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  后台守护：常驻托盘，盯着游戏进程 —— 开了就优化，关了还原
    //  为什么是轮询而不是 WMI 进程创建事件：轮询 4 秒一次，两个进程名的开销约等于零，
    //  而 WMI 事件订阅要额外的权限、有 1 秒以上延迟，还会在休眠唤醒后失效。
    // ────────────────────────────────────────────────────────────────────
    internal class WatchApp : ApplicationContext
    {
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _timer;
        private Icon _iconIdle, _iconLive;
        private List<GameProfile> _games;
        private string _modeKey;
        private bool _active;
        private int _quietTicks;
        private int _repinTicks;        // 距上次「补压新起的后台程序」过了几轮
        private string _seenScheme = "";   // 上次看到的「活动电源方案」，用来发现荣耀把方案换掉了
        private MenuItem _miAuto;

        /// <summary>
        /// 守护用独立的备份文件，绝不碰界面里「一键优化」留下的 boost-backup.txt。
        /// 否则守护还原时会把用户手动优化的那次一起抹掉。
        /// </summary>
        public static string AutoBackupPath
        {
            get { return Path.Combine(Engine.Dir, "auto-backup.txt"); }
        }

        public WatchApp(string modeKey)
        {
            _modeKey = (modeKey == null || modeKey.Length == 0) ? "competitive" : modeKey;
            _games = Games.Build();
            _iconIdle = MakeIcon(Theme.Accent);
            _iconLive = MakeIcon(Theme.Green);

            _miAuto = new MenuItem("开机自启动");
            _miAuto.Click += delegate(object s, EventArgs e) { ToggleAuto(); };

            MenuItem show = new MenuItem("显示主界面");
            show.Click += delegate(object s, EventArgs e) { ShowMain(); };
            MenuItem opt = new MenuItem("立即优化");
            opt.Click += delegate(object s, EventArgs e) { DoApply("右键菜单"); };
            MenuItem res = new MenuItem("立即还原");
            res.Click += delegate(object s, EventArgs e) { DoRestore("右键菜单"); };
            MenuItem quit = new MenuItem("退出守护");
            quit.Click += delegate(object s, EventArgs e) { Quit(); };

            _tray = new NotifyIcon();
            _tray.Icon = _iconIdle;
            _tray.Text = "游戏 CPU 高频优化器 · 待机";
            _tray.ContextMenu = new ContextMenu(new MenuItem[] {
                show, opt, res, new MenuItem("-"), _miAuto, new MenuItem("-"), quit });
            _tray.DoubleClick += delegate(object s, EventArgs e) { ShowMain(); };
            _tray.Visible = true;

            // 启动时冒一次气泡。Windows 11 默认把新图标收进隐藏区，气泡是唯一的「我还活着」提示。
            try
            {
                _tray.BalloonTipTitle = "后台守护已启动";
                _tray.BalloonTipText = "游戏启动就自动优化，退出就还原。"
                    + "图标如果不在任务栏上，点任务栏的 ^ 找它，或拖出来固定。";
                _tray.ShowBalloonTip(8000);
            }
            catch { }

            RefreshAutoMenu();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 4000;
            _timer.Tick += delegate(object s, EventArgs e) { Tick(); };
            _timer.Start();

            Engine.Log("守护启动：模式 " + Modes.Find(_modeKey).Name + "，每 4 秒查一次");

            // ★ 守护把自己钉到能效核。
            // 它整轮工作就是「取一次进程快照 + 比对方案 GUID」，微秒级的事，
            // 没有任何理由待在 P 核上 —— 而这台机器上游戏是 CPU 瓶颈。
            // 注意这【只】改本进程；给雷神、壁纸那些设亲和性走的是它们各自的句柄。
            try
            {
                if (CorePin.PinSelf())
                    Engine.Log("守护：自己已钉到能效核，不跟游戏抢 P 核");
                else if (!CpuTopo.Usable)
                    // 不是失败，是本机没有大小核可分 —— 写一条带原因的日志，
                    // 免得排障的人把「没有这行」当成 SetProcessAffinityMask 挂了。
                    Engine.Log("守护：没把自己钉到能效核 —— " + CpuTopo.Note);
            }
            catch { }

            // 安全网：上一次守护可能是在游戏运行中被杀掉的（任务计划、手动结束进程、
            // 系统更新都会），那样后台程序会一直被压在能效核上。启动时如果游戏没在跑，
            // 先无条件把它们放回去 —— 宁可多还原一次，也不要让雷神一直瘸着。
            try
            {
                string gname0;
                if (!IsGameRunning(out gname0) && CorePin.PinnedNames().Length > 0)
                {
                    int n0;
                    CorePin.Restore(out n0);
                    Engine.Log("守护：启动时没检测到游戏，先把 " + n0 + " 个后台实例放回全部核心");
                }
            }
            catch (Exception ex0) { Engine.Log("守护：启动清理异常 " + ex0.Message); }

            Tick();
        }

        private static Icon MakeIcon(Color c)
        {
            try
            {
                Bitmap bmp = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // ★ 整块铺满，绝不留透明像素。
                    // Icon.FromHandle(bmp.GetHicon()) 不保留 alpha 通道，透明区会被渲染成
                    // 纯黑 —— 画成「透明底 + 圆」的图标到了任务栏上就是一个认不出来的黑方块。
                    g.Clear(c);
                    using (Pen p = new Pen(Color.FromArgb(90, 0, 0, 0), 1f))
                        g.DrawRectangle(p, 0, 0, 15, 15);
                    using (SolidBrush b = new SolidBrush(Color.White))
                    using (Font f = new Font("Segoe UI", 8f, FontStyle.Bold))
                    {
                        StringFormat sf = new StringFormat();
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString("V", f, b, new RectangleF(0, 0, 16, 16), sf);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
            catch { return SystemIcons.Application; }
        }

        private bool IsGameRunning(out string name)
        {
            name = "";
            // ★ 只取一次进程快照，所有游戏的所有进程名都在内存里查。
            // 原先是每个进程名各调一次 Process.GetProcessesByName —— 而它每次
            // 都会把系统进程表整个枚举一遍。这个方法每 4 秒跑一次，等于每 4 秒
            // 白白全量枚举 3~4 遍（Valorant 1 个名字 + Apex 2 个）。
            Dictionary<string, List<int>> snap = ProcSnap.Take();
            foreach (GameProfile g in _games)
            {
                foreach (string pn in g.Procs)
                {
                    List<int> pids = ProcSnap.Pids(snap, pn);
                    if (pids.Count == 0) continue;
                    // ★ 进程名对上还不够。鸣潮的主程序叫 Client-Win64-Shipping ——
                    //   那是【虚幻引擎的默认名】，几十个游戏共用。只看名字的话，
                    //   随便开哪个 UE 游戏都会被认成鸣潮：守护对着别人的游戏优化，
                    //   界面上还显示成鸣潮。配了 PathHint 的游戏必须再核对一次完整路径。
                    //   （无畏契约和 Apex 的名字够独特，PathHint 是空的，不多花这一趟。）
                    if (g.PathHint.Length > 0 && !Games.AnyPathHas(pids, g.PathHint)) continue;
                    name = g.Name;
                    return true;
                }
            }
            return false;
        }

        private void Tick()
        {
            string gname;
            bool running = IsGameRunning(out gname);

            if (running)
            {
                _quietTicks = 0;
                if (!_active) DoApply(gname);
                else ReapplyIfSchemeChanged();
                RepinNewcomers();
            }
            else if (_active)
            {
                // 退游戏后不立刻还原：读图、切场景、掉线重连都会让进程名短暂消失。
                // 连续两次（约 8 秒）都看不见，才算真关了。
                _quietTicks++;
                if (_quietTicks >= 2) DoRestore("游戏已退出");
            }
        }

        // ★ 荣耀电脑管家不只会「切换」活动方案，它还会把自己建的方案【删掉重建】——
        // 重建出来的是它的出厂值，我们写进去的优化就全没了（本机实测：22:02 时系统里
        // 只剩「平衡」，22:44 打游戏时 Honor Performance 又冒出来且是原始值）。
        // 所以游戏在跑的时候每轮都核对一次活动方案，一变就整组重写。
        // 重写时必须 skipBackup —— 方案里装的已经是优化值，再采集一次会把优化值
        // 当成「原始值」存进备份，之后还原成了空操作，CPU 会一直钉在 100%。
        private void ReapplyIfSchemeChanged()
        {
            // ★ 走 PowerGetActiveScheme 直接问电源服务，不启动 powercfg.exe。
            // 这个方法每 4 秒被调一次；用 powercfg.exe 的话就是游戏跑着的时候
            // 每 4 秒创建一个进程 —— 实测 16.4 ms/次、一分钟约 250 ms 纯开销。
            // 实测这一条就占了守护全部 CPU 用量的九成以上。
            string now = PowerCfg.ActiveSchemeGuidFast();
            if (string.IsNullOrEmpty(now)) now = PowerCfg.ActiveSchemeGuid();   // 兜底：老系统
            if (string.IsNullOrEmpty(now)) return;
            if (now == _seenScheme) return;
            _seenScheme = now;
            try
            {
                OpResult r = Engine.Apply(Modes.Find(_modeKey), AutoBackupPath, true);
                Engine.Log("守护：活动方案变成 " + Engine.SchemeNameOf(now) + "，重新应用 —— "
                    + (r.Ok ? "成功" : ("失败 " + r.Text.Replace("\r\n", " | "))));
            }
            catch (Exception ex) { Engine.Log("守护：重新应用异常 " + ex.Message); }
        }

        // ★ 常驻程序会在游戏跑着的时候自己重启 —— 本机实测 epicwebhelper 28524 就是
        // 压制之后才起来的，之后一直用着全部核心（漏压）。DoApply 里那次压制只在
        // 「检测到游戏」的那一瞬间做一次，管不了后来者。所以游戏在跑期间定期重扫。
        // Pin() 只对「当时不在能效核上」的实例计数，没有新实例时这里完全静默。
        private void RepinNewcomers()
        {
            _repinTicks++;
            if (_repinTicks < 15) return;   // 15 轮 × 4 秒 ≈ 60 秒
            _repinTicks = 0;
            try
            {
                int n;
                CorePin.Apply(out n);
                if (n > 0) Engine.Log("守护：补压 " + n + " 个新起的后台实例到能效核");
            }
            catch (Exception ex) { Engine.Log("守护：补压异常 " + ex.Message); }
        }

        private void DoApply(string why)
        {
            try
            {
                OpResult r = Engine.Apply(Modes.Find(_modeKey), AutoBackupPath);
                if (r.Ok)
                {
                    _active = true;
                    _quietTicks = 0;
                    _seenScheme = PowerCfg.ActiveSchemeGuid();
                    _tray.Icon = _iconLive;
                    _tray.Text = Cut("游戏中 · 已优化（" + why + "）");
                    Engine.Log("守护：检测到 " + why + "，已优化");
                }
                else
                {
                    _tray.Text = Cut("优化失败，右键看日志");
                    Engine.Log("守护：优化失败 —— " + r.Text.Replace("\r\n", " | "));
                }
            }
            catch (Exception ex) { Engine.Log("守护：优化异常 " + ex.Message); }

            // 后台程序压制：跟电源方案分开做，电源那步成没成都照压。
            // 理由：这是纯收益（把常驻程序赶离 P 核），而且完全可逆。
            try
            {
                if (CorePin.PinnedNames().Length == 0)
                {
                    int n;
                    // 保留 Apply 的报告：0 个的时候要把原因区分开 —— 「没找到可以压的」
                    // 和「本机压根没大小核」在日志里原本长得一模一样，用户看不出是
                    // 设计如此还是坏了。
                    string rep = CorePin.Apply(out n);
                    if (n == 0 && !CpuTopo.Usable)
                        Engine.Log("守护：压制没生效 —— " + CpuTopo.Note);
                    else
                        Engine.Log("守护：把 " + n + " 个后台实例压到能效核");
                    // 一个候选都没压上时把逐条原因也留下（在保护名单里、打不开、
                    // 设亲和性失败……），否则日志只剩一个 0，什么都查不出来。
                    if (n == 0) Engine.Log("守护：压制明细 —— " + rep.Replace("\r\n", " | "));
                }
            }
            catch (Exception ex2) { Engine.Log("守护：压制后台程序异常 " + ex2.Message); }
        }

        private void DoRestore(string why)
        {
            // 放回全部核心这件事跟电源还原成不成功无关，先做，且一定要做。
            try
            {
                if (CorePin.PinnedNames().Length > 0)
                {
                    int n;
                    CorePin.Restore(out n);
                    Engine.Log("守护：把 " + n + " 个后台实例放回全部核心");
                }
            }
            catch (Exception ex2) { Engine.Log("守护：还原后台程序异常 " + ex2.Message); }

            try
            {
                OpResult r = Engine.Restore(AutoBackupPath);
                _active = false;
                _quietTicks = 0;
                _seenScheme = "";
                _tray.Icon = _iconIdle;
                _tray.Text = Cut("待机（" + why + "，已还原）");
                Engine.Log(r.Ok ? ("守护：" + why + "，已还原")
                                : ("守护：还原失败 —— " + r.Text.Replace("\r\n", " | ")));
            }
            catch (Exception ex)
            {
                _active = false;
                _quietTicks = 0;
                _seenScheme = "";
                Engine.Log("守护：还原异常 " + ex.Message);
            }
        }

        // NotifyIcon.Text 超过 63 个字符会抛异常
        private static string Cut(string s)
        {
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        private void ShowMain()
        {
            try { Engine.Log("托盘：" + UiLaunch.Show()); }
            catch { }
        }

        private void ToggleAuto()
        {
            try
            {
                string msg;
                bool ok;
                if (AutoStart.IsInstalled()) ok = AutoStart.Uninstall(out msg);
                else ok = AutoStart.Install(out msg);
                RefreshAutoMenu();
                _tray.BalloonTipTitle = ok ? "游戏 CPU 高频优化器" : "操作失败";
                _tray.BalloonTipText = msg.Length > 220 ? msg.Substring(0, 220) : msg;
                _tray.ShowBalloonTip(5000);
            }
            catch { }
        }

        private void RefreshAutoMenu()
        {
            try { _miAuto.Checked = AutoStart.IsInstalled(); } catch { }
        }

        private void Quit()
        {
            try
            {
                _timer.Stop();
                _tray.Visible = false;
                Engine.Log("守护退出");
            }
            catch { }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            try { if (_tray != null) _tray.Visible = false; } catch { }
            base.Dispose(disposing);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  入口
    // ────────────────────────────────────────────────────────────────────
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        // ★ 本程序编译成 /target:winexe —— 从 cmd 里跑它，Console.Write 会进黑洞，
        //   用户敲了命令什么也看不见，只能靠 --out 落文件。
        //   ⚠️ 但 AttachConsole 不能无条件调：实测「cmd /c app.exe pinstat > 文件」时，
        //   一旦挂上父控制台，.NET 就把内容写回 CONOUT$ 了，重定向的文件始终是 0 字节
        //   （调试证实 Write 返回成功、写了 1792 字节，文件却是空的）。所以先看
        //   stdout 有没有被接走：是文件或管道就什么都别动，只有压根没有句柄
        //   （双击启动）才需要挂控制台。
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFileType(IntPtr hFile);
        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;
        private const uint FILE_TYPE_UNKNOWN = 0x0000;

        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }

            // 命令行模式：界面提权时会用 apply/restore/check 拉起一个无窗口实例
            if (args.Length > 0)
            {
                // ★ 先看 stdout 有没有被接走：被重定向到文件/管道时就保持原样；
                //   压根没有句柄（双击启动）才挂父控制台。详见上面 AttachConsole 的注释。
                try
                {
                    if (GetFileType(GetStdHandle(STD_OUTPUT_HANDLE)) == FILE_TYPE_UNKNOWN)
                        AttachConsole(ATTACH_PARENT_PROCESS);
                }
                catch { }

                string cmd = args[0].ToLowerInvariant();

                // watch 需要消息循环（托盘图标常驻），不能走 Headless 那条同步返回的路
                if (cmd == "watch")
                {
                    string mk = "competitive";
                    // ★ 守护被安装到 ProgramData 后，靠 --dir 把「日志和自动备份写哪」
                    //   带回来，否则界面读不到守护写的那份 boost-log.txt。
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] == "--dir" && i + 1 < args.Length) { Engine.SetDir(args[i + 1]); i++; }
                        else if (!args[i].StartsWith("--")) mk = args[i];
                    }
                    // ★ 单实例保护：计划任务在登录时拉起一个，用户手动点「开启守护」又会拉起一个，
                    // 两个守护同时写 auto-backup.txt 会互相覆盖，日志也会交错。
                    // 本机实测真的撞上过两个同时跑（pid 34428 与 36332）。
                    bool created;
                    System.Threading.Mutex one =
                        new System.Threading.Mutex(true, "Global\\ValorantBoostWatcher", out created);
                    if (!created)
                    {
                        Engine.Log("守护：已经有一个守护在跑了，本次退出");
                        return;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    try { Application.Run(new WatchApp(mk)); }
                    catch (Exception ex) { Engine.Log("守护崩溃：" + ex.Message); }
                    GC.KeepAlive(one);
                    return;
                }

                if (cmd == "apply" || cmd == "restore" || cmd == "check" || cmd == "bench"
                    || cmd == "audit" || cmd == "gpupref" || cmd == "bgcpu"
                    || cmd == "install" || cmd == "uninstall" || cmd == "autocheck"
                    || cmd == "pin" || cmd == "unpin" || cmd == "pinstat"
                    || cmd == "gametime"
                    || cmd == "show"
                    || cmd == "psoclean"
                    || cmd == "psodry"
                    || cmd == "engini"
                    || cmd == "enginirevert"
                    || cmd == "probe")
                {
                    string arg = null;
                    string outp = null;
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] == "--out" && i + 1 < args.Length) { outp = args[i + 1]; i++; }
                        else if (!args[i].StartsWith("--")) arg = args[i];
                    }
                    string text = Headless(cmd, arg);
                    if (outp != null)
                    {
                        try { File.WriteAllText(outp, text, new UTF8Encoding(false)); } catch { }
                    }
                    // ★ 没给 --out 时，以前这份结果算完就直接扔掉 —— 用户敲 `pinstat`
                    //   什么都看不见。现在同时打到标准输出。
                    //   这里刻意不用 Console.Out：WinExe 的 Console.Out 可能在
                    //   AttachConsole 之前就被 CLR 初始化成「没有控制台」的状态了，
                    //   之后怎么 Write 都进黑洞。OpenStandardOutput() 是直接去要
                    //   STD_OUTPUT_HANDLE，父进程要是重定向过，拿到的就是那个文件。
                    try
                    {
                        System.IO.Stream so = Console.OpenStandardOutput();
                        byte[] bb = new UTF8Encoding(false).GetBytes(
                            text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                        so.Write(bb, 0, bb.Length);
                        so.Flush();
                    }
                    catch { }
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                MessageBox.Show("程序异常退出：\r\n\r\n" + ex.ToString(), "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public static string Headless(string cmd, string arg)
        {
            try
            {
                // 托盘「显示主界面」的那一击，命令行也能复现 —— 排障时不用真去点托盘。
                if (cmd == "show") return UiLaunch.Show();
                if (cmd == "apply") return Engine.Apply(Modes.Find(arg == null ? "competitive" : arg)).Text;
                if (cmd == "restore") return Engine.Restore().Text;
                if (cmd == "check") return Engine.Check().Text;
                if (cmd == "bench") return CmdBench(arg);
                if (cmd == "audit") return CmdAudit(arg);
                if (cmd == "psoclean") return CmdPsoClean(arg, false);
                if (cmd == "psodry") return CmdPsoClean(arg, true);
                // Engine.ini 的进阶项（目前只有锐化那一项）。跟 psoclean 一样，
                // 写入和还原必须是两个命令名 —— CLI 只认一个位置参数。
                if (cmd == "engini") return EngIni.Apply(Games.Find(arg == null ? "" : arg.Trim()));
                if (cmd == "enginirevert") return EngIni.Revert(Games.Find(arg == null ? "" : arg.Trim()));
                if (cmd == "gametime") return CmdGameTime(arg);
                if (cmd == "autocheck") return CmdWatchProbe();
                if (cmd == "probe") return CmdProbe();
                if (cmd == "bgcpu") return SysAudit.Report(6000);
                if (cmd == "pin")
                {
                    int n;
                    return CorePin.Apply(out n);
                }
                if (cmd == "unpin")
                {
                    int n;
                    return CorePin.Restore(out n);
                }
                if (cmd == "pinstat") return CorePin.Report();
                if (cmd == "install")
                {
                    string m; bool ok = AutoStart.Install(out m);
                    return (ok ? "" : "失败：\r\n") + m;
                }
                if (cmd == "uninstall")
                {
                    string m; bool ok = AutoStart.Uninstall(out m);
                    return (ok ? "" : "失败：\r\n") + m;
                }
                if (cmd == "gpupref")
                {
                    GameProfile g = Games.Find(arg);
                    string exe = Games.FoundExe(g);
                    if (exe.Length == 0) return "没找到 " + g.Name + " 的主程序。";
                    return GpuPref.Register(exe) ? ("已登记为高性能：" + exe) : "写注册表失败。";
                }
                return "未知命令：" + cmd;
            }
            catch (Exception ex)
            {
                return "执行失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 后台守护的一次性自检（无窗口）：只报告状态，不改任何设置。
        /// 用来确认「监视哪些进程、自启动装没装、备份文件在哪」。
        /// </summary>
        // 临时诊断命令：把几种「找进程」的办法并排跑，看哪一种在游戏运行时稳定。
        // 起因：守护明明在跑却检测不到游戏，而同一个 exe 从桌面 shell 跑就能看到。
        private static string CmdProbe()
        {
            StringBuilder sb = new StringBuilder();
            List<GameProfile> gs = Games.Build();
            string pn = "VALORANT-Win64-Shipping";
            sb.AppendLine("目标进程   : " + pn);
            sb.AppendLine("进程位数   : " + (IntPtr.Size * 8) + " 位");
            sb.AppendLine("当前用户   : " + System.Security.Principal.WindowsIdentity.GetCurrent().Name);
            sb.AppendLine("工作目录   : " + Environment.CurrentDirectory);
            sb.AppendLine("SessionId  : " + Process.GetCurrentProcess().SessionId);
            sb.AppendLine("交互式     : " + Environment.UserInteractive);
            sb.AppendLine("进程总数   : " + Process.GetProcesses().Length);
            // 对照组：任务上下文到底能不能看见「别的」进程
            sb.AppendLine("对照组     : " + ControlProbe("explorer") + " / "
                + ControlProbe("dwm") + " / " + ControlProbe("svchost"));
            sb.AppendLine();
            for (int i = 0; i < 8; i++)
            {
                // ① Process.GetProcessesByName
                string a;
                try { Process[] ps = Process.GetProcessesByName(pn); a = ps.Length.ToString(); foreach (Process p in ps) p.Dispose(); }
                catch (Exception ex) { a = "异常 " + ex.GetType().Name; }

                // ② Process.GetProcesses() 自己按名字过滤
                string b;
                try
                {
                    int n = 0;
                    Process[] all = Process.GetProcesses();
                    foreach (Process p in all)
                    {
                        try { if (string.Equals(p.ProcessName, pn, StringComparison.OrdinalIgnoreCase)) n++; }
                        catch { }
                        p.Dispose();
                    }
                    b = n.ToString();
                }
                catch (Exception ex) { b = "异常 " + ex.GetType().Name; }

                // ③ WMI
                string c;
                try
                {
                    System.Management.ManagementObjectSearcher s =
                        new System.Management.ManagementObjectSearcher(
                            "SELECT ProcessId FROM Win32_Process WHERE Name='" + pn + ".exe'");
                    int n = 0;
                    foreach (System.Management.ManagementBaseObject mo in s.Get()) { n++; mo.Dispose(); }
                    c = n.ToString();
                }
                catch (Exception ex) { c = "异常 " + ex.GetType().Name; }

                sb.AppendLine("第 " + (i + 1) + " 次  GetProcessesByName=" + a
                    + "   GetProcesses过滤=" + b + "   WMI=" + c);

                if (i < 7) System.Threading.Thread.Sleep(2500);
            }
            return sb.ToString();
        }

        private static string ControlProbe(string pn)
        {
            try
            {
                Process[] ps = Process.GetProcessesByName(pn);
                int n = ps.Length;
                foreach (Process p in ps) p.Dispose();
                return pn + "=" + n;
            }
            catch { return pn + "=异常"; }
        }

        private static string CmdWatchProbe()
        {
            StringBuilder sb = new StringBuilder();
            List<GameProfile> gs = Games.Build();
            sb.AppendLine("===== 后台守护自检 =====");
            sb.AppendLine("自身路径   : " + AutoStart.ExePath());
            sb.AppendLine("自启动     : " + AutoStart.StatusText());
            sb.AppendLine("自动备份   : " + WatchApp.AutoBackupPath);
            sb.AppendLine("手动备份   : " + Engine.BackupPath + "（守护不会碰它）");
            sb.AppendLine("轮询间隔   : 4 秒；游戏退出后要连续 2 次看不见才还原");
            sb.AppendLine("");
            sb.AppendLine("监视的进程：");
            foreach (GameProfile g in gs)
            {
                string exe = Games.FoundExe(g);
                sb.AppendLine("  " + g.Name + "  进程 " + string.Join(" / ", g.Procs)
                    + "   主程序" + (exe.Length > 0 ? "已找到" : "没找到"));
                foreach (string pn in g.Procs)
                {
                    int n = 0;
                    try
                    {
                        Process[] ps = Process.GetProcessesByName(pn);
                        n = ps.Length;
                        foreach (Process p in ps) p.Dispose();
                    }
                    catch { }
                    sb.AppendLine("      当前 " + pn + " 实例数 = " + n + (n > 0 ? "   ← 正在运行" : ""));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 量「游戏专项」页每一步各花多少毫秒（无窗口）。
        /// 用户反馈点这一栏要等很久，先把时间花在哪一步量出来，别猜。
        /// </summary>
        private static string CmdGameTime(string arg)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("游戏专项页各步骤耗时");
            sb.AppendLine("--------------------------------------------------");
            GameProfile g = Games.Find(arg);
            sb.AppendLine("游戏：" + g.Name);
            sb.AppendLine();
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

            string head = null;
            for (int pass = 1; pass <= 2; pass++)
            {
                sb.AppendLine(pass == 1 ? "第一遍（含 JIT 和首次磁盘缓存）" : "第二遍（热路径，界面实际感受接近这一遍）");

                sw.Restart();
                string exe = Games.FoundExe(g);
                sw.Stop();
                sb.AppendLine("  FoundExe 找主程序                " + sw.ElapsedMilliseconds + " ms");

                sw.Restart();
                string raw = GpuPref.Get(exe);
                sw.Stop();
                sb.AppendLine("  GpuPref.Get 读显卡绑定           " + sw.ElapsedMilliseconds + " ms");

                sw.Restart();
                List<GameCheck> list = GameAudit.Run(g, out head);
                sw.Stop();
                sb.AppendLine("  GameAudit.Run 查配置             " + sw.ElapsedMilliseconds + " ms   " + list.Count + " 项");

                sw.Restart();
                bool inst = AutoStart.IsInstalled();
                sw.Stop();
                sb.AppendLine("  AutoStart.IsInstalled 起进程     " + sw.ElapsedMilliseconds + " ms   " + inst);

                sw.Restart();
                string[] names = CorePin.PinnedNames();
                sw.Stop();
                sb.AppendLine("  CorePin.PinnedNames 读名单       " + sw.ElapsedMilliseconds + " ms   " + names.Length + " 类");

                sw.Restart();
                Games.ResolveConfig(g);
                sw.Stop();
                sb.AppendLine("  ResolveConfig 找配置文件         " + sw.ElapsedMilliseconds + " ms");

                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>
        /// 游戏专项检查（无窗口）。参数是游戏键名：valorant / apex。
        /// </summary>
        private static string CmdAudit(string arg)
        {
            GameProfile g = Games.Find(arg);
            StringBuilder sb = new StringBuilder();
            string exe = Games.FoundExe(g);
            sb.AppendLine("===== 游戏专项检查：" + g.Name + " =====");
            sb.AppendLine("主程序     : " + (exe.Length > 0 ? exe : "没找到"));
            sb.AppendLine("显卡绑定   : " + GpuPref.Describe(GpuPref.Get(exe)));
            sb.AppendLine("进程名     : " + string.Join(" / ", g.Procs));
            sb.AppendLine("推荐档位   : " + Modes.Find(g.RecMode).Name);
            sb.AppendLine("说明       : " + g.Tip);
            sb.AppendLine("");
            string head;
            List<GameCheck> list = GameAudit.Run(g, out head);
            sb.AppendLine("配置文件   : " + head);
            sb.AppendLine("");
            if (list.Count == 0)
            {
                sb.AppendLine("  （没有可检查的项）");
                return sb.ToString();
            }
            int bad = 0, warn = 0;
            foreach (GameCheck c in list)
            {
                sb.AppendLine("  " + c.Name + "  |  " + c.Value + "  |  " + c.Verdict + "  |  " + c.Advice);
                if (c.Verdict == "不符合") bad++;
                else if (c.Verdict == "注意") warn++;
            }
            sb.AppendLine("");
            sb.AppendLine("不符合 " + bad + " 项，值得注意 " + warn + " 项。");
            return sb.ToString();
        }

        /// <summary>
        /// 清理旧版着色器预缓存。参数就是游戏 key，一个词。
        ///
        /// ★ 为什么分成两个命令名（psodry / psoclean）而不是 `psoclean wuwa dry`：
        ///   命令行解析在上面那个 for 循环里 —— 它【只留最后一个非 -- 开头的参数】，
        ///   所以 `psoclean wuwa dry` 传进来的 arg 是 "dry"，不是 "wuwa"，
        ///   Games.Find("dry") 找不到就退回第一个游戏（无畏契约），删错目录。
        ///   这个坑我踩过一次（预览时打出了 VALORANT 的路径），所以改成两个名字，
        ///   各自只吃一个参数，跟现有 CLI 的模型严丝合缝。
        ///
        /// 界面上应当先跑 psodry 把清单摆给用户看，确认了再跑 psoclean —— 删文件
        /// 这一步不该在用户没看过清单的情况下发生。真删的逻辑见 Core.cs 的 Pso.Clean。
        /// </summary>
        private static string CmdPsoClean(string arg, bool dry)
        {
            return Pso.Clean(Games.Find(arg == null ? "" : arg.Trim()), dry);
        }

        /// <summary>
        /// 持续负载吞吐量测试。同一份代码、同样的线程数、同样的时长，反复跑若干轮。
        /// 判读：如果第 4、5 轮明显低于第 1 轮，说明机器在持续负载下**掉频**（热墙或功耗墙），
        /// 这时候「把最小处理器状态钉在 100%」是火上浇油 —— CPU 一直在最高功耗，
        /// 温度提前撞墙，全核频率反而更低。
        /// 参数：轮数（默认 5），每轮秒数固定 6 秒。
        /// </summary>
        private static string CmdBench(string arg)
        {
            int rounds = 5;
            if (arg != null) { int t; if (int.TryParse(arg, out t) && t > 0) rounds = t; }
            int secs = 6;

            CpuInfo ci = CpuInfo.Detect();
            int threads = ci.Logical > 0 ? ci.Logical : Environment.ProcessorCount;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("===== 持续负载吞吐量测试 =====");
            sb.AppendLine("CPU        : " + ci.Name);
            sb.AppendLine("逻辑处理器 : " + threads);
            sb.AppendLine("每轮       : " + secs + " 秒，共 " + rounds + " 轮");
            sb.AppendLine("数字含义   : 固定时间里算出的开方次数，越大越好");
            sb.AppendLine();

            double e0, p0; int k0, s0;
            Bench.SampleFrequency(out e0, out p0, out k0, out s0);
            sb.AppendLine(string.Format("开始前  估算频率 {0:N0} MHz   停泊 {1}/{2}", e0, k0, s0));
            sb.AppendLine();

            List<long> v = new List<long>();
            long first = 0;
            for (int i = 0; i < rounds; i++)
            {
                long n = Bench.Run(threads, secs * 1000);
                v.Add(n);
                if (i == 0) first = n;
                double pct = first > 0 ? (100.0 * n / first) : 0;
                sb.AppendLine(string.Format("第 {0} 轮   {1,16:N0}   {2,6:N1}%", i + 1, n, pct));
            }

            double e1, p1; int k1, s1;
            Bench.SampleFrequency(out e1, out p1, out k1, out s1);

            List<long> sorted = new List<long>(v);
            sorted.Sort();
            long med = sorted[sorted.Count / 2];
            long last = v[v.Count - 1];
            double drop = first > 0 ? (100.0 * (first - last) / first) : 0;

            sb.AppendLine();
            sb.AppendLine("---------------- 汇总 ----------------");
            sb.AppendLine(string.Format("中位数     : {0:N0}", med));
            sb.AppendLine(string.Format("首轮       : {0:N0}", first));
            sb.AppendLine(string.Format("末轮       : {0:N0}", last));
            sb.AppendLine(string.Format("首→末衰减 : {0:N1}%", drop));
            sb.AppendLine(string.Format("结束时频率 : 估算 {0:N0} MHz   停泊 {1}/{2}", e1, k1, s1));
            sb.AppendLine("--------------------------------------");
            sb.AppendLine();
            if (drop > 8)
                sb.AppendLine("判读：掉幅超过 8% —— **机器在持续负载下确实在降频**（热墙或功耗墙）。");
            else if (drop > 3)
                sb.AppendLine("判读：掉幅 3%~8% —— 轻微降频，属于正常范围。");
            else
                sb.AppendLine("判读：基本没掉 —— 至少在这 30 秒里，机器没有撞到温度墙或功耗墙。");
            sb.AppendLine();
            sb.AppendLine("注意：这个数字受后台程序影响很大。比较两次结果时，");
            sb.AppendLine("      后台必须保持一样（同一个加速器、同一张壁纸、同样开着的客户端）。");
            return sb.ToString();
        }
    }
}
