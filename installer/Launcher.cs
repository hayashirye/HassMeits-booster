// ---------------------------------------------------------------------------
//  run-ui.exe —— 「游戏 CPU 高频优化器」的静默启动跳板
// ---------------------------------------------------------------------------
//  它只做一件事：schtasks /run /tn "ValorantBoostUI"
//
//  为什么要单独一个 exe，而不是让快捷方式直接调 schtasks.exe：
//    · schtasks.exe 是控制台程序，快捷方式调它会闪一下黑窗；
//    · 本程序编成 /target:winexe，没有控制台，全程看不见。
//
//  为什么不直接用安装器当跳板：
//    安装器带 requireAdministrator 清单，每次运行都会弹 UAC —— 那就白干了。
//    本程序**不带任何清单**（asInvoker），所以不弹；真正提权的是被它触发的
//    那个 RunLevel=HighestAvailable 计划任务。
//
//  编码：本文件含中文，必须存成 UTF-8 带 BOM（csc 靠 BOM 判断编码）。
//  语法：只用 C# 5，兼容系统自带的旧 csc。
// ---------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class Launcher
{
    private const string UiTaskName = "ValorantBoostUI";
    private const string AppName    = "游戏 CPU 高频优化器";
    private const string UiExe      = "valorant_boost.exe";

    [STAThread]
    private static int Main(string[] args)
    {
        // ① 首选：走计划任务 → 管理员身份、零 UAC
        if (RunTask()) return 0;

        // ② 兜底：任务不在（比如装完被手动删了），直接启动界面。
        //    这次会弹 UAC，但总比什么都打不开强。
        return DirectLaunch();
    }

    private static bool RunTask()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe",
                "/run /tn \"" + UiTaskName + "\"");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(20000);
                return p.HasExited && p.ExitCode == 0;
            }
        }
        catch { return false; }
    }

    private static int DirectLaunch()
    {
        try
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string exe = Path.Combine(dir, UiExe);
            if (!File.Exists(exe))
            {
                MessageBox.Show("找不到 " + UiExe + "。\r\n\r\n安装目录可能被搬走了，请重新运行 install.exe。",
                                AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.WorkingDirectory = dir;
            psi.UseShellExecute = true;
            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败：\r\n" + ex.Message, AppName,
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
