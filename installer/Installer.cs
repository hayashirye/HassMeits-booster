// ---------------------------------------------------------------------------
//  游戏 CPU 高频优化器 —— 单文件安装器
// ---------------------------------------------------------------------------
//  用法：
//      install.exe                 装到 %LOCALAPPDATA%\ValorantCpuBoost 并启动
//      install.exe --dir "D:\x"    指定安装目录
//      install.exe --uninstall     卸载（删任务、删快捷方式、删目录）
//      install.exe --quiet         不暂停，装完直接退出
//
//  为什么要有这个东西（设计约束，改动前先读）：
//
//  1) 「不弹 UAC」是这个安装器存在的唯一理由。
//     改电源方案 / 写 HKLM / 装计划任务都必须提权，Windows 的设计就是必须
//     有人同意一次。所以这里的做法是：**只在安装时弹一次**，之后靠一个
//     RunLevel=HighestAvailable 的计划任务拉起界面 —— 那个任务拿到的是完整
//     管理员令牌，而 schtasks /run 触发它**不弹任何东西**（实测 32ms 返回）。
//
//  2) 界面「从一启动就是管理员」，所以 Flutter 侧 engine.dart 里的
//     Process.run 直接继承管理员令牌，一行都不用改。
//     （C# 版那套「每条命令 RunHeadless 提权」在 Dart 上套不上：
//      Process.run 走 CreateProcess，不带 ShellExecute，没有 runas。）
//
//  3) 界面任务名用 ASCII（ValorantBoostUI）。schtasks 的参数要过控制台编码，
//     中文任务名在非中文代码页上会出问题；快捷方式的显示名仍用中文。
//
//  4) 这份源码必须存成 **UTF-8 带 BOM**。系统自带的 csc.exe 靠 BOM 判断编码，
//     没有 BOM 时会按系统 ANSI 代码页读，中文全变乱码。
//     （和 build.ps1 踩的是同一个坑。）
//
//  5) 目标编译器可能是 .NET Framework 自带的 csc（C# 5），所以全文只用
//     C# 5 语法：不用 $"" 插值、不用 ?. 、不用表达式体成员、不用 nameof。
//
//  6) 安装目录选 %LOCALAPPDATA% 而不是 Program Files，理由：
//     引擎 Core.cs 的 AutoStart 会把 boost-log.txt / boost-backup.txt 写在
//     「调用它的那个 exe 所在目录」，Program Files 下的写入在非提权场景会失败；
//     而 engine.dart 的候选目录里恰好有「自己所在目录」这一条，两边正好对上。
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

internal static class Installer
{
    // ── 常量 ────────────────────────────────────────────────────────────
    // ★ 这些名字的真身在 Vcb.cs 里 —— 那边同时被 uninstall.exe 编进去，
    //   所以只能有一份。这里用 const 别名转一下，下面 600 多行的调用点
    //   就一个字都不用改（C# 允许 const string 由另一个 const string 初始化）。
    private const string AppName         = Vcb.AppName;
    private const string UiExe           = Vcb.UiExe;           // Flutter 界面
    private const string EngineExe       = Vcb.EngineExe;       // C# 引擎
    private const string LauncherExe     = Vcb.LauncherExe;     // schtasks /run 的静默跳板
    private const string UninstallerExe  = Vcb.UninstallerExe;  // 独立卸载器
    private const string UiTaskName      = Vcb.UiTaskName;      // ASCII，见文件头第 3 条
    private const string WatcherTaskName = Vcb.WatcherTaskName; // 引擎 one\Core.cs:890
    private const string ShortcutName    = Vcb.ShortcutName;

    // 这两个是本文件独有的（内嵌资源名），不进 Vcb。
    private const string ResPayload     = "Payload";          // 内嵌 payload.zip
    private const string ResLauncher    = "RunUi";            // 内嵌 run-ui.exe
    private const string ResUninstaller = "Uninstaller";      // 内嵌 uninstall.exe

    private static bool _quiet;

