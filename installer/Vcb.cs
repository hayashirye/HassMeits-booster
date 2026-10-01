// ---------------------------------------------------------------------------
//  游戏 CPU 高频优化器 —— 安装器 / 卸载器 共用的那一半
// ---------------------------------------------------------------------------
//  这个文件同时被编进两个程序：
//      install.exe    = Vcb.cs + Installer.cs
//      uninstall.exe  = Vcb.cs + Uninstaller.cs
//
//  所以这里只放「两边都要用」的东西：
//      · 常量（程序名、exe 名、任务名、注册表键）
//      · 控制台输出、跑命令、杀进程、目录定位
//      · Defender 排除项的加/收
//      · 注册 / 注销「程序和功能」里的条目
//      · 卸载流程 DoUninstall
//
//  设计约束（和 Installer.cs 完全一致，改动前先读）：
//
//  1) 这份源码必须存成 **UTF-8 带 BOM**。系统自带的 csc.exe 靠 BOM 判断编码，
//     没有 BOM 时会按系统 ANSI 代码页读，中文全变乱码。
//
//  2) 目标编译器可能是 .NET Framework 自带的 csc（C# 5），所以全文只用
//     C# 5 语法：不用 $"" 插值、不用 ?. 、不用表达式体成员、不用 nameof。
//
//  3) Installer.cs 里特意保留了一批同签名的私有薄包装（Say / Run / PS …），
//     它们只是转调到这里。这样 Installer.cs 那 600 多行调用点一个字都不用改，
//     两个程序也不会各自 fork 一份逻辑出来。
//
//  4) 卸载必须是**幂等**的：每一条都可能失败（任务本来就不在、排除项本来就没有、
//     exe 本来就被删了），失败只提示不中断，最后一定走到「删目录」那一步。
//     用户点卸载的意图是「别再出现在我机器上」，不是「给我看报错」。
// ---------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

internal static class Vcb
{
    // ── 常量 ────────────────────────────────────────────────────────────
    // ★ 这几个名字和 Installer.cs 里的是同一套，改一处必须改两处 —— 所以只留一份。
    internal const string AppName         = "游戏 CPU 高频优化器";
    internal const string AppVersion      = "1.0.0";
    internal const string AppPublisher    = "ValorantCpuBoost";
    internal const string AppId           = "ValorantCpuBoost";

    internal const string UiExe           = "valorant_boost.exe";   // Flutter 界面
    internal const string EngineExe       = "ValorantBoost.exe";    // C# 引擎
    internal const string LauncherExe     = "run-ui.exe";           // schtasks /run 的静默跳板
    internal const string InstallerExe    = "install.exe";
    internal const string UninstallerExe  = "uninstall.exe";

    // 界面任务名用 ASCII。schtasks 的参数要过控制台编码，中文任务名在非中文
    // 代码页上会出问题；快捷方式的显示名仍用中文。
    internal const string UiTaskName      = "ValorantBoostUI";
    // 引擎自己注册的守护任务名，写死在 one\Core.cs:890 的 AutoStart.TASK。
    // 我们只负责把它 run 起来 / 删掉，不能改它。
    internal const string WatcherTaskName = "ValorantBoostWatcher";

    internal const string ShortcutName    = "游戏 CPU 高频优化器";

    // ★ 卸载时先往安装目录放这个「哨兵」文件，延迟删除的批处理醒来时靠它确认
    //   「这个目录还是我当初要删的那个」。理由见 DoUninstall 第 7 步。
    internal const string SentinelName    = ".vcb-uninstalling";

    // 「设置 → 应用」里那一行。写在 HKLM 所以所有用户都看得见。
    internal const string UninstallKey    =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId;

    // 界面自己的设置目录（配色种子）。卸载时**刻意不删** —— 那是用户数据，
    // 不是程序文件；重装还能接着用。要清得干净，卸载完手动删即可。
    internal const string UiSettingsDir = "valorant_boost_flutter";

    // ── 控制台输出 ──────────────────────────────────────────────────────
    // ★ 不要设 Console.OutputEncoding = UTF8。控制台代码页在中文系统上是 936，
    //   把编码设成 UTF8 而不改代码页，中文会全变乱码。
    internal static void Step(int i, int n, string what)
    {
        Console.WriteLine("[" + i + "/" + n + "] " + what + " ...");
    }

    internal static void Say(string s)
    {
        Console.WriteLine("      " + s);
    }

    internal static void Warn(string s)
    {
        InColor(ConsoleColor.Yellow, "      [注意] " + s);
    }

    internal static void Fail(string s)
    {
        InColor(ConsoleColor.Red, "      [失败] " + s);
    }

