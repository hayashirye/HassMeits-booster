// ---------------------------------------------------------------------------
//  游戏 CPU 高频优化器 —— 独立卸载器
// ---------------------------------------------------------------------------
//  用法：
//      uninstall.exe                卸载（自己找安装位置，问一句再动手）
//      uninstall.exe --dir "D:\x"   指定安装目录
//      uninstall.exe --quiet        不暂停、不询问（给「设置 → 应用」里的
//                                   卸载按钮用，也方便脚本调用）
//
//  为什么要有这个文件，而不是继续用 install.exe --uninstall：
//
//  1) install.exe 一旦被删 / 被安全软件拦掉，卸载入口就断了 —— 用户的
//     安装目录里会永远留着一个删不掉的文件夹和一个计划任务。卸载器不该
//     跟安装器共用一条命。
//
//  2) 「设置 → 应用」里需要一条注册表记录（HKLM\...\Uninstall\ValorantCpuBoost），
//     它的 UninstallString 要指向一个**稳定存在**的程序名。install.exe 不是
//     个好名字 —— 用户看到「卸载」按钮却跳出个 install，会以为是装东西。
//
//  3) 只做卸载这一件事，所以它不需要内嵌 payload，体积 5 KB 上下。
//
//  真正的卸载逻辑在 Vcb.cs 里，这个文件和 Installer.cs 共用它。
//
//  和 Installer.cs 同一套约束：UTF-8 带 BOM、只用 C# 5 语法。
// ---------------------------------------------------------------------------

using System;
using System.IO;
using Microsoft.Win32;

internal static class Uninstaller
{
    private static bool _quiet;

    private static int Main(string[] args)
    {
        string dir = null;
        bool forceDir = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--quiet" || a == "/quiet" || a == "-q") { _quiet = true; continue; }
            if (a == "--dir" || a == "/dir")
            {
                if (i + 1 < args.Length) { dir = args[i + 1]; forceDir = true; i++; }
                continue;
            }
            if (a.StartsWith("--dir=")) { dir = a.Substring(6); forceDir = true; continue; }
            if (a.StartsWith("/dir="))  { dir = a.Substring(5); forceDir = true; continue; }
        }

        Console.WriteLine();
        Console.WriteLine("  " + Vcb.AppName + " —— 卸载");
        Console.WriteLine("  ──────────────────────────────────────────────");

        if (forceDir)
        {
            if (dir == null || dir.Trim().Length == 0) dir = Vcb.FindInstalledDir();
            else dir = Path.GetFullPath(dir);
        }
        else
        {
            dir = Vcb.FindInstalledDir();
        }

        // 先把「将要发生什么」摊开给用户看。卸载是不可逆操作，
        // 而用户双击之前多半不知道程序都往机器上放了些什么。
        Console.WriteLine();
        Console.WriteLine("  安装位置：" + dir);
        Console.WriteLine();
        Console.WriteLine("  将会做这几件事：");
        Console.WriteLine("    · 关掉正在运行的界面");
        Console.WriteLine("    · 删除开机提权启动项（" + Vcb.UiTaskName + "）");
        Console.WriteLine("    · 卸载后台守护（" + Vcb.WatcherTaskName + "）");
        Console.WriteLine("    · 收回安装时加的 Windows Defender 排除项");
        Console.WriteLine("    · 从「程序和功能」列表里移除");
        Console.WriteLine("    · 删除桌面和开始菜单的快捷方式");
        Console.WriteLine("    · 删除程序文件");
        Console.WriteLine();
        Console.WriteLine("  不会动您的电源方案、游戏配置、以及界面的配色设置。");
        Console.WriteLine("  （电源方案如需还原，请在卸载前先在界面里点「还原初始设置」。）");
        Console.WriteLine();

        if (!_quiet)
        {
            Console.Write("  确认卸载请按回车，取消请直接关掉这个窗口 ...");
            Console.ReadLine();
        }

        Console.WriteLine();
        int rc = Vcb.DoUninstall(dir);

        Console.WriteLine();
        Console.WriteLine("  ──────────────────────────────────────────────");
        Console.WriteLine("  卸载完成。");
        Console.WriteLine();

        if (!_quiet)
        {
            Console.Write("按回车键关闭这个窗口...");
            Console.ReadLine();
        }
        return rc;
    }
}