    private static int Main(string[] args)
    {
        // ★ 不要设 Console.OutputEncoding = UTF8。
        //   控制台代码页在中文系统上是 936；把它设成 UTF8 而不改代码页，
        //   中文会全都变成乱码。让 .NET 按控制台默认编码输出才是对的
        //   （内存里的字符串本来就是 UTF-16，.NET 会正确转成 936）。

        string dir = null;
        bool uninstall = false;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--uninstall" || a == "/uninstall") uninstall = true;
                else if (a == "--quiet" || a == "/quiet" || a == "-q") _quiet = true;
                else if ((a == "--dir" || a == "/dir") && i + 1 < args.Length) dir = args[++i];
                else if (a.StartsWith("--dir=")) dir = args[i].Substring(6);
                else if (a.StartsWith("/dir="))  dir = args[i].Substring(5);
            }
        }
        catch { }

        if (dir == null || dir.Trim().Length == 0) dir = DefaultDir();
        try { dir = Path.GetFullPath(dir); } catch { }

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("   " + AppName + (uninstall ? "  —  卸载" : "  —  安装"));
        Console.WriteLine("============================================================");
        Console.WriteLine("   安装目录：" + dir);
        Console.WriteLine();

        int rc;
        if (uninstall) rc = DoUninstall(dir);
        else           rc = DoInstall(dir);

        Console.WriteLine();
        if (rc == 0)
        {
            Console.WriteLine("================ 完成 ================");
            Console.WriteLine(uninstall ? "   已经卸载干净。" : "   装好了，界面马上出来。");
            Console.WriteLine("======================================");
        }
        else
        {
            Console.WriteLine("================ 失败 ================");
            Console.WriteLine("   出错了，看上面的红字。");
            Console.WriteLine("======================================");
        }

        if (!_quiet)
        {
            Console.WriteLine();
            Console.Write("按回车键关闭这个窗口...");
            try { Console.ReadLine(); } catch { }
        }
        return rc;
    }

    // ── 安装目录 ────────────────────────────────────────────────────────
    private static string DefaultDir() { return Vcb.DefaultDir(); }

    // ── 安装 ────────────────────────────────────────────────────────────
    private static int DoInstall(string dir)
    {
        // [1/7] 先确认自己带没带 payload。忘了 /resource 编译出来的是个空壳，
        //       这一步能立刻报出来，不用等装到一半。
        Step(1, 8, "检查安装包完整性");
        Assembly asm = Assembly.GetExecutingAssembly();
        if (asm.GetManifestResourceStream(ResPayload) == null)
        {
            Fail("这个安装包是空的（没有内嵌 payload）。多半是编译时漏了 /resource:payload.zip,Payload。");
            return 1;
        }
        Say("自带安装包，完整。");

        // [2/7] 关掉正在跑的旧版本，否则文件被占用会解压失败。
        Step(2, 8, "关闭正在运行的旧版本");
        KillRunning(UiExe);
        KillRunning(EngineExe);
        Thread.Sleep(400);
        Say("旧进程已清理。");

        // [3/7] 解压
        Step(3, 8, "解压程序文件（约 27 MB，稍等几秒）");
        try
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Fail("建不了安装目录：" + ex.Message);
            return 1;
        }

        string zip = Path.Combine(Path.GetTempPath(), "vcb-payload-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (Stream s = asm.GetManifestResourceStream(ResPayload))
            using (FileStream fs = new FileStream(zip, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }
            int count = ExtractZip(zip, dir);
            Say("解压完成，" + count + " 个文件。");

            // 覆盖安装时，目录里可能留着「上个版本有、这个版本不再有」的文件。
            // 解压只负责写和覆盖，从来不删，所以那些文件会一直赖着。
            int stale = RemoveStaleFiles(zip, dir);
            if (stale > 0) Say("顺手清掉 " + stale + " 个上个版本遗留的文件。");
        }
        catch (Exception ex)
        {
            Fail("解压失败：" + ex.Message);
            return 1;
        }
        finally
        {
            try { if (File.Exists(zip)) File.Delete(zip); } catch { }
        }

        // 静默跳板：快捷方式指向它，它去 schtasks /run。
        // 单独放一个 exe 而不是直接让快捷方式调 schtasks.exe，是为了不闪黑窗。
        try
        {
            if (!WriteResource(asm, ResLauncher, Path.Combine(dir, LauncherExe)))
                Warn("跳板程序没内嵌进安装包（界面仍可从任务计划启动）。");
        }
        catch (Exception ex) { Warn("写跳板程序失败（界面仍可从任务计划启动）：" + ex.Message); }

        // 独立的卸载器。**必须落在安装目录里**，因为「设置 → 应用」里那条
        // 记录的 UninstallString 指向的就是这个路径；如果它只存在于
        // dist\ 那种地方，用户把 dist 一删，卸载入口就断了。
        // 现在安装目录里不再放 install.exe 的副本了（那是 11.4 MB 的冗余），
        // 所以这一步失败的后果比以前严重 —— 说话要更重。
        bool haveUninstaller = false;
        try
        {
            haveUninstaller = WriteResource(asm, ResUninstaller, Path.Combine(dir, UninstallerExe));
            if (!haveUninstaller)
                Warn("★ 卸载器没内嵌进安装包 —— 这个 install.exe 是旧版编译的。");
        }
        catch (Exception ex) { Warn("★ 写卸载器失败：" + ex.Message); }
        if (!haveUninstaller)
        {
            Warn("  「设置 → 应用」里将不会出现卸载按钮，也没有独立的卸载程序。");
            Warn("  要卸载请重新运行你下载的那个 install.exe 并加 --uninstall：");
            Warn("      install.exe --uninstall");
        }

        // [4/7] 让 Windows Defender 放行安装目录。
        //       ★ 这一步不是可选的，是整个设计能成立的前提：
        //         我们干的事 —— 往一个可写目录里落一个未签名的 exe，再用它注册一个
        //         「登录时触发、最高权限」的计划任务 —— 在 Defender 的行为检测里
        //         就是教科书式的「计划任务持久化」，会被判成
        //         Behavior:Win32/Persistence.A!ml（严重度 5）。
        //         实测：不加排除，装完几分钟内引擎 exe 会被丢进隔离区、
        //         ValorantBoostWatcher 任务会被删掉，用户的守护直接失效。
        //       所以这里显式、可控、可撤销地排除「只这一个目录」，并在卸载时收回。
        Step(4, 8, "让 Windows Defender 放行安装目录");
        if (AddDefenderExclusion(dir))
        {
            Say("已排除 " + dir);
            Say("（不加这一步，Defender 会把「未签名程序 + 开机自启」认成木马，");
            Say("  把守护任务和引擎一起清掉。卸载时会自动收回这条排除。）");
        }
        else
        {
            Warn("没能加上 Defender 排除项。");
            Warn("守护可能装好后被 Defender 清掉 —— 如果发现它莫名不工作，");
            Warn("请手动在「Windows 安全中心 → 病毒和威胁防护 → 排除项」里加上：");
            Warn("    " + dir);
        }

        // [5/7] 装后台守护。这一步同时干两件事：
        //       ① 把引擎复制到安装目录并注册 ValorantBoostWatcher
        //       ② 把「当前目录」作为 watch --dir 烤进任务 —— 所以日志会写在我们
        //          这个安装目录里，而 engine.dart 的候选目录里正好有这一条。
        Step(5, 8, "安装后台守护（检测到游戏自动优化）");
        string eng = Path.Combine(dir, EngineExe);
        if (!File.Exists(eng))
        {
            Warn("没找到 " + EngineExe + "，跳过守护安装。界面仍能看数据，但不会自动优化。");
        }
        else
        {
            string o;
            int rc = Run(eng, "install", out o);
            foreach (string line in Split(o)) if (line.Trim().Length > 0) Say("引擎：" + line.Trim());
            if (rc != 0) Warn("守护安装返回码 " + rc + "，可能没装上。");
            else
            {
                Say("守护已注册。");
                // ★ 引擎的 Install() 只注册任务、不启动它（唯一的触发器是「登录时」），
                //   所以不主动 run 一下的话，守护要等到下次登录才活过来 ——
                //   用户会以为装了没用。这里补上。
                //   schtasks /run 触发 HighestAvailable 任务同样不弹 UAC。
                string o2;
                int rc2 = Run("schtasks.exe", "/run /tn \"" + WatcherTaskName + "\"", out o2);
                if (rc2 == 0) Say("守护已启动，现在就在盯着游戏。");
                else Warn("守护已注册但没能立刻启动（下次登录会自动起来）。");
            }
        }

        // [6/7] 注册提权界面任务 —— 整个「不弹 UAC」就靠它。
        Step(6, 8, "注册提权启动项（以后启动界面不再弹 UAC）");
        if (!RegisterUiTask(dir))
        {
            Warn("注册失败。界面还能双击 " + UiExe + " 打开，但每次都会弹 UAC。");
        }

        // [7/8] 注册到「设置 → 应用」。
        //       写的是标准的那套 Uninstall 注册表约定，所以不需要安装任何框架，
        //       Windows 自带的卸载列表就能看到、就能点。
        //       ★ 卸载器此时已经解压到安装目录了（见上面那段），所以这里能直接引用它。
        Step(7, 8, "注册到「程序和功能」");
        if (RegisterInAppsList(dir))
        {
            Say("已注册：「设置 → 应用」里现在能看到「" + AppName + "」。");
        }
        else
        {
            Warn("没写进「程序和功能」（多半是被安全软件拦了注册表写入）。");
            Warn("不影响使用，卸载可以走开始菜单里的「卸载 " + AppName + "」。");
        }

        // [8/8] 快捷方式
        Step(8, 8, "创建快捷方式");
        string launch = Path.Combine(dir, LauncherExe);
        string target = File.Exists(launch) ? launch : Path.Combine(dir, UiExe);
        string icon   = Path.Combine(dir, UiExe) + ",0";

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        CreateShortcut(Path.Combine(desktop, ShortcutName + ".lnk"), target, "", dir, icon, AppName);
        Say("桌面快捷方式已创建。");
        if (programs != null && programs.Length > 0)
        {
            string group = Path.Combine(programs, AppName);
            try { if (!Directory.Exists(group)) Directory.CreateDirectory(group); } catch { }
            CreateShortcut(Path.Combine(group, ShortcutName + ".lnk"), target, "", dir, icon, AppName);
            // ★ 卸载快捷方式指向 uninstall.exe。
            //   老写法是 install.exe --uninstall，能用，但用户看到「卸载」按钮跳出
            //   一个叫 install 的程序会犯嘀咕；而且安装目录里现在也不再放 install.exe
            //   的副本了。万一 uninstall.exe 没写成功，就干脆不建这个快捷方式 ——
            //   注册表里的 UninstallString 同样指向 uninstall.exe，那条也一起落空，
            //   上面已经用黄字把补救办法说清楚了。
            string unExe = Path.Combine(dir, UninstallerExe);
            if (File.Exists(unExe))
                CreateShortcut(Path.Combine(group, "卸载 " + AppName + ".lnk"), unExe, "", dir, icon, "卸载");
            Say("开始菜单快捷方式已创建。");
        }

        // ★ 以前这里会把 install.exe 自己复制一份到安装目录，理由是「卸载入口才找得到」。
        //   现在不需要了：卸载入口是 uninstall.exe（13 KB，注册表 UninstallString 和
        //   开始菜单快捷方式都指向它），而 install.exe 带着内嵌的 payload.zip，
        //   是 11.4 MB —— 放在一个 27 MB 的安装目录里正好是 42% 的纯冗余。
        //   要重装/修复，用当初下载的那个 install.exe 就行，它一样能跑：
        //       install.exe --uninstall        （卸载）
        //       install.exe                    （覆盖重装，顺便修复被安全软件吃掉的文件）

        // 启动界面
        Console.WriteLine();
        Say("正在启动界面...");
        LaunchUi(target, dir);
        Thread.Sleep(1200);
        return 0;
    }

    // ── 卸载 ────────────────────────────────────────────────────────────
    // ── 卸载 ────────────────────────────────────────────────────────────
    //  ★ 逻辑已经搬到 Vcb.cs 了 —— 独立的 uninstall.exe 也要走同一条路，
    //    两边各留一份的话迟早会分叉（比如「收回 Defender 排除项」只在一边修了）。
    //    这里保留 --uninstall 这个入口，是因为老版本的开始菜单快捷方式、
    //    以及用户手写的脚本里用的都是它，不能因为出了 uninstall.exe 就把它拆了。
    private static int DoUninstall(string dir) { return Vcb.DoUninstall(dir); }

    // ── 提权界面任务 ────────────────────────────────────────────────────
    //  这是整个方案的核心。要点：
    //    · RunLevel=HighestAvailable  →  任务里的进程拿到完整管理员令牌
    //    · LogonType=InteractiveToken  →  窗口出现在当前用户的桌面上
    //    · 没有 Trigger               →  只能按需启动（schtasks /run）
    //    · ExecutionTimeLimit=PT0S    →  不设时限，界面可以一直开着
    //  走过的弯路：`schtasks /create /rl highest` 命令行开关建出来的任务，
    //  DisallowStartIfOnBatteries / StopIfGoingOnBatteries 默认是 true，
    //  笔记本一拔电源 Windows 会直接杀掉进程 —— 这两个值只有 XML 能设。
    private static bool RegisterUiTask(string dir)
    {
        string exe = Path.Combine(dir, UiExe);
        if (!File.Exists(exe)) { Fail("找不到 " + exe + "，没法注册启动项。"); return false; }

        string me = "";
        try { me = System.Security.Principal.WindowsIdentity.GetCurrent().Name; }
        catch { }
        if (me == null || me.Length == 0) me = Environment.UserDomainName + "\\" + Environment.UserName;

        string xml = ""
            + "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
            + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n"
            + "  <RegistrationInfo>\r\n"
            + "    <Description>" + X(AppName) + " 界面。以管理员身份运行，这样调电源方案和写注册表都不会再弹 UAC。</Description>\r\n"
            + "  </RegistrationInfo>\r\n"
            + "  <Triggers />\r\n"
            + "  <Principals>\r\n"
            + "    <Principal id=\"Author\">\r\n"
            + "      <UserId>" + X(me) + "</UserId>\r\n"
            + "      <LogonType>InteractiveToken</LogonType>\r\n"
            + "      <RunLevel>HighestAvailable</RunLevel>\r\n"
            + "    </Principal>\r\n"
            + "  </Principals>\r\n"
            + "  <Settings>\r\n"
            + "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n"
            + "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n"
            + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n"
            + "    <AllowHardTerminate>false</AllowHardTerminate>\r\n"
            + "    <StartWhenAvailable>false</StartWhenAvailable>\r\n"
            + "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n"
            + "    <IdleSettings>\r\n"
            + "      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n"
            + "      <RestartOnIdle>false</RestartOnIdle>\r\n"
            + "    </IdleSettings>\r\n"
            + "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n"
            + "    <Enabled>true</Enabled>\r\n"
            + "    <Hidden>false</Hidden>\r\n"
            + "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n"
            + "    <WakeToRun>false</WakeToRun>\r\n"
            + "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n"
            + "    <Priority>5</Priority>\r\n"
            + "  </Settings>\r\n"
            + "  <Actions Context=\"Author\">\r\n"
            + "    <Exec>\r\n"
            + "      <Command>" + X(exe) + "</Command>\r\n"
            + "      <WorkingDirectory>" + X(dir) + "</WorkingDirectory>\r\n"
            + "    </Exec>\r\n"
            + "  </Actions>\r\n"
            + "</Task>\r\n";

        string tmp = Path.Combine(Path.GetTempPath(), "vcb-ui-task.xml");
        try
        {
            // 计划任务 XML 必须是 UTF-16 且带 BOM，schtasks 才认。
            File.WriteAllText(tmp, xml, new UnicodeEncoding(false, true));
        }
        catch (Exception ex) { Fail("写任务 XML 失败：" + ex.Message); return false; }

        string o;
        int rc = Run("schtasks.exe", "/create /tn \"" + UiTaskName + "\" /xml \"" + tmp + "\" /f", out o);
        try { File.Delete(tmp); } catch { }
        if (rc != 0)
        {
            foreach (string line in Split(o)) if (line.Trim().Length > 0) Say("schtasks：" + line.Trim());
            return false;
        }
        Say("已注册：" + UiTaskName + "（管理员身份、零 UAC）。");
        return true;
    }

    // ── Windows Defender 排除项 ─────────────────────────────────────────
    //  为什么非做不可：这个安装器的核心手法 —— 把未签名的 exe 落到一个可写目录，
    //  再用它注册「登录时触发、最高权限」的计划任务 —— 正是 Defender 行为检测
    //  里的「计划任务持久化」样本。实测（2026-10-01）不加排除时：
    //    16:46 解压出的引擎被判 Behavior:Win32/Persistence.A!ml（严重度 5）
    //    16:48 建守护任务的动作让引擎 exe 被隔离、ValorantBoostWatcher 被删
    //  所以安装时必须先放行，卸载时原样收回。
    //  ★ 只排除「安装目录」这一个路径，不碰 Defender 的其它任何设置。
    private static bool AddDefenderExclusion(string dir) { return Vcb.AddDefenderExclusion(dir); }

    private static bool RemoveDefenderExclusion(string dir) { return Vcb.RemoveDefenderExclusion(dir); }

    // ── 注册到「设置 → 应用」────────────────────────────────────────────
    //  写 HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ValorantCpuBoost。
    //  这是 Windows 自带的卸载列表认得的那套约定，不需要装任何框架。
    //
    //  ★ UninstallString 指向安装目录里的 uninstall.exe，不是 install.exe ——
    //    用户点「卸载」时跳出一个叫 install 的程序会让人以为点错了。
    //    install.exe 也会留在目录里（见 DoInstall 收尾），但那是备用入口。
    //  ★ NoModify / NoRepair = 1：我们没实现「修改」和「修复」，
    //    不置这两个值的话 Windows 会给那一行配上两个点了没反应的按钮。
    private static bool RegisterInAppsList(string dir)
    {
        try
        {
            using (RegistryKey k = Registry.LocalMachine.CreateSubKey(Vcb.UninstallKey))
            {
                if (k == null) return false;

                string ui = Path.Combine(dir, UiExe);
                string un = Path.Combine(dir, UninstallerExe);

                k.SetValue("DisplayName",     AppName);
                k.SetValue("DisplayVersion",  Vcb.AppVersion);
                k.SetValue("Publisher",       Vcb.AppPublisher);
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString",      "\"" + un + "\"");
                k.SetValue("QuietUninstallString", "\"" + un + "\" --quiet");
                if (File.Exists(ui)) k.SetValue("DisplayIcon", ui + ",0");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                int kb = Vcb.DirSizeKb(dir);
                if (kb > 0) k.SetValue("EstimatedSize", kb, RegistryValueKind.DWord);
            }
            return true;
        }
        catch { return false; }
    }

    // ── 启动界面 ────────────────────────────────────────────────────────
    //  优先走任务计划（提权、不弹 UAC）；任务不在就退回直接启动（会弹 UAC）。
    private static void LaunchUi(string fallbackExe, string dir)
    {
        string o;
        int rc = Run("schtasks.exe", "/run /tn \"" + UiTaskName + "\"", out o);
        if (rc == 0) { Say("已通过计划任务启动（管理员身份，没弹 UAC）。"); return; }

        Say("计划任务启动失败，直接打开界面（这次会弹 UAC）。");
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(fallbackExe);
            psi.WorkingDirectory = dir;
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch (Exception ex) { Warn("启动失败：" + ex.Message + "\r\n请直接双击 " + fallbackExe); }
    }

    // ── 快捷方式（交给 PowerShell 的 WScript.Shell，省掉 C# 里的 COM 互操作）──
    private static void CreateShortcut(string lnk, string target, string arguments,
                                       string workDir, string icon, string desc)
    {
        string ps = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + PS(lnk) + "');"
                  + "$s.TargetPath='" + PS(target) + "';"
                  + "$s.Arguments='" + PS(arguments) + "';"
                  + "$s.WorkingDirectory='" + PS(workDir) + "';"
                  + "$s.IconLocation='" + PS(icon) + "';"
                  + "$s.Description='" + PS(desc) + "';"
                  + "$s.Save()";
        string o;
        int rc = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"" + ps + "\"", out o);
        if (rc != 0) Warn("建快捷方式失败：" + lnk);
    }

    // PowerShell 单引号字符串里，单引号要写成两个。
    private static string PS(string s)
    {
        if (s == null) return "";
        return s.Replace("'", "''");
    }

    // ── 辅助 ────────────────────────────────────────────────────────────
    //  把内嵌资源原样写成一个文件。run-ui.exe 和 uninstall.exe 都走这里。
    //  返回 false 表示安装包里根本没这个资源（编译时漏了 /resource:）。
    private static bool WriteResource(Assembly asm, string resName, string dest)
    {
        using (Stream s = asm.GetManifestResourceStream(resName))
        {
            if (s == null) return false;
            using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
            {
                byte[] buf = new byte[81920];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
            }
        }
        return true;
    }

    private static int ExtractZip(string zip, string dest)
    {
        int n = 0;
        using (ZipArchive za = ZipFile.OpenRead(zip))
        {
            foreach (ZipArchiveEntry e in za.Entries)
            {
                string rel = e.FullName.Replace('/', Path.DirectorySeparatorChar);
                string target = Path.Combine(dest, rel);
                if (e.Name.Length == 0)
                {
                    if (!Directory.Exists(target)) Directory.CreateDirectory(target);
                    continue;
                }
                string parent = Path.GetDirectoryName(target);
                if (parent != null && parent.Length > 0 && !Directory.Exists(parent))
                    Directory.CreateDirectory(parent);
                e.ExtractToFile(target, true);
                n++;
            }
        }
        return n;
    }

    // 覆盖安装时把「上个版本有、这个版本不再有」的文件清掉。
    // ★ 实测踩到过：install.exe 以前会把自己复制一份进安装目录当卸载入口（11.4 MB），
    //   改成独立的 uninstall.exe 之后那 11.4 MB 就一直赖着 —— 覆盖安装不会删它，
    //   安装目录从 27 MB 变成 39 MB。ExtractZip 只写不删，所以必须单独走一遍。
    // ★ 保留名单：引擎在运行期自己写的两个文件，它们不在 payload 里，
    //   但删掉会让用户丢掉「还原初始设置」的还原点。
    private static int RemoveStaleFiles(string zip, string dir)
    {
        HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "boost-log.txt", "boost-backup.txt", ".wtest"
        };
        HashSet<string> want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (ZipArchive za = ZipFile.OpenRead(zip))
            foreach (ZipArchiveEntry e in za.Entries)
                want.Add(e.FullName.Replace('/', '\\'));

        int n = 0;
        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = f.Substring(dir.Length).TrimStart('\\', '/');
            if (want.Contains(rel)) continue;
            if (keep.Contains(Path.GetFileName(rel))) continue;
            try { File.Delete(f); n++; } catch { }
        }
        return n;
    }

    // ── 以下都是转调 Vcb.cs 的薄包装 ────────────────────────────────────
    //  ★ 之所以保留这一层而不是把调用点全改成 Vcb.xxx：Installer.cs 有 600 多行、
    //    上百个调用点，逐个改既容易漏又容易改错；而且这两个程序做的事本来
    //    就高度重合，包一层反而让「哪些是安装独有的」一眼看得出来。
    private static void KillRunning(string exeName) { Vcb.KillRunning(exeName); }

    private static int Run(string exe, string arg, out string output)
    {
        return Vcb.Run(exe, arg, out output);
    }

    private static string[] Split(string s)
    {
        if (s == null || s.Length == 0) return new string[0];
        return Vcb.Split(s);
    }

    private static string X(string s) { return Vcb.X(s); }

    private static void Step(int i, int n, string what) { Vcb.Step(i, n, what); }

    private static void Say(string s) { Vcb.Say(s); }

    private static void Warn(string s) { Vcb.Warn(s); }

    private static void Fail(string s) { Vcb.Fail(s); }
}
