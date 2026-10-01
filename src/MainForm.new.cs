// ============================================================================
//  MainForm.new.cs  -  可视化主界面
// ----------------------------------------------------------------------------
//  这是「无畏契约 CPU 高频优化器」的图形界面部分：无边框窗口 + 左侧导航 +
//  仪表盘（实时频率/占用率仪表盘）+ 15 项设置对比 + 60 秒实时曲线 + 日志。
//
//  它和 ValorantCpuBoost.cs 是同一个命名空间的两个文件，由 build.ps1 一起编译：
//      csc.exe ... ValorantCpuBoost.cs MainForm.new.cs
//  数据来源：PowerEngine（电源方案读写）、CpuMetrics（实时频率/占用率）、
//            ProcTuner（进程增强）、RegTweaks（注册表）。
//
//  【别删这些 using】这个文件通篇直接用 Form / Panel / Label / Color / Graphics /
//  GraphicsPath 这些短名字。少了对应的 using，csc 会为每一处报
//  CS0246「未能找到类型或命名空间名称」。System.Drawing.Drawing2D 是给
//  GraphicsPath / SmoothingMode 用的，最容易被漏掉。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ValorantCpuBoost
{
    // ======================= 主界面（可视化） =======================
    internal class MainForm : Form
    {
        // ---- 配色（深色导航 + 浅色内容区，参考 Win11 Fluent 的层次感）----
        private static readonly Color C_BG = Color.FromArgb(240, 243, 248);        // 内容区底色
        private static readonly Color C_NAV = Color.FromArgb(18, 24, 36);          // 左侧导航
        private static readonly Color C_CARD = Color.White;                         // 卡片
        private static readonly Color C_LINE = Color.FromArgb(224, 230, 240);      // 卡片描边
        private static readonly Color C_SHADOW = Color.FromArgb(16, 24, 48, 92);   // 卡片投影（最前面的 16 是透明度）
        private static readonly Color C_TEXT = Color.FromArgb(30, 37, 50);
        private static readonly Color C_SUB = Color.FromArgb(118, 129, 148);
        private static readonly Color C_MAIN = Color.FromArgb(0, 110, 218);        // 主色
        private static readonly Color C_OK = Color.FromArgb(26, 164, 100);
        private static readonly Color C_WARN = Color.FromArgb(233, 134, 30);
        private static readonly Color C_DANGER = Color.FromArgb(206, 74, 64);
        private static readonly Color C_FREQ = Color.FromArgb(243, 138, 26);       // 频率曲线/仪表
        private static readonly Color C_LOAD = Color.FromArgb(74, 144, 250);       // 占用曲线/仪表
        private static readonly Color C_LOG_BG = Color.FromArgb(15, 19, 28);
        private static readonly Color C_LOG_FG = Color.FromArgb(186, 220, 202);
        private static readonly Color C_NAV_ON = Color.FromArgb(0, 100, 190);
        private static readonly Color C_TRACK = Color.FromArgb(229, 234, 242);     // 仪表底环/进度条底槽

        // ---- 基本信息 ----
        private CpuInfo cpu;
        private int mhzMax = 5000;

        // ---- 标题栏 ----
        private Panel topBar;
        private Label lblTitle, lblSubTitle, lblAdmin, lblUser;
        private Button btnMin, btnMax, btnClose, btnTopTune;

        // ---- 导航 ----
        private Panel nav;
        private Button navDash, navSet, navLog;

        // ---- 页面容器 ----
        private Panel pnlPages, pagDash, pagSet, pagLog;

        // ---- 仪表盘 ----
        private Panel pnlDashCpu, pnlDashScheme, pnlDashFreq, pnlDashLoad;
        private Panel pnlDashAct, pnlDashLog;
        private TextBox dashLog;
        private Label lblD1T, lblD1State, lblD1P, lblD1E, lblD1Lpe;
        private Label lblD2Name, lblD2Guid, lblD2Mode, lblD2Source;
        private Label lblFreqRange, lblLoadWhat;
        private Button btnApplyTop, btnTuneTop, btnCheckTop, btnRestoreTop, btnExitTop;

        // ---- 设置页 ----
        private Panel setList, pnlSetHead;
        private ScrollHost scrollHost;
        private Label lblSetCount, lblSetSummary;
        private RadioButton rbSafe, rbCompetitive, rbBalanced, rbMax;
        private Button btnSetRefresh, btnSetApply;
        private List<RowRef> rows = new List<RowRef>();

        // ---- 日志页 / 曲线 ----
        private Panel pnlChart, pnlChartLogWrap;
        private TextBox log;
        private Label lblChartNow;
        private Bitmap chartBuf;

        // ---- 运行状态 ----
        private readonly object _lock = new object();
        private Sample latest = new Sample();
        private bool hasSample;
        private Queue<Action> uiQ = new Queue<Action>();
        private System.Windows.Forms.Timer uiTimer;
        private System.Threading.Timer bgTimer;
        private int[] histLoad = new int[60];
        private int[] histMhz = new int[60];
        private int histCount, tick;
        private bool firstLog = true;

        private class RowRef
        {
            public SettingDef Def;
            public bool Supported = true;
            public Panel Row;
            public Label Value, Status, Reason;
        }

        public MainForm()
        {
            Text = "无畏契约 CPU 高频优化器 v3.1";
            ClientSize = new Size(1020, 700);
            MinimumSize = new Size(1020, 700);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = C_BG;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;
            KeyPreview = true;
            cpu = CpuInfo.Detect();
            if (cpu.MaxMhz > 0) mhzMax = (int)(Math.Ceiling((cpu.MaxMhz + 300) / 500.0) * 500.0);

            BuildTopBar();
            BuildNav();
            BuildPages();
            BuildDashboard();
            BuildSettingsPage();
            BuildLogPage();
            BuildGrip();

            Load += OnFormLoad;
            MouseWheel += OnFormWheel;
            FormClosing += OnFormClosing;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); };
        }

        // ==================================================================
        //  窗口骨架
        // ==================================================================
        private void BuildTopBar()
        {
            topBar = new Panel();
            topBar.SetBounds(0, 0, 1020, 58);
            topBar.BackColor = C_NAV;
            topBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            lblTitle = new Label();
            lblTitle.Text = "无畏契约  CPU 高频优化器";
            lblTitle.Font = new Font("Microsoft YaHei UI", 13.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.White;
            lblTitle.BackColor = C_NAV;
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(18, 8);
            lblTitle.MouseDown += DragWindow;

            lblSubTitle = new Label();
            lblSubTitle.Text = "Intel Core Ultra (Meteor Lake) 专项 · 频率地板 / EPP / 大小核调度 / 核心停泊";
            lblSubTitle.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblSubTitle.ForeColor = Color.FromArgb(130, 160, 205);
            lblSubTitle.BackColor = C_NAV;
            lblSubTitle.AutoSize = true;
            lblSubTitle.Location = new Point(20, 33);
            lblSubTitle.MouseDown += DragWindow;

            lblAdmin = Badge(620, 19, IsAdmin() ? "● 管理员" : "● 未提权", IsAdmin() ? C_OK : C_DANGER);

            lblUser = new Label();
            try { lblUser.Text = Environment.UserName; } catch { lblUser.Text = ""; }
            lblUser.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblUser.ForeColor = Color.FromArgb(140, 155, 180);
            lblUser.BackColor = C_NAV;
            lblUser.AutoSize = true;
            lblUser.Location = new Point(700, 22);
            lblUser.MouseDown += DragWindow;

            btnTopTune = GrayButton("一键增强游戏进程", 770, 14, 160, 30);
            btnTopTune.Click += delegate { DoTune(); };

            btnMin = GrayButton("—", 948, 14, 22, 30);
            btnMax = GrayButton("□", 970, 14, 22, 30);
            btnClose = GrayButton("✕", 992, 14, 22, 30);
            btnClose.BackColor = C_DANGER;
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            btnMax.Click += delegate
            {
                if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
                else WindowState = FormWindowState.Maximized;
            };
            btnClose.Click += delegate { Close(); };

            topBar.Controls.Add(lblTitle);
            topBar.Controls.Add(lblSubTitle);
            topBar.Controls.Add(lblAdmin);
            topBar.Controls.Add(lblUser);
            topBar.Controls.Add(btnTopTune);
            topBar.Controls.Add(btnMin);
            topBar.Controls.Add(btnMax);
            topBar.Controls.Add(btnClose);
            topBar.MouseDown += DragWindow;
            // 标题栏顶部一条 3px 强调色带，把整个窗口"框"起来，比纯色块更精致
            topBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(0, 0, Math.Max(1, topBar.Width), 3),
                    C_MAIN, Color.FromArgb(120, C_MAIN), LinearGradientMode.Horizontal))
                    e.Graphics.FillRectangle(b, 0, 0, topBar.Width, 3);
            };
            Controls.Add(topBar);
        }

        private Label Badge(int x, int y, string text, Color c)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            l.ForeColor = c;
            l.BackColor = C_NAV;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            return l;
        }

        private Button GrayButton(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.FromArgb(42, 51, 68);
            b.ForeColor = Color.FromArgb(218, 226, 238);
            b.Font = new Font("Microsoft YaHei UI", 9F);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void BuildNav()
        {
            nav = new Panel();
            nav.SetBounds(0, 58, 152, 642);
            nav.BackColor = C_NAV;
            nav.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom;

            navDash = NavButton("仪表盘", "实时状态", 14);
            navSet = NavButton("设置明细", "15 项对比", 70);
            navLog = NavButton("曲线与日志", "60 秒趋势", 126);

            navDash.Click += delegate { ShowPage(0); };
            navSet.Click += delegate { ShowPage(1); };
            navLog.Click += delegate { ShowPage(2); };

            Label ver = new Label();
            // 版本号故意做得显眼：重新编译后它应该变成 v3.1。
            // 如果界面上还是 v3.0 或 v2.0，说明跑的是旧的 exe（没重新编译，或双击了别处的副本）。
            ver.Text = "v3.1 界面重做\r\n所有改动均可一键还原";
            ver.Font = new Font("Microsoft YaHei UI", 8F);
            ver.ForeColor = Color.FromArgb(104, 116, 136);
            ver.BackColor = C_NAV;
            ver.SetBounds(20, 572, 122, 44);
            ver.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            nav.Controls.Add(navDash);
            nav.Controls.Add(navSet);
            nav.Controls.Add(navLog);
            nav.Controls.Add(ver);
            nav.MouseDown += DragWindow;
            Controls.Add(nav);
        }

        private Button NavButton(string title, string sub, int y)
        {
            Button b = new Button();
            b.Text = title + "\r\n" + sub;
            b.SetBounds(10, y, 132, 48);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = C_NAV;
            b.ForeColor = Color.FromArgb(166, 180, 202);
            b.Font = new Font("Microsoft YaHei UI", 9.5F);
            b.TextAlign = ContentAlignment.MiddleLeft;
            b.Padding = new Padding(12, 0, 0, 0);
            b.Cursor = Cursors.Hand;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(33, 42, 60);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(44, 56, 78);
            b.UseVisualStyleBackColor = false;
            return b;
        }

        private void BuildPages()
        {
            pnlPages = new Panel();
            pnlPages.SetBounds(152, 58, 868, 642);
            pnlPages.BackColor = C_BG;
            pnlPages.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            pagDash = NewPage();
            pagSet = NewPage();
            pagLog = NewPage();

            pnlPages.Controls.Add(pagDash);
            pnlPages.Controls.Add(pagSet);
            pnlPages.Controls.Add(pagLog);
            Controls.Add(pnlPages);
        }

        private Panel NewPage()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            p.BackColor = C_BG;
            p.Visible = false;
            return p;
        }

        private void ShowPage(int i)
        {
            pagDash.Visible = (i == 0);
            pagSet.Visible = (i == 1);
            pagLog.Visible = (i == 2);
            navDash.BackColor = (i == 0) ? C_NAV_ON : C_NAV;
            navSet.BackColor = (i == 1) ? C_NAV_ON : C_NAV;
            navLog.BackColor = (i == 2) ? C_NAV_ON : C_NAV;
            navDash.ForeColor = (i == 0) ? Color.White : Color.FromArgb(166, 180, 202);
            navSet.ForeColor = (i == 1) ? Color.White : Color.FromArgb(166, 180, 202);
            navLog.ForeColor = (i == 2) ? Color.White : Color.FromArgb(166, 180, 202);
            if (i == 2) DrawChart();
        }

        // ==================================================================
        //  仪表盘
        // ==================================================================
        private void BuildDashboard()
        {
            // ---------- CPU 信息卡 ----------
            pnlDashCpu = Card(14, 14, 500, 120, pagDash);
            AddTitle(pnlDashCpu, "处理器", 16, 12);
            lblD1T = CardText(pnlDashCpu, 16, 36, 464, 30, 10.5F, FontStyle.Bold, C_TEXT);
            lblD1T.Text = cpu.Name;
            lblD1State = CardText(pnlDashCpu, 16, 70, 464, 22, 9F, FontStyle.Regular, C_SUB);
            lblD1State.Text = "核心构成：" + (cpu.PCores > 0
                ? cpu.PCores + " P 核(超线程) + " + cpu.ECores + " E 核 + " + cpu.LpeCores + " LP-E 核"
                : "正在识别…");
            lblD1P = CardText(pnlDashCpu, 16, 94, 150, 20, 9F, FontStyle.Regular, C_MAIN);
            lblD1P.Text = "逻辑线程  " + cpu.Threads;
            lblD1E = CardText(pnlDashCpu, 172, 94, 170, 20, 9F, FontStyle.Regular, C_MAIN);
            lblD1E.Text = "架构  " + (cpu.IsHybrid ? "混合架构 (大小核)" : "同构 / 未识别");
            lblD1Lpe = CardText(pnlDashCpu, 348, 94, 132, 20, 9F, FontStyle.Regular, C_MAIN);
            lblD1Lpe.Text = cpu.PCores > 0 ? "建议绑定 P 核" : "";

            // ---------- 当前电源方案卡 ----------
            pnlDashScheme = Card(524, 14, 330, 120, pagDash);
            AddTitle(pnlDashScheme, "当前电源方案", 16, 12);
            lblD2Name = CardText(pnlDashScheme, 16, 36, 298, 26, 11F, FontStyle.Bold, C_WARN);
            lblD2Name.Text = "读取中…";
            lblD2Guid = CardText(pnlDashScheme, 16, 64, 298, 18, 8F, FontStyle.Regular, C_SUB);
            lblD2Guid.Text = "";
            lblD2Mode = CardText(pnlDashScheme, 16, 84, 298, 20, 9F, FontStyle.Regular, C_TEXT);
            lblD2Mode.Text = "";
            lblD2Source = CardText(pnlDashScheme, 16, 102, 298, 16, 8F, FontStyle.Regular, C_SUB);
            lblD2Source.Text = "";

            // ---------- 两个仪表 ----------
            pnlDashFreq = Card(14, 144, 415, 200, pagDash);
            pnlDashFreq.Paint += PaintFreqGauge;
            lblFreqRange = CardText(pnlDashFreq, 16, 176, 383, 18, 8.5F, FontStyle.Regular, C_SUB);
            lblFreqRange.Text = "";

            pnlDashLoad = Card(443, 144, 411, 200, pagDash);
            pnlDashLoad.Paint += PaintLoadGauge;
            lblLoadWhat = CardText(pnlDashLoad, 16, 176, 379, 18, 8.5F, FontStyle.Regular, C_SUB);
            lblLoadWhat.Text = "采样：GetSystemTimes 差分（与任务管理器接近）";

            // ---------- 操作按钮 ----------
            pnlDashAct = Card(14, 354, 840, 80, pagDash);
            AddTitle(pnlDashAct, "操作", 16, 10);
            btnApplyTop = ActionButton(pnlDashAct, "① 应用优化", 16, 34, 152, C_MAIN);
            btnTuneTop = ActionButton(pnlDashAct, "② 增强游戏进程", 182, 34, 152, Color.FromArgb(0, 150, 136));
            btnCheckTop = ActionButton(pnlDashAct, "③ 体检 / 查看", 348, 34, 152, Color.FromArgb(92, 102, 122));
            btnRestoreTop = ActionButton(pnlDashAct, "④ 一键还原", 514, 34, 152, Color.FromArgb(198, 78, 68));
            btnExitTop = ActionButton(pnlDashAct, "退出", 680, 34, 144, Color.FromArgb(122, 128, 140));
            btnApplyTop.Click += delegate { DoApply(); };
            btnTuneTop.Click += delegate { DoTune(); };
            btnCheckTop.Click += delegate { ShowPage(1); RefreshSettings(true); };
            btnRestoreTop.Click += delegate { DoRestore(); };
            btnExitTop.Click += delegate { Close(); };

            // ---------- 迷你日志 ----------
            pnlDashLog = Card(14, 444, 840, 182, pagDash);
            AddTitle(pnlDashLog, "实时日志", 16, 10);
            dashLog = new TextBox();
            dashLog.Multiline = true;
            dashLog.ReadOnly = true;
            dashLog.ScrollBars = ScrollBars.Vertical;
            dashLog.BorderStyle = BorderStyle.None;
            dashLog.BackColor = C_LOG_BG;
            dashLog.ForeColor = C_LOG_FG;
            dashLog.Font = new Font("Consolas", 8.5F);
            dashLog.SetBounds(16, 36, 808, 132);
            dashLog.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            pnlDashLog.Controls.Add(dashLog);

            pagDash.Resize += delegate { LayoutDashboard(); };
        }

        private void LayoutDashboard()
        {
            int w = pagDash.ClientSize.Width;
            int h = pagDash.ClientSize.Height;
            if (w < 720) w = 720;
            int cw1 = (int)(w * 0.60);
            int cw2 = w - 28 - cw1;
            pnlDashCpu.SetBounds(14, 14, cw1, 120);
            LayoutCpuCard(cw1);
            pnlDashScheme.SetBounds(14 + cw1 + 14, 14, cw2, 120);
            LayoutSchemeCard(cw2);
            int half = (w - 28) / 2;
            pnlDashFreq.SetBounds(14, 144, half, 200);
            lblFreqRange.SetBounds(16, 176, half - 32, 18);
            pnlDashLoad.SetBounds(14 + half + 14, 144, w - 28 - half, 200);
            lblLoadWhat.SetBounds(16, 176, w - 28 - half - 32, 18);
            pnlDashAct.SetBounds(14, 354, w - 28, 80);
            LayoutActionButtons(w - 28);
            pnlDashLog.SetBounds(14, 444, w - 28, Math.Max(120, h - 458));
            dashLog.SetBounds(16, 36, w - 28 - 32, Math.Max(60, pnlDashLog.Height - 50));
            pnlDashFreq.Invalidate();
            pnlDashLoad.Invalidate();
        }

        private void LayoutCpuCard(int w)
        {
            lblD1T.SetBounds(16, 36, w - 32, 30);
            lblD1State.SetBounds(16, 70, w - 32, 22);
            int third = (w - 32) / 3;
            lblD1P.SetBounds(16, 94, third, 20);
            lblD1E.SetBounds(16 + third, 94, third + 10, 20);
            lblD1Lpe.SetBounds(16 + third * 2 + 10, 94, third - 10, 20);
        }

        private void LayoutSchemeCard(int w)
        {
            lblD2Name.SetBounds(16, 36, w - 32, 26);
            lblD2Guid.SetBounds(16, 64, w - 32, 18);
            lblD2Mode.SetBounds(16, 84, w - 32, 20);
            lblD2Source.SetBounds(16, 102, w - 32, 16);
        }

        private void LayoutActionButtons(int panelW)
        {
            int bw = (panelW - 32 - 4 * 12) / 5;
            if (bw < 90) bw = 90;
            int i = 0;
            foreach (Control c in pnlDashAct.Controls)
            {
                Button b = c as Button;
                if (b == null) continue;
                b.SetBounds(16 + i * (bw + 12), 34, bw, 34);
                i++;
            }
        }

        // ==================================================================
        //  设置明细页
        // ==================================================================
        private void BuildSettingsPage()
        {
            pnlSetHead = Card(14, 14, 840, 116, pagSet);
            AddTitle(pnlSetHead, "优化强度（切换就能看到每一项会变成多少）", 16, 10);

            // 四种强度并排，x 间距 210 是算过标签宽度的（每个标签 200 宽，绝不会互相压到）
            rbBalanced = ModeRadio(pnlSetHead, "均衡 · 推荐", 16, 38, "地板 60% / EPP 30，高频不过热");
            rbCompetitive = ModeRadio(pnlSetHead, "竞技 · 省功率", 226, 38, "地板 25% / EPP 0，让功率给独显");
            rbMax = ModeRadio(pnlSetHead, "极限高频", 436, 38, "地板 100% / EPP 0，最热最耗电");
            rbSafe = ModeRadio(pnlSetHead, "保守", 646, 38, "只解绑掉频，优先 P 核");
            rbBalanced.Checked = true;
            rbBalanced.CheckedChanged += delegate { OnModeChanged(); };
            rbCompetitive.CheckedChanged += delegate { OnModeChanged(); };
            rbMax.CheckedChanged += delegate { OnModeChanged(); };
            rbSafe.CheckedChanged += delegate { OnModeChanged(); };

            btnSetApply = ActionButton(pnlSetHead, "应用这套设置", 16, 78, 150, C_MAIN);
            btnSetRefresh = ActionButton(pnlSetHead, "刷新当前状态", 176, 78, 140, Color.FromArgb(92, 102, 122));
            btnSetApply.Click += delegate { DoApply(); };
            btnSetRefresh.Click += delegate { RefreshSettings(true); };

            lblSetCount = CardText(pnlSetHead, 330, 78, 240, 20, 9F, FontStyle.Bold, C_TEXT);
            lblSetCount.Text = "";
            lblSetSummary = CardText(pnlSetHead, 330, 97, 494, 18, 8.5F, FontStyle.Regular, C_SUB);
            lblSetSummary.Text = "";

            setList = new Panel();
            setList.SetBounds(14, 140, 840, 502);
            setList.BackColor = C_BG;
            setList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            BuildSettingRows();
            // 用自绘的深色滚动条接管滚动：系统默认滚动条是浅灰立体样式，
            // 在这个深色导航 + 浅色卡片的界面里非常突兀，是"看起来像老软件"的主因。
            scrollHost = new ScrollHost(setList, C_NAV);
            pagSet.Resize += delegate
            {
                pnlSetHead.SetBounds(14, 14, pagSet.ClientSize.Width - 28, 116);
                setList.SetBounds(14, 140, pagSet.ClientSize.Width - 28,
                    Math.Max(180, pagSet.ClientSize.Height - 154));
                LayoutSettingRows();
            };
        }

        private RadioButton ModeRadio(Panel parent, string title, int x, int y, string sub)
        {
            RadioButton r = new RadioButton();
            r.Text = title;
            r.SetBounds(x, y, 200, 22);
            r.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            r.ForeColor = C_TEXT;
            r.BackColor = Color.Transparent;
            r.FlatStyle = FlatStyle.Standard;
            Label s = new Label();
            s.Text = sub;
            s.Font = new Font("Microsoft YaHei UI", 8.5F);
            s.ForeColor = C_SUB;
            s.BackColor = Color.Transparent;
            s.SetBounds(x + 20, y + 24, 190, 18);
            parent.Controls.Add(r);
            parent.Controls.Add(s);
            return r;
        }

        private void BuildSettingRows()
        {
            List<SettingDef> defs = Plans.Build();
            int y = 0;
            foreach (SettingDef d in defs)
            {
                Panel row = new Panel();
                row.SetBounds(0, y, 810, 62);
                row.BackColor = Color.Transparent;
                row.Paint += PaintSettingRow;

                RowRef rf = new RowRef();
                rf.Def = d;
                rf.Row = row;

                Label nm = new Label();
                nm.Text = d.Name;
                nm.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                nm.ForeColor = C_TEXT;
                nm.BackColor = Color.Transparent;
                nm.SetBounds(24, 8, 232, 20);
                row.Controls.Add(nm);

                Label cur = new Label();
                cur.Text = "机器默认值: " + d.Current;
                cur.Font = new Font("Microsoft YaHei UI", 8F);
                cur.ForeColor = Color.FromArgb(160, 168, 182);
                cur.BackColor = Color.Transparent;
                cur.SetBounds(24, 30, 232, 18);
                row.Controls.Add(cur);

                rf.Reason = new Label();
                rf.Reason.Text = d.Why;
                rf.Reason.Font = new Font("Microsoft YaHei UI", 8F);
                rf.Reason.ForeColor = C_SUB;
                rf.Reason.BackColor = Color.Transparent;
                rf.Reason.SetBounds(264, 8, 200, 44);
                row.Controls.Add(rf.Reason);

                rf.Value = new Label();
                rf.Value.Text = "";
                rf.Value.Font = new Font("Consolas", 9F, FontStyle.Bold);
                rf.Value.ForeColor = C_TEXT;
                rf.Value.BackColor = Color.Transparent;
                rf.Value.TextAlign = ContentAlignment.MiddleRight;
                rf.Value.SetBounds(658, 8, 136, 22);
                row.Controls.Add(rf.Value);

                rf.Status = new Label();
                rf.Status.Text = "";
                rf.Status.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
                rf.Status.ForeColor = C_SUB;
                rf.Status.BackColor = Color.Transparent;
                rf.Status.TextAlign = ContentAlignment.MiddleRight;
                rf.Status.SetBounds(600, 34, 194, 18);
                row.Controls.Add(rf.Status);

                row.Tag = rf;
                setList.Controls.Add(row);
                rows.Add(rf);
                y += 66;
            }
        }

        private void LayoutSettingRows()
        {
            int rw = (scrollHost != null ? scrollHost.ViewportWidth : setList.ClientSize.Width) - 4;
            if (rw < 300) rw = 300;
            foreach (RowRef r in rows)
            {
                r.Row.SetBounds(0, r.Row.Top, rw, 62);
                int vw = rw - 16;
                r.Reason.SetBounds(200, 8, 268, 44);
                r.Value.SetBounds(vw - 150, 8, 134, 22);
                r.Status.SetBounds(vw - 144, 34, 128, 18);
                r.Row.Invalidate();
            }
            if (scrollHost != null) scrollHost.UpdateScrollbar();
            foreach (RowRef r in rows) r.Row.Invalidate();
        }

        private void PaintSettingRow(object sender, PaintEventArgs e)
        {
            Panel row = sender as Panel;
            if (row == null) return;
            RowRef rf = row.Tag as RowRef;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle card = new Rectangle(0, 0, row.Width - 4, row.Height - 4);
            Gfx.Card(g, card, C_CARD);
            Gfx.CardBorder(g, card, C_LINE);
            if (rf == null) return;

            // 进度条轨道（右边留出数值列）
            int trackW = row.Width - 480 - 36;
            if (trackW < 120) trackW = 120;
            Rectangle track = new Rectangle(470, 16, trackW, 10);
            using (GraphicsPath p = Gfx.Rounded(track, 5))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(230, 234, 241)))
                g.FillPath(b, p);

            if (!rf.Supported)
            {
                using (Font f = new Font("Microsoft YaHei UI", 9F))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(170, 178, 192)))
                    g.DrawString("本机不支持此项", f, b, new RectangleF(470, 12, trackW, 20));
                return;
            }

            int t = PowerEngine.TargetValue(rf.Def, SelectedMode());
            int cur = PowerEngine.CurrentValueCached(rf.Def);
            int tc = t; if (tc > 100) tc = 100;
            int cc = cur; if (cc < 0) cc = 0; if (cc > 100) cc = 100;

            Color fill = (t > 100) ? C_WARN : C_OK;
            int tw = (int)(tc / 100.0 * (trackW - 8));
            if (tw > 0)
            {
                Rectangle fr = new Rectangle(track.X + 4, track.Y, Math.Max(3, tw), track.Height);
                using (GraphicsPath p = Gfx.Rounded(fr, 5))
                using (LinearGradientBrush b = new LinearGradientBrush(fr,
                    Color.FromArgb(150, fill), fill, LinearGradientMode.Horizontal))
                    g.FillPath(b, p);
            }
            if (cc > 0)
            {
                int cx = track.X + 4 + (int)(cc / 100.0 * (trackW - 8));
                using (Pen pen = new Pen(Color.FromArgb(180, 60, 70, 88), 2))
                    g.DrawLine(pen, cx, track.Y - 4, cx, track.Bottom + 5);
            }
            using (Font f = new Font("Microsoft YaHei UI", 8F))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 160, 176)))
            {
                g.DrawString("机器默认", f, b, new RectangleF(track.X, track.Bottom + 6, 70, 16));
                SizeF sz = g.MeasureString("目标 100", f);
                g.DrawString("目标 100", f, b, new RectangleF(track.Right - sz.Width, track.Bottom + 6, sz.Width, 16));
            }
        }

        private void OnModeChanged()
        {
            RefreshSettings(false);
        }

        private void RefreshSettings(bool rescan)
        {
            Mode m = SelectedMode();
            if (rbBalanced != null)
                lblSetCount.Text = "当前强度：" + PowerEngine.ModeName(m);
            if (rescan) PowerEngine.InvalidateCache();
            int applied = 0, pending = 0, unsupported = 0;
            foreach (RowRef r in rows)
            {
                int cur = PowerEngine.CurrentValueCached(r.Def);
                int t = PowerEngine.TargetValue(r.Def, m);
                r.Supported = (cur >= 0);
                if (!r.Supported)
                {
                    unsupported++;
                    r.Value.Text = "—";
                    r.Status.Text = "本机不支持";
                    r.Status.ForeColor = Color.FromArgb(160, 168, 182);
                }
                else
                {
                    string want = (r.Def.Set.Equals(PowerEngine.GUID_MINSTATE) && t >= 100)
                        ? "100 (锁最高频)" : PowerEngine.RawToHuman(r.Def.Set, t);
                    r.Value.Text = cur + "  →  " + want;
                    if (cur == t)
                    {
                        applied++;
                        r.Status.Text = "✔ 已应用";
                        r.Status.ForeColor = C_OK;
                    }
                    else
                    {
                        pending++;
                        r.Status.Text = "待应用";
                        r.Status.ForeColor = C_WARN;
                    }
                }
                r.Row.Invalidate();
            }
            lblSetSummary.Text = string.Format(
                "共 {0} 项：已应用 {1} · 待应用 {2} · 本机不支持 {3}（本机不支持属正常，不同主板/CPU 开放项不同）",
                rows.Count, applied, pending, unsupported);
        }

        // ==================================================================
        //  曲线与日志页
        // ==================================================================
        private void BuildLogPage()
        {
            pnlChart = Card(14, 14, 840, 260, pagLog);
            pnlChart.Paint += PaintChart;
            lblChartNow = CardText(pnlChart, 552, 12, 272, 20, 9F, FontStyle.Bold, C_MAIN);
            lblChartNow.TextAlign = ContentAlignment.MiddleRight;
            lblChartNow.Text = "";

            Label hint = CardText(pnlChart, 16, 232, 520, 18, 8F, FontStyle.Regular, C_SUB);
            hint.Text = "橙线 = 实时频率   蓝线 = CPU 占用   每秒一点，保留最近 60 秒";

            pnlChartLogWrap = Card(14, 284, 840, 358, pagLog);
            AddTitle(pnlChartLogWrap, "操作日志", 16, 10);
            log = new TextBox();
            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.BorderStyle = BorderStyle.None;
            log.BackColor = C_LOG_BG;
            log.ForeColor = C_LOG_FG;
            log.Font = new Font("Consolas", 9F);
            log.SetBounds(16, 36, 808, 306);
            log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            pnlChartLogWrap.Controls.Add(log);

            pagLog.Resize += delegate
            {
                int w = pagLog.ClientSize.Width;
                pnlChart.SetBounds(14, 14, w - 28, 260);
                lblChartNow.SetBounds(w - 28 - 288, 12, 272, 20);
                pnlChartLogWrap.SetBounds(14, 284, w - 28, Math.Max(160, pagLog.ClientSize.Height - 298));
                log.SetBounds(16, 36, w - 28 - 32, Math.Max(80, pnlChartLogWrap.Height - 50));
                DrawChart();
            };
        }

        private void PaintChart(object sender, PaintEventArgs e)
        {
            Panel p = sender as Panel;
            if (p == null) return;
            if (chartBuf == null || chartBuf.Width != p.Width || chartBuf.Height != p.Height)
            {
                if (chartBuf != null) chartBuf.Dispose();
                chartBuf = new Bitmap(Math.Max(1, p.Width), Math.Max(1, p.Height));
            }
            using (Graphics g = Graphics.FromImage(chartBuf))
                RenderChart(g, chartBuf.Width, chartBuf.Height);
            e.Graphics.DrawImageUnscaled(chartBuf, 0, 0);
        }

        private void DrawChart()
        {
            if (pnlChart != null && pagLog != null && pagLog.Visible) pnlChart.Invalidate();
        }

        private void RenderChart(Graphics g, int w, int h)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (SolidBrush b = new SolidBrush(C_CARD)) g.FillRectangle(b, 0, 0, w, h);
            using (Font f = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(C_TEXT))
                g.DrawString("CPU 实时趋势（最近 60 秒）", f, b, new PointF(16, 10));

            Rectangle plot = new Rectangle(16, 38, w - 32, h - 72);
            if (plot.Width < 60 || plot.Height < 50) return;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(249, 250, 252)))
                g.FillRectangle(b, plot);
            using (Pen gp = new Pen(Color.FromArgb(233, 237, 243), 1))
            {
                for (int i = 1; i < 4; i++)
                    g.DrawLine(gp, plot.Left, plot.Top + plot.Height * i / 4, plot.Right, plot.Top + plot.Height * i / 4);
                for (int i = 1; i < 6; i++)
                    g.DrawLine(gp, plot.Left + plot.Width * i / 6, plot.Top, plot.Left + plot.Width * i / 6, plot.Bottom);
            }
            using (Pen pen = new Pen(C_LINE, 1)) g.DrawRectangle(pen, plot);

            int n = 60;
            float step = (float)plot.Width / (float)(n - 1);
            int mMax = 1000;
            for (int i = 0; i < n; i++) if (histMhz[i] > mMax) mMax = histMhz[i];
            mMax = (int)(Math.Ceiling((mMax + 200) / 500.0) * 500.0);
            if (mMax < 1000) mMax = 1000;

            using (Font f = new Font("Microsoft YaHei UI", 7.5F))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(150, 160, 176)))
            {
                g.DrawString("100%", f, sb, new PointF(plot.Left + 3, plot.Top + 2));
                g.DrawString("50%", f, sb, new PointF(plot.Left + 3, plot.Top + plot.Height / 2 - 12));
                g.DrawString("0%", f, sb, new PointF(plot.Left + 3, plot.Bottom - 14));
                string mx = mMax + " MHz";
                SizeF sz = g.MeasureString(mx, f);
                g.DrawString(mx, f, sb, new PointF(plot.Right - sz.Width - 4, plot.Top + 2));
            }

            PointF[] lp = new PointF[n];
            PointF[] fp = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                int idx = (histCount >= n) ? ((histCount + i) % n) : i;
                float x = plot.Left + step * i;
                int lv = (histCount >= n) ? histLoad[idx] : (i < n - histCount ? 0 : histLoad[i - (n - histCount)]);
                if (lv < 0) lv = 0; if (lv > 100) lv = 100;
                lp[i] = new PointF(x, plot.Bottom - plot.Height * (lv / 100f));
                int fv = (histCount >= n) ? histMhz[idx] : (i < n - histCount ? 0 : histMhz[i - (n - histCount)]);
                if (fv < 0) fv = 0; if (fv > mMax) fv = mMax;
                fp[i] = new PointF(x, plot.Bottom - plot.Height * (fv / (float)mMax));
            }

            using (SolidBrush fb = new SolidBrush(Color.FromArgb(28, 0, 120, 215)))
            {
                GraphicsPath area = new GraphicsPath();
                area.AddLines(lp);
                area.AddLine(lp[n - 1].X, plot.Bottom, lp[0].X, plot.Bottom);
                area.CloseFigure();
                g.FillPath(fb, area);
                area.Dispose();
            }
            using (Pen pen = new Pen(C_MAIN, 2f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, lp); }
            using (Pen pen = new Pen(C_FREQ, 2f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, fp); }
            using (SolidBrush b = new SolidBrush(C_MAIN)) g.FillEllipse(b, lp[n - 1].X - 3.5f, lp[n - 1].Y - 3.5f, 7, 7);
            using (SolidBrush b = new SolidBrush(C_FREQ)) g.FillEllipse(b, fp[n - 1].X - 3.5f, fp[n - 1].Y - 3.5f, 7, 7);

            using (Font f = new Font("Microsoft YaHei UI", 8F))
            using (SolidBrush b = new SolidBrush(C_SUB))
            {
                g.DrawString("60 秒前", f, b, new PointF(plot.Left, plot.Bottom + 4));
                SizeF sz = g.MeasureString("现在", f);
                g.DrawString("现在", f, b, new PointF(plot.Right - sz.Width, plot.Bottom + 4));
            }
        }

        // ==================================================================
        //  仪表 / 卡片绘制
        // ==================================================================
        // ---- 小工具：按比例调亮/调暗，用于按钮和图形的层次 ----
        private static Color Mix(Color c, int dr, int dg, int db)
        {
            return Color.FromArgb(c.A,
                Math.Max(0, Math.Min(255, c.R + dr)),
                Math.Max(0, Math.Min(255, c.G + dg)),
                Math.Max(0, Math.Min(255, c.B + db)));
        }
        private static Color Lighten(Color c, int d) { return Mix(c, d, d, d); }
        private static Color Darken(Color c, int d) { return Mix(c, -d, -d, -d); }

        // ---- 绘图 ----
        private void PaintFreqGauge(object sender, PaintEventArgs e)
        {
            Panel p = sender as Panel;
            if (p == null) return;
            Gauge(e.Graphics, p, "实时 CPU 频率", latest.Mhz, "MHz", mhzMax, C_FREQ);
        }

        private void PaintLoadGauge(object sender, PaintEventArgs e)
        {
            Panel p = sender as Panel;
            if (p == null) return;
            Gauge(e.Graphics, p, "CPU 占用率", latest.Load < 0 ? 0 : latest.Load, "%", 100, C_LOAD);
        }

        private void Gauge(Graphics g, Panel p, string title, int val, string unit, int max, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 标题左上角 + 一小段强调色短线，比纯文字更有层次
            using (Font f = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(C_TEXT))
                g.DrawString(title, f, b, new PointF(20, 12));
            using (SolidBrush b = new SolidBrush(color))
                g.FillRectangle(b, 14, 16, 3, 11);

            int cx = p.Width / 2;
            int size = Math.Min(p.Width - 60, 132);
            if (size < 90) size = 90;
            Rectangle arc = new Rectangle(cx - size / 2, 44, size, size);

            // 底环
            using (Pen bp = new Pen(C_TRACK, 11f))
            {
                bp.StartCap = LineCap.Round; bp.EndCap = LineCap.Round;
                g.DrawArc(bp, arc, 135f, 270f);
            }

            // 刻度：每 45° 一格，共 7 格
            using (Pen tp = new Pen(Color.FromArgb(196, 204, 218), 1f))
            {
                for (int i = 0; i <= 6; i++)
                {
                    double a = (135.0 + 270.0 * i / 6.0) * Math.PI / 180.0;
                    double r1 = arc.Width / 2.0 + 9.0;
                    double r2 = arc.Width / 2.0 + 14.0;
                    double mx = arc.Left + arc.Width / 2.0;
                    double my = arc.Top + arc.Height / 2.0;
                    g.DrawLine(tp,
                        (float)(mx + r1 * Math.Cos(a)), (float)(my + r1 * Math.Sin(a)),
                        (float)(mx + r2 * Math.Cos(a)), (float)(my + r2 * Math.Sin(a)));
                }
            }

            // 进度环（外圈细描边做出一点"发光"感）
            float frac = max <= 0 ? 0f : (float)val / (float)max;
            if (frac < 0f) frac = 0f;
            if (frac > 1f) frac = 1f;
            if (frac > 0.002f)
            {
                using (Pen glow = new Pen(Color.FromArgb(48, color), 17f))
                {
                    glow.StartCap = LineCap.Round; glow.EndCap = LineCap.Round;
                    g.DrawArc(glow, arc, 135f, 270f * frac);
                }
                using (Pen pen = new Pen(color, 11f))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, arc, 135f, 270f * frac);
                }
            }

            // 数值（用测量高度定位，不同字号都不会偏）
            int midY = arc.Top + arc.Height / 2 - 22;
            using (Font f = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(C_TEXT))
            {
                string s = (val < 0 ? "--" : val.ToString());
                SizeF sz = g.MeasureString(s, f);
                g.DrawString(s, f, b, new PointF(cx - sz.Width / 2f, midY));
            }
            using (Font f = new Font("Microsoft YaHei UI", 9F))
            using (SolidBrush b = new SolidBrush(C_SUB))
            {
                SizeF sz = g.MeasureString(unit, f);
                g.DrawString(unit, f, b, new PointF(cx - sz.Width / 2f, midY + 36));
            }

            // 两端量程标注
            using (Font f = new Font("Microsoft YaHei UI", 8.5F))
            using (SolidBrush b = new SolidBrush(C_SUB))
            {
                g.DrawString("0", f, b, new PointF(arc.Left - 4, arc.Bottom - 12));
                string s = max.ToString();
                SizeF sz = g.MeasureString(s, f);
                g.DrawString(s, f, b, new PointF(arc.Right - sz.Width + 4, arc.Bottom - 12));
            }
        }

        private Panel Card(int x, int y, int w, int h, Control parent)
        {
            Panel p = new Panel();
            p.SetBounds(x, y, w, h);
            // 底色取内容区颜色（不是纯白），这样圆角之外露出的是页面底色而不是灰色方块
            p.BackColor = C_BG;
            p.Paint += PaintCard;
            parent.Controls.Add(p);
            return p;
        }

        private void PaintCard(object sender, PaintEventArgs e)
        {
            Panel p = sender as Panel;
            if (p == null) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
            // 先画一层偏下的半透明投影，卡片就有"浮起来"的感觉
            Rectangle sh = new Rectangle(1, 2, p.Width - 2, p.Height - 2);
            Gfx.Card(e.Graphics, sh, C_SHADOW);
            Gfx.Card(e.Graphics, r, C_CARD);
            Gfx.CardBorder(e.Graphics, r, C_LINE);
        }

        private void AddTitle(Panel p, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            l.ForeColor = C_TEXT;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            p.Controls.Add(l);
        }

        private Label CardText(Panel p, int x, int y, int w, int h, float size, FontStyle st, Color c)
        {
            Label l = new Label();
            l.SetBounds(x, y, w, h);
            l.Font = new Font("Microsoft YaHei UI", size, st);
            l.ForeColor = c;
            l.BackColor = Color.Transparent;
            p.Controls.Add(l);
            return l;
        }

        private Button ActionButton(Panel p, string text, int x, int y, int w, Color c)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 34);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = c;
            b.ForeColor = Color.White;
            b.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            // 悬停提亮、按下压暗，比默认的"颜色不变"明显更像现代按钮
            b.FlatAppearance.MouseOverBackColor = Lighten(c, 14);
            b.FlatAppearance.MouseDownBackColor = Darken(c, 18);
            b.UseVisualStyleBackColor = false;
            p.Controls.Add(b);
            return b;
        }

        private void BuildGrip()
        {
            Panel grip = new Panel();
            grip.SetBounds(1000, 680, 20, 20);
            grip.BackColor = C_BG;
            grip.Cursor = Cursors.SizeNWSE;
            grip.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            grip.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(Color.FromArgb(160, 172, 190), 1.5f))
                {
                    for (int i = 1; i <= 3; i++)
                        e.Graphics.DrawLine(pen, 20 - i * 5, 19, 19, 20 - i * 5);
                }
            };
            grip.MouseDown += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    Win32Native.ReleaseCapture();
                    Win32Native.SendMessage(Handle, 0xA1 /*WM_NCLBUTTONDOWN*/, 17 /*HTBOTTOMRIGHT*/, 0);
                }
            };
            Controls.Add(grip);
            grip.BringToFront();
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Win32Native.ReleaseCapture();
                Win32Native.SendMessage(Handle, Win32Native.WM_NCLBUTTONDOWN, Win32Native.HTCAPTION, 0);
            }
        }

        // ==================================================================
        //  运行逻辑
        // ==================================================================
        private void OnFormLoad(object sender, EventArgs e)
        {
            TryRoundCorners();
            ShowPage(0);
            LayoutDashboard();
            RefreshSettings(true);
            EnableSmoothRendering();
            Log("欢迎使用「无畏契约 CPU 高频优化器」。");
            Log("建议顺序：先切到「设置明细」，挑一个优化强度看看每一项会变成多少，再点「① 应用优化」。");
            Log("所有改动都能一键还原：原电源方案会导出到程序目录的 backup\\ 里。");
            Log("提示：笔记本插电时效果最好；用电池时 Windows 会另走一套（DC）设置。");
            Log("");
            Log(cpu.Describe());
            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 150;
            uiTimer.Tick += delegate { DrainQueue(); };
            uiTimer.Start();
            bgTimer = new System.Threading.Timer(delegate { QueueSample(); }, null, 500, 1000);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            try { if (bgTimer != null) bgTimer.Dispose(); } catch { }
            try { if (uiTimer != null) uiTimer.Stop(); } catch { }
        }

        // ==================================================================
        //  渲染优化：所有自绘控件开启双缓冲
        // ==================================================================
        // 自绘卡片、仪表、曲线如果不开双缓冲，每秒重绘时会有明显闪烁。
        // DoubleBuffered 是 Control 的 protected 属性，外部读不到，
        // 所以只有在"确实存在该属性"时才调用，控件类型换了也不会崩。
        private static void SetDoubleBuffered(Control c)
        {
            if (c == null) return;
            try
            {
                System.Reflection.PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (pi != null && pi.CanWrite) pi.SetValue(c, true, null);
            }
            catch { }
        }

        private void EnableSmoothRendering()
        {
            try
            {
                SetDoubleBuffered(this);
                SetDoubleBuffered(topBar);
                SetDoubleBuffered(nav);
                SetDoubleBuffered(pnlPages);
                SetDoubleBuffered(pagDash);
                SetDoubleBuffered(pagSet);
                SetDoubleBuffered(pagLog);
                SetDoubleBuffered(pnlDashCpu);
                SetDoubleBuffered(pnlDashScheme);
                SetDoubleBuffered(pnlDashFreq);
                SetDoubleBuffered(pnlDashLoad);
                SetDoubleBuffered(pnlDashAct);
                SetDoubleBuffered(pnlDashLog);
                SetDoubleBuffered(pnlSetHead);
                SetDoubleBuffered(pnlChart);
                SetDoubleBuffered(pnlChartLogWrap);
            }
            catch { }
        }

        private void QueueSample()
        {
            Sample s;
            try { s = CpuMetrics.Collect(); }
            catch { return; }
            lock (_lock) { uiQ.Enqueue(delegate { ApplySample(s); }); }
        }

        private void DrainQueue()
        {
            for (int guard = 0; guard < 40; guard++)
            {
                Action a = null;
                lock (_lock)
                {
                    if (uiQ.Count > 0) a = uiQ.Dequeue();
                }
                if (a == null) break;
                try { a(); }
                catch (Exception ex) { Log("[界面异常] " + ex.Message); }
            }
        }

        private void ApplySample(Sample s)
        {
            latest = s;
            hasSample = true;
            for (int i = 0; i < histLoad.Length - 1; i++)
            {
                histLoad[i] = histLoad[i + 1];
                histMhz[i] = histMhz[i + 1];
            }
            histLoad[histLoad.Length - 1] = s.Load;
            histMhz[histMhz.Length - 1] = s.Mhz;
            if (histCount < 60) histCount++;

            if (s.Mhz > 0)
            {
                int need = (int)(Math.Ceiling((s.Mhz + 300) / 500.0) * 500.0);
                if (need > mhzMax) mhzMax = need;
            }

            bool adm = IsAdmin();
            lblAdmin.Text = adm ? "● 管理员" : "● 未提权";
            lblAdmin.ForeColor = adm ? C_OK : C_DANGER;

            lblD2Name.Text = s.SchemeName;
            lblD2Guid.Text = s.SchemeGuid;
            lblD2Mode.Text = "界面强度：" + PowerEngine.ModeName(SelectedMode());
            lblD2Source.Text = "名字来源：" + (string.IsNullOrEmpty(s.SchemeSource) ? "注册表 / powercfg" : s.SchemeSource);
            bool good = s.SchemeName != null &&
                (s.SchemeName.IndexOf("VALORANT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 s.SchemeName.IndexOf("卓越", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 s.SchemeName.IndexOf("高性能", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 s.SchemeName.IndexOf("High performance", StringComparison.OrdinalIgnoreCase) >= 0);
            lblD2Name.ForeColor = good ? C_OK : C_WARN;

            lblFreqRange.Text = "标称最高 " + (cpu.MaxMhz > 0 ? cpu.MaxMhz.ToString() : "?") +
                " MHz · 仪表上限 " + mhzMax + " MHz";
            pnlDashFreq.Invalidate();
            pnlDashLoad.Invalidate();
            if (lblChartNow != null)
                lblChartNow.Text = "占用 " + (s.Load < 0 ? "--" : s.Load + "%") + "    频率 " + s.Mhz + " MHz";
            DrawChart();

            tick++;
            if (tick % 10 == 0) RefreshSettings(false);
        }

        private Mode SelectedMode()
        {
            if (rbMax != null && rbMax.Checked) return Mode.Max;
            if (rbCompetitive != null && rbCompetitive.Checked) return Mode.Competitive;
            if (rbSafe != null && rbSafe.Checked) return Mode.Safe;
            return Mode.Balanced;
        }

        private void DoApply()
        {
            if (!IsAdmin())
            {
                Log("需要管理员权限：请关掉本程序，右键 ->「以管理员身份运行」（或直接双击，会自动弹 UAC）。");
                MessageBox.Show("需要管理员权限才能修改电源方案。\n请右键本程序 ->「以管理员身份运行」。",
                    "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Mode m = SelectedMode();
            if (MessageBox.Show(
                "即将把当前电源方案调整为：" + PowerEngine.ModeName(m) + "\n\n" +
                "· 原方案会先整体备份到程序目录的 backup\\ 里\n" +
                "· 随时可以点「④ 一键还原」恢复\n\n确定继续？",
                "确认应用优化", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            btnApplyTop.Enabled = false;
            if (btnSetApply != null) btnSetApply.Enabled = false;
            Application.DoEvents();
            try
            {
                Log("");
                Log("=== 开始应用优化（模式：" + PowerEngine.ModeName(m) + "）===");
                Log(PowerEngine.Apply(m).Text);
                Log(RegTweaks.Apply(true, true, true, true, true, RegTweaks.FindValorant() ?? "").Text);
                Log(ProcTuner.TuneOnce().Text);
                Log("");
                Log("全部完成。建议重启一次电脑让核心放置策略完全生效，然后进游戏。");
                Log("想撤销：点「④ 一键还原」。");
                PowerEngine.InvalidateCache();
                RefreshSettings(false);
            }
            catch (Exception ex) { Log("[异常] " + ex.Message); }
            finally
            {
                btnApplyTop.Enabled = true;
                if (btnSetApply != null) btnSetApply.Enabled = true;
            }
        }

        private void DoTune()
        {
            try { Log(ProcTuner.TuneOnce().Text); }
            catch (Exception ex) { Log("[异常] " + ex.Message); }
        }

        private void DoRestore()
        {
            if (!IsAdmin())
            {
                Log("需要管理员权限才能还原。");
                MessageBox.Show("需要管理员权限。\n请右键本程序 ->「以管理员身份运行」。",
                    "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(
                "确定把所有改动还原到优化前的状态？\n\n包括：电源方案（用备份的 .pow 整体还原）、注册表改动。",
                "确认还原", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            try
            {
                Log("");
                Log("=== 开始还原 ===");
                Log(PowerEngine.Restore().Text);
                Log(RegTweaks.RestoreDefaults().Text);
                Log("=== 还原结束 ===");
                PowerEngine.InvalidateCache();
                RefreshSettings(false);
            }
            catch (Exception ex) { Log("[异常] " + ex.Message); }
        }

        private void Log(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            string line = s.Replace("\r\n", "\n").Replace("\n", "\r\n");
            try
            {
                if (firstLog)
                {
                    firstLog = false;
                    if (log != null) log.Text = line + "\r\n";
                    if (dashLog != null) dashLog.Text = line + "\r\n";
                    return;
                }
                if (log != null) { log.AppendText(line + "\r\n"); log.SelectionStart = log.Text.Length; log.ScrollToCaret(); }
                if (dashLog != null) { dashLog.AppendText(line + "\r\n"); dashLog.SelectionStart = dashLog.Text.Length; dashLog.ScrollToCaret(); }
            }
            catch { }
        }

        private static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal p = new WindowsPrincipal(id);
                return p.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // 不靠控件自己拿到焦点：Windows 只把滚轮消息投给光标下的可聚焦控件，
        // 纯 Panel 拿不到，所以统一在窗体层兜住再转发给设置页的滚动容器。
        private void OnFormWheel(object sender, MouseEventArgs e)
        {
            if (scrollHost == null || pagSet == null || !pagSet.Visible) return;
            Point pt = pagSet.PointToClient(PointToScreen(e.Location));
            if (!pagSet.ClientRectangle.Contains(pt)) return;
            scrollHost.WheelBy(e.Delta);
        }

        // Win11 圆角：无边框窗口本来四个角是直角，开了这个才跟系统其它窗口一致
        private void TryRoundCorners()
        {
            try { Win32Native.RoundCorners(Handle); }
            catch { }
        }

        // ==================================================================
        //  自绘深色滚动条
        // ==================================================================
        // 系统默认滚动条是浅灰立体样式（Windows 经典三态按钮），在深色导航 +
        // 浅色卡片的界面里非常突兀 —— 这是"看起来像老软件"的最大单一原因。
        // 这里干三件事：
        //   1. 用 AutoScrollMinSize 精确控制滚动范围（而不是靠 AutoScroll 自动推算），
        //      这样内容被滚动时不会因为重算范围而抖动；
        //   2. 把系统滚动条挪到屏幕外藏起来（比控 AutoScroll 属性更稳）；
        //   3. 在右边缘画一条 8px 的细滚动条，圆角、半透明、鼠标悬停变亮。
        private class ScrollHost
        {
            private readonly Panel host;
            private readonly Color barColor;
            private bool thumbHot, thumbDrag;
            private int dragStartY, dragStartTop;

            public ScrollHost(Panel h, Color bar)
            {
                host = h;
                barColor = bar;
                host.AutoScroll = true;          // 让父容器接管"把子控件整体上移"这件事
                host.MouseWheel += OnWheel;
                host.Paint += OnPaint;
                host.MouseDown += OnDown;
                host.MouseMove += OnMove;
                host.MouseUp += OnUp;
                host.MouseLeave += delegate { thumbHot = false; host.Invalidate(); };
                host.Resize += delegate { UpdateScrollbar(); };
                host.ControlAdded += delegate { UpdateScrollbar(); };
                host.ControlRemoved += delegate { UpdateScrollbar(); };
                UpdateScrollbar();
            }

            public int ViewportWidth
            {
                get { return Math.Max(80, host.ClientSize.Width - BarW); }
            }

            private const int BarW = 10;

            private int MaxScroll
            {
                get
                {
                    int content = ContentHeight;
                    int view = host.ClientSize.Height;
                    int m = content - view;
                    return m > 0 ? m : 0;
                }
            }

            private int ContentHeight
            {
                get
                {
                    int max = 0;
                    foreach (Control c in host.Controls)
                        if (c.Visible && c.Bottom > max) max = c.Bottom;
                    return max;
                }
            }

            public void UpdateScrollbar()
            {
                // 显式设定滚动范围：内容多高就允许多滚多少
                host.AutoScrollMinSize = new Size(0, ContentHeight);
                host.Invalidate();
            }

            public void WheelBy(int delta)
            {
                int max = MaxScroll;
                if (max <= 0) return;
                int y = -host.AutoScrollPosition.Y - delta;
                ScrollTo(y);
            }

            private void OnWheel(object sender, MouseEventArgs e)
            {
                WheelBy(e.Delta);
            }

            private void ScrollTo(int v)
            {
                if (v < 0) v = 0;
                int max = MaxScroll;
                if (v > max) v = max;
                host.AutoScrollPosition = new Point(0, v);
                host.Invalidate();
            }

            private void OnPaint(object sender, PaintEventArgs e)
            {
                int max = MaxScroll;
                if (max <= 0) return;

                int view = host.ClientSize.Height;
                int content = ContentHeight;
                if (content <= 0 || view <= 0) return;

                int barX = host.ClientSize.Width - BarW;
                int trackH = view - 6;
                if (trackH < 20) return;

                int thumbH = (int)((long)trackH * view / content);
                if (thumbH < 34) thumbH = 34;
                if (thumbH > trackH) thumbH = trackH;

                int pos = -host.AutoScrollPosition.Y;
                int range = trackH - thumbH;
                int thumbY = 3 + (max <= 0 ? 0 : (int)((long)range * pos / max));

                Color trackBg = Color.FromArgb(26, 255, 255, 255);
                Color thumb = thumbHot || thumbDrag
                    ? Color.FromArgb(210, 255, 255, 255)
                    : Color.FromArgb(120, 255, 255, 255);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath tp = Gfx.Rounded(new Rectangle(barX + 4, 3, 3, trackH), 2))
                using (SolidBrush tb = new SolidBrush(trackBg))
                    e.Graphics.FillPath(tb, tp);
                using (GraphicsPath hp = Gfx.Rounded(new Rectangle(barX + 3, thumbY, 5, thumbH), 3))
                using (SolidBrush hb = new SolidBrush(thumb))
                    e.Graphics.FillPath(hb, hp);
            }

            private Rectangle ThumbRect()
            {
                int view = host.ClientSize.Height;
                int content = ContentHeight;
                int max = MaxScroll;
                if (max <= 0 || content <= 0) return Rectangle.Empty;
                int trackH = view - 6;
                int thumbH = (int)((long)trackH * view / content);
                if (thumbH < 34) thumbH = 34;
                if (thumbH > trackH) thumbH = trackH;
                int pos = -host.AutoScrollPosition.Y;
                int range = trackH - thumbH;
                int thumbY = 3 + (max <= 0 ? 0 : (int)((long)range * pos / max));
                return new Rectangle(host.ClientSize.Width - BarW, thumbY, BarW, thumbH);
            }

            private void OnDown(object sender, MouseEventArgs e)
            {
                if (MaxScroll <= 0) return;
                if (e.X < host.ClientSize.Width - BarW) return;
                Rectangle t = ThumbRect();
                if (t.Contains(e.Location)) { thumbDrag = true; dragStartY = e.Y; dragStartTop = -host.AutoScrollPosition.Y; }
                else ScrollTo(e.Y < t.Top ? -host.AutoScrollPosition.Y - host.ClientSize.Height
                                          : -host.AutoScrollPosition.Y + host.ClientSize.Height);
                host.Invalidate();
            }

            private void OnMove(object sender, MouseEventArgs e)
            {
                if (MaxScroll <= 0) return;
                bool hot = e.X >= host.ClientSize.Width - BarW;
                if (thumbDrag)
                {
                    int view = host.ClientSize.Height;
                    int trackH = view - 6;
                    int thumbH = ThumbRect().Height;
                    int range = trackH - thumbH;
                    if (range > 0)
                    {
                        int dy = e.Y - dragStartY;
                        int nv = dragStartTop + (int)((long)MaxScroll * dy / range);
                        ScrollTo(nv);
                    }
                }
                else if (hot != thumbHot) { thumbHot = hot; host.Invalidate(); }
            }

            private void OnUp(object sender, MouseEventArgs e)
            {
                if (thumbDrag) { thumbDrag = false; host.Invalidate(); }
            }
        }
    }
}