    private static void InColor(ConsoleColor c, string s)
    {
        ConsoleColor old = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = c;
            Console.WriteLine(s);
        }
        finally
        {
            Console.ForegroundColor = old;
        }
    }

    // ── 跑外部命令 ──────────────────────────────────────────────────────
    internal static int Run(string exe, string arg, out string output)
    {
        output = "";
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, arg);
            psi.UseShellExecute        = false;
            psi.CreateNoWindow         = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError  = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding  = Encoding.UTF8;

            using (Process p = Process.Start(psi))
            {
                string so = p.StandardOutput.ReadToEnd();
                string se = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                output = (so + se).Trim();
                return p.HasExited ? p.ExitCode : -1;
            }
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return -1;
        }
    }

    internal static void KillRunning(string exeName)
    {
        try
        {
            string bare = Path.GetFileNameWithoutExtension(exeName);
            foreach (Process p in Process.GetProcessesByName(bare))
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    // ── 文本 ────────────────────────────────────────────────────────────
    internal static string[] Split(string s)
    {
        return s.Replace("\r\n", "\n").Split('\n');
    }

    internal static string X(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
    }

    // PowerShell 单引号串里的单引号要写两个。
    internal static string PS(string s)
    {
        return s.Replace("'", "''");
    }

    // ── 目录 ────────────────────────────────────────────────────────────
    internal static string DefaultDir()
    {
        try
        {
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (la != null && la.Length > 0) return Path.Combine(la, AppId);
        }
        catch { }
        return Path.GetTempPath();
    }

    /// <summary>
    /// 找出「装在哪」。给独立的 uninstall.exe 用 —— 它是从任意位置被双击的，
    /// 不能假设自己就在安装目录里（虽然安装时确实会放一份进去）。
    /// 顺序：自己所在目录（且里面有界面 exe）→ 注册表记的 InstallLocation → 默认位置。
    /// </summary>
    internal static string FindInstalledDir()
    {
        try
        {
            string self = Assembly.GetExecutingAssembly().Location;
            string own  = Path.GetDirectoryName(self);
            if (own != null && own.Length > 0 && File.Exists(Path.Combine(own, UiExe)))
                return own;
        }
        catch { }

        try
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(UninstallKey))
            {
                if (k != null)
                {
                    string loc = k.GetValue("InstallLocation") as string;
                    if (loc != null && loc.Length > 0 && Directory.Exists(loc)) return loc;
                }
            }
        }
        catch { }

        return DefaultDir();
    }

    // ── Windows Defender 排除项 ─────────────────────────────────────────
    // ★ 为什么安装时要加：往可写目录落一个未签名的 exe、再用它注册一个
    //   「登录时触发、最高权限」的计划任务 —— 在 Defender 的行为检测里就是
    //   教科书式的「计划任务持久化」，会被判成 Behavior:Win32/Persistence.A!ml。
    //   实测不加排除，装完几分钟内引擎 exe 就被丢进隔离区。
    //   卸载时**必须对称地收回来**，不能给用户机器上留一条莫名其妙的排除。
    internal static bool AddDefenderExclusion(string dir)
    {
        string script =
            "try { Add-MpPreference -ExclusionPath '" + PS(dir) + "' -ErrorAction Stop; exit 0 } " +
            "catch { exit 1 }";
        string o;
        return Run("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"", out o) == 0;
    }

    internal static bool RemoveDefenderExclusion(string dir)
    {
        string script =
            "try { Remove-MpPreference -ExclusionPath '" + PS(dir) + "' -ErrorAction Stop; exit 0 } " +
            "catch { exit 1 }";
        string o;
        return Run("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script + "\"", out o) == 0;
    }

    // ── 「程序和功能」里的条目 ──────────────────────────────────────────
    internal static void RemoveFromAppsList()
    {
        try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); }
        catch { }
    }

    internal static int DirSizeKb(string dir)
    {
        long total = 0;
        try
        {
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return (int)(total / 1024);
    }

    // ── 卸载 ────────────────────────────────────────────────────────────
    // 七步。每一步都是「尽力而为」：失败只提示，绝不中断 —— 用户点卸载的
    // 意图是「别再出现在我机器上」，不是「给我看报错」。最后一定走到删目录。
    internal static int DoUninstall(string dir)
    {
        // [1/7] 先关界面，否则目录里的 dll / exe 被占用，删不掉。
        Step(1, 7, "关闭正在运行的界面");
        KillRunning(UiExe);
        KillRunning(EngineExe);
        Thread.Sleep(400);
        Say("已关闭。");

        // [2/7] 删掉「不弹 UAC」那个提权任务。删不掉不是致命问题（任务计划里
        //       留个指向已删目录的条目而已），但要告诉用户一声。
        Step(2, 7, "删除提权启动项");
        string o2;
        if (Run("schtasks.exe", "/delete /tn \"" + UiTaskName + "\" /f", out o2) == 0)
            Say("已删除 " + UiTaskName + "。");
        else
            Say("没有这个任务（或已删过），跳过。");

        // [3/7] 让引擎自己卸守护 —— 那套逻辑在 one\Core.cs 里，我们不重写。
        Step(3, 7, "卸载后台守护");
        string eng = Path.Combine(dir, EngineExe);
        if (!File.Exists(eng))
        {
            Say("引擎不在了，跳过（守护任务若还在，可在「任务计划程序」里手动删）。");
        }
        else
        {
            string o;
            int rc = Run(eng, "uninstall", out o);
            foreach (string line in Split(o)) if (line.Trim().Length > 0) Say("引擎：" + line.Trim());
            if (rc == 0) Say("守护已卸载。");
            else Warn("守护卸载返回码 " + rc + "，可能没清干净。");
        }

        // [4/7] 对称收回 Defender 排除项。**这一步很重要** —— 安装时加的，
        //       卸载不收回就是给用户机器上留了一条永久的、说不清来历的排除。
        Step(4, 7, "收回 Windows Defender 排除项");
        if (RemoveDefenderExclusion(dir)) Say("已收回 " + dir + " 的排除项，Defender 恢复原样。");
        else Say("没有可收回的排除项（或收回失败），不影响卸载。");

        // [5/7] 从「设置 → 应用」里摘掉自己。
        Step(5, 7, "从「程序和功能」里移除");
        RemoveFromAppsList();
        Say("已移除。");

        // [6/7] 快捷方式
        Step(6, 7, "删除快捷方式");
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desktop, ShortcutName + ".lnk");
            if (File.Exists(lnk)) { File.Delete(lnk); Say("桌面快捷方式已删除。"); }
            else Say("桌面没有快捷方式，跳过。");

            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (programs != null && programs.Length > 0)
            {
                string group = Path.Combine(programs, AppName);
                if (Directory.Exists(group)) { Directory.Delete(group, true); Say("开始菜单组已删除。"); }
                else Say("开始菜单里没有，跳过。");
            }
        }
        catch (Exception ex) { Warn("删快捷方式出错：" + ex.Message); }

        // [7/7] 删程序文件。**自己就住在要删的目录里**，Windows 不允许删掉正在
        //       运行的自家的 exe ⇒ 写一个批处理，等本进程退出几秒后再删。
        Step(7, 7, "删除程序文件");
        bool inside = false;
        try
        {
            string self = Assembly.GetExecutingAssembly().Location;
            inside = self.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }
        catch { }

        if (inside)
        {
            try
            {
                //  ★ 哨兵文件：**这是这段代码唯一的安全阀**。
                //    批处理醒来时先确认「这个目录还是我当初要删的那个」。
                //    如果这中间用户又装了一次，Installer 的 RemoveStaleFiles 会把
                //    这个文件当遗留文件清掉 ⇒ 批处理看到哨兵不在就跳过，不会把
                //    刚装好的那份删掉。没有哨兵的话，ping 那两三秒就是一段盲删
                //    窗口 —— 卸载后立刻重装，新装的那份会被吃掉（实测撞到过：
                //    18:12 装好的目录在两分钟后凭空消失，只剩界面配色目录）。
                string sentinel = Path.Combine(dir, SentinelName);
                File.WriteAllText(sentinel, DateTime.Now.ToString("o"), Encoding.UTF8);

                string bat = Path.Combine(Path.GetTempPath(), "vcb-uninst-" + Guid.NewGuid().ToString("N") + ".cmd");
                string body =
                    "@echo off\r\n" +
                    "ping 127.0.0.1 -n 4 >nul\r\n" +
                    "if not exist \"" + sentinel + "\" goto :vcb_done\r\n" +
                    "rd /s /q \"" + dir + "\"\r\n" +
                    ":vcb_done\r\n" +
                    "del /f /q \"" + bat + "\"\r\n";
                File.WriteAllText(bat, body, Encoding.Default);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow  = true;
                Process.Start(psi);
                Say("程序文件将在本窗口关闭后删除：" + dir);
            }
            catch (Exception ex)
            {
                Warn("排删除任务失败，请手动删掉这个目录：" + dir);
                Warn("（" + ex.Message + "）");
            }
        }
        else
        {
            try
            {
                if (Directory.Exists(dir)) { Directory.Delete(dir, true); Say("已删除 " + dir); }
                else Say("目录本来就不存在，跳过。");
            }
            catch (Exception ex)
            {
                Warn("删不干净：" + ex.Message);
                Warn("请手动删掉这个目录：" + dir);
            }
        }

        // 界面设置（配色种子）刻意留着 —— 那是用户数据，不是程序文件。
        try
        {
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (la != null && la.Length > 0)
            {
                string setDir = Path.Combine(la, UiSettingsDir);
                if (Directory.Exists(setDir))
                    Say("界面的配色设置保留在 " + setDir + "（想清掉可以手动删）。");
            }
        }
        catch { }

        return 0;
    }
}
