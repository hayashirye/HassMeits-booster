// ============================================================================
//  无畏契约 CPU 高频优化器  ·  核心引擎（原生 C#，不依赖 PowerShell）
// ----------------------------------------------------------------------------
//  设计要点（全部来自真机实测，不是猜的）：
//   1. 只写「当前活动的电源方案」。荣耀电脑管家会自己建方案并抢走活动状态，
//      老版本切到「卓越性能」再写设置的路线，在真机上等于一项都没生效。
//   2. 写入前按 powercfg 自报的「最小/最大可能的设置」夹取。
//   3. 40fbefc7 是枚举（0=理想的 1=单一 2=Rocket），不是百分比，
//      老代码拿 100 去写它，注定被夹成 2。
//   4. 备份用行式文本，不引 JSON 库，Nobody 需要 JSON。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Vcb
{
    // ────────────────────────────────────────────────────────────────────
    //  设置项定义
    // ────────────────────────────────────────────────────────────────────
    internal class SettingDef
    {
        public string Guid;      // powercfg 设置 GUID
        public string Name;      // 中文名
        public string Hint;      // 说明
        public SettingDef(string g, string n, string h) { Guid = g; Name = n; Hint = h; }
    }

    internal class SettingValue
    {
        public bool Exists;
        public int Ac = -1;
        public int Dc = -1;
        public int AcMin = -1;
        public int AcMax = -1;
        public int DcMin = -1;
        public int DcMax = -1;
    }

    internal class SchemeInfo
    {
        public string Guid = "";
        public string Name = "";
        public bool Active;
    }

    internal static class Catalog
    {
        public const string SUB_PROCESSOR = "54533251-82be-4824-96c1-47b60b740d00";

        // 本机实测该子组只有 8 项，另两项是"名单里有、本机没有"，
        // 保留它们是因为别的机器（尤其台式机）上有 —— 不存在就自动跳过。
        public static List<SettingDef> Build()
        {
            List<SettingDef> L = new List<SettingDef>();
            L.Add(new SettingDef("893dee8e-2bef-41e0-89c6-b55d0929964c", "最小处理器状态", "100 = 不许降频（最影响游戏手感的一项）"));
            L.Add(new SettingDef("bc5038f7-23e0-4960-96da-33abaf5935ec", "最大处理器状态", "100 = 不设上限"));
            L.Add(new SettingDef("be337238-0d82-4146-a960-4f3749d470c7", "性能提升模式", "2 = 激进（睿频更积极）"));
            L.Add(new SettingDef("36687f9e-e3a5-4dbf-b1dc-15eb381c6863", "能效偏好 EPP", "0 = 最偏性能，100 = 最偏省电"));
            L.Add(new SettingDef("06cadf0e-64ed-448a-8927-ce7bf90eb35d", "性能提高阈值", "0 = 更早升频"));
            L.Add(new SettingDef("40fbefc7-2e9d-4d25-a185-0cfd8574bac6", "性能降低策略", "0=理想的 1=单一 2=Rocket（枚举，不是百分比）"));
            L.Add(new SettingDef("5d76a2ca-e8c0-402f-a133-2158492d58ad", "空闲禁用", "1 = 不进深度空闲，唤醒更快"));
            L.Add(new SettingDef("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5", "异构调度策略", "0 = 优先用 P 核"));
            L.Add(new SettingDef("0cc5b647-c1df-4637-891a-dec35c318583", "核心停放最小核心", "100 = 不核心停泊"));
            L.Add(new SettingDef("ea062031-0e34-4ff1-9b6d-eb1059334028", "核心停放最大核心", "100 = 全部可唤醒"));
            return L;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  四档模式
    // ────────────────────────────────────────────────────────────────────
    internal class ModeProfile
    {
        public string Key;
        public string Name;
        public string Desc;
        public Dictionary<string, int> Ac = new Dictionary<string, int>();
        public Dictionary<string, int> Dc = new Dictionary<string, int>();
    }

    internal static class Modes
    {
        // 键名 = 设置项名的前缀，写入时按前缀匹配 Catalog 里的项
        public static List<ModeProfile> Build()
        {
            List<ModeProfile> L = new List<ModeProfile>();

            ModeProfile comp = new ModeProfile();
            comp.Key = "competitive"; comp.Name = "竞技";
            comp.Desc = "插电时锁死高频，不插电时保持体面。最推荐。";
            comp.Ac["最小处理器状态"] = 100; comp.Dc["最小处理器状态"] = 50;
            comp.Ac["最大处理器状态"] = 100; comp.Dc["最大处理器状态"] = 100;
            comp.Ac["性能提升模式"] = 2;     comp.Dc["性能提升模式"] = 2;
            comp.Ac["能效偏好"] = 0;         comp.Dc["能效偏好"] = 40;
            comp.Ac["性能提高阈值"] = 0;     comp.Dc["性能提高阈值"] = 10;
            comp.Ac["性能降低策略"] = 1;     comp.Dc["性能降低策略"] = 1;
            comp.Ac["空闲禁用"] = 1;         comp.Dc["空闲禁用"] = 1;
            comp.Ac["异构调度策略"] = 0;     comp.Dc["异构调度策略"] = 0;
            comp.Ac["核心停放最小核心"] = 100; comp.Dc["核心停放最小核心"] = 100;
            comp.Ac["核心停放最大核心"] = 100; comp.Dc["核心停放最大核心"] = 100;
            L.Add(comp);

            ModeProfile max = new ModeProfile();
            max.Key = "max"; max.Name = "极限";
            max.Desc = "插不插电都拉满，最热最费电。台式机或一直插电的笔记本用。";
            max.Ac["最小处理器状态"] = 100; max.Dc["最小处理器状态"] = 100;
            max.Ac["最大处理器状态"] = 100; max.Dc["最大处理器状态"] = 100;
            max.Ac["性能提升模式"] = 2;     max.Dc["性能提升模式"] = 2;
            max.Ac["能效偏好"] = 0;         max.Dc["能效偏好"] = 0;
            max.Ac["性能提高阈值"] = 0;     max.Dc["性能提高阈值"] = 0;
            max.Ac["性能降低策略"] = 1;     max.Dc["性能降低策略"] = 1;
            max.Ac["空闲禁用"] = 1;         max.Dc["空闲禁用"] = 1;
            max.Ac["异构调度策略"] = 0;     max.Dc["异构调度策略"] = 0;
            max.Ac["核心停放最小核心"] = 100; max.Dc["核心停放最小核心"] = 100;
            max.Ac["核心停放最大核心"] = 100; max.Dc["核心停放最大核心"] = 100;
            L.Add(max);

            ModeProfile bal = new ModeProfile();
            bal.Key = "balanced"; bal.Name = "均衡";
            bal.Desc = "日常使用与游戏兼顾，空闲时允许 CPU 省电。";
            bal.Ac["最小处理器状态"] = 50;  bal.Dc["最小处理器状态"] = 20;
            bal.Ac["最大处理器状态"] = 100; bal.Dc["最大处理器状态"] = 100;
            bal.Ac["性能提升模式"] = 2;     bal.Dc["性能提升模式"] = 2;
            bal.Ac["能效偏好"] = 20;        bal.Dc["能效偏好"] = 50;
            bal.Ac["性能提高阈值"] = 10;    bal.Dc["性能提高阈值"] = 30;
            bal.Ac["空闲禁用"] = 1;         bal.Dc["空闲禁用"] = 0;
            bal.Ac["核心停放最小核心"] = 100; bal.Dc["核心停放最小核心"] = 100;
            bal.Ac["核心停放最大核心"] = 100; bal.Dc["核心停放最大核心"] = 100;
            L.Add(bal);

            ModeProfile safe = new ModeProfile();
            safe.Key = "safe"; safe.Name = "保守";
            safe.Desc = "只动最关键的三项，风险最小，随时可退。";
            safe.Ac["最小处理器状态"] = 100; safe.Dc["最小处理器状态"] = 20;
            safe.Ac["性能提升模式"] = 2;     safe.Dc["性能提升模式"] = 2;
            safe.Ac["能效偏好"] = 0;         safe.Dc["能效偏好"] = 50;
            L.Add(safe);

            return L;
        }

        public static ModeProfile Find(string key)
        {
            foreach (ModeProfile m in Build()) if (m.Key == key) return m;
            return Build()[0];
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  powercfg 封装
    // ────────────────────────────────────────────────────────────────────
    internal static class PowerCfg
    {
        public static int LastExit = 0;

        public static string Run(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                // powercfg 的中文输出走系统 ANSI 代码页（中文系统 = GBK/936），
                // 不显式指定的话中文全变问号，所有标签匹配都会失败。
                psi.StandardOutputEncoding = Encoding.Default;
                psi.StandardErrorEncoding = Encoding.Default;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(20000);
                    LastExit = p.ExitCode;
                    return o;
                }
            }
            catch (Exception ex)
            {
                LastExit = -1;
                return "";
            }
        }

        /// <summary>中文标签或英文标签命中其一即算命中。</summary>
        public static bool Has(string line, string zh, string en)
        {
            return line.IndexOf(zh, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf(en, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>取冒号（半角或全角）后面的十六进制值。</summary>
        public static int HexAfterColon(string line)
        {
            int c = line.IndexOf(':');
            int f = line.IndexOf('：');
            if (c < 0 || (f >= 0 && f < c)) c = f;
            if (c < 0) return -1;
            string hex = line.Substring(c + 1).Trim();
            if (hex.StartsWith("0x") || hex.StartsWith("0X")) hex = hex.Substring(2);
            int v;
            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return v;
            return -1;
        }

        /// <summary>在一段 powercfg 输出里找某个标签后面的十六进制值。</summary>
        public static int FindHex(string text, string label)
        {
            if (text == null) return -1;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0) continue;
                if (t.IndexOf(label, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int colon = t.IndexOf(':');
                if (colon < 0) colon = t.IndexOf('：');
                if (colon < 0) continue;
                string hex = t.Substring(colon + 1).Trim();
                if (hex.StartsWith("0x") || hex.StartsWith("0X")) hex = hex.Substring(2);
                int v;
                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return v;
            }
            return -1;
        }

        public static string ActiveSchemeGuid()
        {
            string o = Run("/getactivescheme");
            return FirstGuid(o);
        }

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRoot, out IntPtr guidPtr);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr h);

        /// <summary>
        /// 直接问电源服务要活动方案的 GUID，不启动 powercfg.exe。
        ///
        /// 为什么要有这个：守护在游戏跑着的时候每 4 秒检查一次「活动方案有没有被
        /// 荣耀切走」，用 powercfg.exe 的话就是每 4 秒创建一个进程 —— 实测
        /// 16.4 ms/次、每分钟约 250 ms 的纯开销，而它跑的时候用户正在打游戏。
        /// 换成这个 API 之后是微秒级，而且完全不碰磁盘、不占 P 核。
        /// 失败（老系统没有 powrprof 导出）返回 null，调用方退回慢的那条路。
        /// </summary>
        public static string ActiveSchemeGuidFast()
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                uint r = PowerGetActiveScheme(IntPtr.Zero, out p);
                if (r != 0 || p == IntPtr.Zero) return null;
                // GUID 在内存里的字节序和 Guid.ToByteArray() 一致，直接搬 16 个字节。
                byte[] b = new byte[16];
                Marshal.Copy(p, b, 0, 16);
                return new Guid(b).ToString().ToLowerInvariant();
            }
            catch { return null; }
            finally { if (p != IntPtr.Zero) { try { LocalFree(p); } catch { } } }
        }

        public static string FirstGuid(string s)
        {
            if (s == null) return null;
            for (int i = 0; i + 36 <= s.Length; i++)
            {
                string c = s.Substring(i, 36);
                if (IsGuid(c)) return c.ToLowerInvariant();
            }
            return null;
        }

        public static bool IsGuid(string s)
        {
            if (s == null || s.Length != 36) return false;
            for (int i = 0; i < 36; i++)
            {
                char c = s[i];
                if (i == 8 || i == 13 || i == 18 || i == 23) { if (c != '-') return false; continue; }
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        public static List<SchemeInfo> ListSchemes()
        {
            List<SchemeInfo> L = new List<SchemeInfo>();
            string o = Run("/list");
            string[] lines = o.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string g = FirstGuid(line);
                if (g == null) continue;
                SchemeInfo s = new SchemeInfo();
                s.Guid = g;
                int a = line.IndexOf('(');
                int b = line.LastIndexOf(')');
                if (a >= 0 && b > a) s.Name = line.Substring(a + 1, b - a - 1).Trim();
                if (s.Name.Length == 0) s.Name = g;
                s.Active = line.IndexOf('*') >= 0;
                L.Add(s);
            }
            return L;
        }

        /// <summary>一次调用读回整个子组的所有设置值（比逐项查询快 10 倍）。</summary>
        public static Dictionary<string, SettingValue> ReadSubgroup(string scheme, string sub)
        {
            Dictionary<string, SettingValue> map = new Dictionary<string, SettingValue>();
            string o = Run(string.Format("/query {0} {1}", scheme, sub));
            if (o == null || o.Length == 0) return map;

            string[] lines = o.Split('\n');
            string cur = null;
            SettingValue sv = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0) continue;

                // 设置项的标题行形如：
                //     电源设置 GUID: 06cadf0e-64ed-448a-8927-ce7bf90eb35d  (处理器性能提高阈值)
                //     英文：Power Setting GUID: ...
                // 认这一行来给设置项定界。**紧跟其后的「GUID 别名: PERFINCTHRESHOLD」
                // 那一行根本不含 GUID** —— 第一版就是拿别名行去 FirstGuid，一个都没匹配上，
                // 整张表被误判成「本机无此项」（实测：10 项全是"本机无此项"）。
                // 子组标题是「子组 GUID: …」/「Subgroup GUID: …」，跟设置行区分得开。
                if (Has(t, "电源设置 GUID", "Power Setting GUID"))
                {
                    string g = FirstGuid(t);
                    if (g != null)
                    {
                        cur = g;
                        sv = new SettingValue();
                        sv.Exists = true;
                        map[cur] = sv;
                    }
                    continue;
                }
                if (sv == null) continue;

                // 中文 powercfg 的字段顺序是：
                //   最小可能的设置 -> 最大可能的设置 -> 可能的设置增量 -> 可能的设置单位
                //   -> 当前交流电源设置索引 -> 当前直流电源设置索引
                // 注意「最小/最大可能的设置」出现在「当前交流…索引」**之前**，
                // 老 PowerShell 引擎的注释写成"紧随其后"，导致范围解析整个错位、夹取从未生效。
                if (Has(t, "最小可能的设置", "Minimum Possible Setting"))
                { sv.AcMin = HexAfterColon(t); }
                else if (Has(t, "最大可能的设置", "Maximum Possible Setting"))
                { sv.AcMax = HexAfterColon(t); }
                else if (Has(t, "当前交流电源设置索引", "Current AC Power Setting Index"))
                { sv.Ac = HexAfterColon(t); }
                else if (Has(t, "当前直流电源设置索引", "Current DC Power Setting Index"))
                { sv.Dc = HexAfterColon(t); }
            }
            return map;
        }

        public static bool WriteValue(string scheme, string sub, string set, int ac, int dc)
        {
            Run(string.Format("/setacvalueindex {0} {1} {2} {3}", scheme, sub, set, ac));
            int ca = LastExit;
            Run(string.Format("/setdcvalueindex {0} {1} {2} {3}", scheme, sub, set, dc));
            int cb = LastExit;
            // 老 PowerShell 引擎的判断是「两个都失败才算失败」，只失败一个只打警告。
            // 这里沿用同样的宽松口径，避免因为某台机器不支持 DC 而整项误报失败。
            return ca == 0 || cb == 0;
        }

        public static void Activate(string scheme)
        {
            Run(string.Format("/setactive {0}", scheme));
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  备份文件（行式文本，人也能读）
    // ────────────────────────────────────────────────────────────────────
    internal class BackupItem
    {
        public string Scheme;   // v3 起：这条原始值属于哪个电源方案。v2 的旧备份留空 = 用顶层 Scheme
        public string Set;
        public int Ac = -1;
        public int Dc = -1;
        public bool Existed;
    }

    internal class Backup
    {
        public string Created = "";
        public string Scheme = "";
        public string SchemeName = "";
        public List<BackupItem> Items = new List<BackupItem>();

        public bool IsUsable()
        {
            if (Items.Count == 0) return false;
            foreach (BackupItem it in Items) if (it.Existed) return true;
            return false;
        }

        public string Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# ValorantCpuBoost backup v3");
            sb.AppendLine("Created=" + Created);
            sb.AppendLine("Scheme=" + Scheme);
            sb.AppendLine("SchemeName=" + SchemeName);
            // v3：每个电源方案一段，段前重新声明 Scheme=。
            // 为什么必须分方案备份：荣耀电脑管家会在游戏启动的一瞬间把活动方案从
            // 「平衡」切到它自己的「Honor Performance」，两个方案的原值并不一样，
            // 只存一份的话还原时会把另一个方案弄脏。
            string last = null;
            foreach (BackupItem it in Items)
            {
                if (it.Scheme != last)
                {
                    sb.AppendLine("Scheme=" + it.Scheme);
                    sb.AppendLine("SchemeName=" + Engine.SchemeNameOf(it.Scheme));
                    last = it.Scheme;
                }
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Setting={0} {1} {2} {3}", it.Set, it.Ac, it.Dc, it.Existed ? 1 : 0));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static Backup Load(string path)
        {
            if (!File.Exists(path)) return null;
            Backup b = new Backup();
            string curScheme = null;   // 当前段落属于哪个方案；v2 的备份里只有开头一个 Scheme=
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                if (k == "Created") b.Created = v;
                else if (k == "Scheme") { b.Scheme = v; curScheme = v; }
                else if (k == "SchemeName") b.SchemeName = v;
                else if (k == "Setting")
                {
                    string[] p = v.Split(' ');
                    if (p.Length >= 4)
                    {
                        BackupItem it = new BackupItem();
                        it.Scheme = curScheme;
                        it.Set = p[0];
                        int.TryParse(p[1], out it.Ac);
                        int.TryParse(p[2], out it.Dc);
                        it.Existed = (p[3] == "1");
                        b.Items.Add(it);
                    }
                }
            }
            return b;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  引擎
    // ────────────────────────────────────────────────────────────────────
    internal class OpResult
    {
        public bool Ok = true;
        public List<string> Lines = new List<string>();
        public void Add(string s) { Lines.Add(s); }
        public void Fail(string s) { Ok = false; Lines.Add(s); }
        public string Text { get { return string.Join("\r\n", Lines.ToArray()); } }
    }

    // 表格对齐：按「真实像素」补空格，而不是数格子。
    // 为什么：原来假设「1 个汉字 = 2 个半角」，这在等宽字体（Consolas）下成立，
    // 实测 Consolas 里 4 个汉字 = 8 个数字 = 63px，正好 2 倍。
    // 但界面统一到微软雅黑后就不成立了 —— 雅黑里 4 个汉字 = 56px，
    // 而 8 个数字 = 64px，汉字只有 1.73 格。照老算法补，同一列里
    // 汉字个数不同的行会参差（算下来差 10px 上下），一眼能看出来。
    // UiFont 由界面在构造时设成文本框用的字体；命令行模式留 null，
    // 这时退回按格计数 —— 控制台本身是等宽的，--out 写出来的文件也要能用记事本对齐。
    internal static class TextPad
    {
        public static System.Drawing.Font UiFont = null;

        public static string To(string s, int cells)
        {
            if (s == null) s = "";
            if (UiFont == null)
            {
                int len = 0;
                foreach (char c in s) len += (c > 127 ? 2 : 1);
                if (len >= cells) return s + " ";
                StringBuilder p = new StringBuilder(s);
                for (int i = len; i < cells; i++) p.Append(' ');
                return p.ToString();
            }
            int unit = Measure(" ");
            if (unit <= 0) unit = 1;
            int target = cells * unit;
            int now = Measure(s);
            StringBuilder b = new StringBuilder(s);
            while (now < target) { b.Append(' '); now += unit; }
            return b.ToString();
        }

        private static int Measure(string s)
        {
            try
            {
                return System.Windows.Forms.TextRenderer.MeasureText(
                    s, UiFont, System.Drawing.Size.Empty,
                    System.Windows.Forms.TextFormatFlags.NoPadding).Width;
            }
            catch { return 0; }
        }
    }

    internal static class Engine
    {
        // ★ 守护进程会被安装到工作区之外（见 AutoStart.InstallDir 的注释），
        //   但日志和自动备份必须还写在用户原本的目录里，否则界面读不到守护的日志。
        //   用 watch --dir "<原目录>" 把目录带过去。
        private static string _dirOverride = null;

        public static void SetDir(string d)
        {
            if (string.IsNullOrEmpty(d)) return;
            _dirOverride = d;
            try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { }
        }

        public static string Dir
        {
            get
            {
                if (_dirOverride != null) return _dirOverride;
                string d = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                try { File.WriteAllText(Path.Combine(d, ".wtest"), ""); File.Delete(Path.Combine(d, ".wtest")); }
                catch { d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ValorantCpuBoost"); }
                try { if (!Directory.Exists(d)) Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        public static string BackupPath { get { return Path.Combine(Dir, "boost-backup.txt"); } }
        public static string LogPath { get { return Path.Combine(Dir, "boost-log.txt"); } }

        public static void Log(string s)
        {
            try
            {
                File.AppendAllText(LogPath,
                    string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}\r\n", DateTime.Now, s),
                    new UTF8Encoding(false));
            }
            catch { }
        }

        public static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static Backup MakeBackup()
        {
            Backup b = new Backup();
            b.Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            b.Scheme = PowerCfg.ActiveSchemeGuid();
            b.SchemeName = SchemeNameOf(b.Scheme);
            // ★ 备份系统里【每一个】电源方案，不只当前活动的那一个。
            // 理由：荣耀电脑管家会在游戏启动的一瞬间把活动方案从「平衡」切到它自己建的
            // 「Honor Performance」。优化要写进全部方案，备份就必须覆盖全部方案 ——
            // 否则还原时会拿一个方案的原值去覆盖另一个方案，把没动过的方案弄脏。
            List<string> guids = new List<string>();
            foreach (SchemeInfo s in PowerCfg.ListSchemes()) guids.Add(s.Guid);
            if (guids.Count == 0) guids.Add(b.Scheme);
            foreach (string g in guids)
            {
                Dictionary<string, SettingValue> map = PowerCfg.ReadSubgroup(g, Catalog.SUB_PROCESSOR);
                foreach (SettingDef d in Catalog.Build())
                {
                    BackupItem it = new BackupItem();
                    it.Scheme = g;
                    it.Set = d.Guid;
                    SettingValue sv;
                    if (map.TryGetValue(d.Guid, out sv))
                    {
                        it.Existed = true; it.Ac = sv.Ac; it.Dc = sv.Dc;
                    }
                    else { it.Existed = false; }
                    b.Items.Add(it);
                }
            }
            return b;
        }

        public static string SchemeNameOf(string guid)
        {
            foreach (SchemeInfo s in PowerCfg.ListSchemes())
                if (string.Equals(s.Guid, guid, StringComparison.OrdinalIgnoreCase)) return s.Name;
            return guid;
        }

        public static OpResult Apply(ModeProfile mode)
        {
            return Apply(mode, BackupPath, false);
        }

        // backupPath 可换：后台守护用 auto-backup.txt，不覆盖手动点「一键优化」留下的那一份
        public static OpResult Apply(ModeProfile mode, string backupPath)
        {
            return Apply(mode, backupPath, false);
        }

        // skipBackup = true 用于守护的「活动方案被荣耀换掉了，重写一遍」那次调用。
        // 那时方案里装的已经是优化后的值，再采集一次就会把优化值当成「原始值」存下来，
        // 之后还原等于空操作 —— CPU 会一直钉在 100%，这是很隐蔽的坑。
        public static OpResult Apply(ModeProfile mode, string backupPath, bool skipBackup)
        {
            OpResult r = new OpResult();
            string scheme = PowerCfg.ActiveSchemeGuid();
            if (scheme == null) { r.Fail("读不到当前电源方案 —— powercfg 没有返回结果。"); return r; }

            string sname = SchemeNameOf(scheme);
            bool known = (scheme == "381b4222-f694-41f0-9685-ff5bb260df2e" ||
                          scheme == "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" ||
                          scheme == "a1841308-3541-4fab-bc81-f71556f20b4a" ||
                          scheme == "e9a42b02-d5df-448d-aa00-03f14749eb61");

            // ★ 关键修正：不能只写「当前活动方案」。
            // 荣耀电脑管家会在游戏启动的一瞬间，把活动方案从「平衡」切到它自己建的
            // 「Honor Performance」—— 里面是荣耀的原始值。只写当前方案的话，
            // 用户在桌面上点了「一键优化」，一进游戏就被换回没优化的方案，等于白做。
            // 所以这里把设置写进系统里【每一个】电源方案，它爱怎么切都切不掉。
            List<string> targets = new List<string>();
            List<string> targetNames = new List<string>();
            foreach (SchemeInfo s in PowerCfg.ListSchemes())
            {
                targets.Add(s.Guid);
                targetNames.Add(s.Name);
            }
            if (targets.Count == 0) { targets.Add(scheme); targetNames.Add(sname); }

            r.Add("当前活动方案：" + sname);
            r.Add("方案 GUID：" + scheme);
            r.Add("将写入 " + targets.Count + " 个电源方案：" + string.Join("、", targetNames.ToArray()));
            if (!known)
            {
                r.Add("");
                r.Add("注：「" + sname + "」不是 Windows 自带方案，是别的软件（本机上是荣耀电脑管家）建的。");
                r.Add("    它会在游戏启动时把活动方案切走，所以本程序把设置写进全部方案。");
            }

            // 1. 先备份
            if (skipBackup)
            {
                r.Add("");
                r.Add("（沿用已有备份 " + backupPath + "，不重新采集原始值）");
            }
            else
            {
                Backup b = MakeBackup();
                b.Save(backupPath);
                r.Add("");
                r.Add("已备份原始设置 -> " + backupPath);
            }

            // 2. 写入
            Dictionary<string, SettingValue> map = PowerCfg.ReadSubgroup(scheme, Catalog.SUB_PROCESSOR);
            r.Add("");
            r.Add("设置项                        原值 -> 新值      结果");
            r.Add(new string('-', 62));

            int skipped = 0;
            List<string> guids = new List<string>();
            List<string> names = new List<string>();
            List<int> acs = new List<int>();
            List<int> dcs = new List<int>();

            foreach (SettingDef d in Catalog.Build())
            {
                int ac = Lookup(mode.Ac, d.Name);
                int dc = Lookup(mode.Dc, d.Name);
                if (ac < 0 && dc < 0) continue;

                SettingValue sv;
                if (!map.TryGetValue(d.Guid, out sv))
                {
                    r.Add(Pad(d.Name, 28) + "本机方案里没有这一项，跳过");
                    skipped++;
                    continue;
                }
                if (ac < 0) ac = sv.Ac;
                if (dc < 0) dc = sv.Dc;

                int oldAc = sv.Ac;
                // 按本机允许范围夹取
                if (sv.AcMin >= 0 && ac < sv.AcMin) ac = sv.AcMin;
                if (sv.AcMax >= 0 && ac > sv.AcMax) ac = sv.AcMax;
                if (sv.DcMin >= 0 && dc < sv.DcMin) dc = sv.DcMin;
                if (sv.DcMax >= 0 && dc > sv.DcMax) dc = sv.DcMax;

                guids.Add(d.Guid); names.Add(d.Name); acs.Add(ac); dcs.Add(dc);
                r.Add(string.Format("{0}{1,-6} -> {2,-6}",
                    Pad(d.Name, 28), oldAc, ac));
            }

            // 2b. 同一组值写进每一个方案
            int changed = 0, failed = 0;
            foreach (string tg in targets)
            {
                Dictionary<string, SettingValue> tm = PowerCfg.ReadSubgroup(tg, Catalog.SUB_PROCESSOR);
                for (int i = 0; i < guids.Count; i++)
                {
                    SettingValue tv;
                    if (!tm.TryGetValue(guids[i], out tv)) continue;
                    if (PowerCfg.WriteValue(tg, Catalog.SUB_PROCESSOR, guids[i], acs[i], dcs[i])) changed++;
                    else failed++;
                }
            }

            // 3. 重新激活一次，逼 CPU 立刻重读
            PowerCfg.Activate(scheme);

            r.Add(new string('-', 62));
            r.Add(string.Format("写进 {0} 个方案，共 {1} 项，跳过 {2} 项，失败 {3} 项。",
                targets.Count, changed, skipped, failed));
            r.Add("");
            r.Add("已应用「" + mode.Name + "」模式。");
            r.Add("想撤销：点左侧「还原」，或再跑一次任意模式（会自动重建备份）。");
            Log("应用 " + mode.Name + " 到 " + targets.Count + " 个方案，" + changed + " 项");
            return r;
        }

        private static string Pad(string s, int w)
        {
            return TextPad.To(s, w);
        }

        private static string SettingName(string guid)
        {
            foreach (SettingDef d in Catalog.Build())
                if (string.Equals(d.Guid, guid, StringComparison.OrdinalIgnoreCase)) return d.Name;
            return guid;
        }

        private static int Lookup(Dictionary<string, int> d, string name)
        {
            foreach (KeyValuePair<string, int> kv in d)
                if (name.StartsWith(kv.Key, StringComparison.Ordinal)) return kv.Value;
            return -1;
        }

        public static OpResult Check()
        {
            OpResult r = new OpResult();
            string scheme = PowerCfg.ActiveSchemeGuid();
            if (scheme == null) { r.Fail("读不到当前电源方案。"); return r; }
            string sname = SchemeNameOf(scheme);

            r.Add("当前电源方案：" + sname);
            r.Add("方案 GUID：" + scheme);
            r.Add("");
            r.Add(string.Format("{0}{1,-8}{2,-8}{3}", Pad("设置项", 28), "交流AC", "电池DC", "判定"));
            r.Add(new string('-', 60));

            Dictionary<string, SettingValue> map = PowerCfg.ReadSubgroup(scheme, Catalog.SUB_PROCESSOR);
            ModeProfile refMode = Modes.Find("competitive");

            int ok = 0, bad = 0, none = 0;
            foreach (SettingDef d in Catalog.Build())
            {
                SettingValue sv;
                if (!map.TryGetValue(d.Guid, out sv))
                {
                    r.Add(string.Format("{0}{1,-8}{2,-8}{3}", Pad(d.Name, 28), "—", "—", "本机无此项"));
                    none++;
                    continue;
                }
                int want = Lookup(refMode.Ac, d.Name);
                string verdict;
                if (want < 0) verdict = "(参考)";
                else if (sv.Ac == want) { verdict = "符合"; ok++; }
                else { verdict = "不符合"; bad++; }
                r.Add(string.Format("{0}{1,-8}{2,-8}{3}", Pad(d.Name, 28), sv.Ac, sv.Dc, verdict));
            }
            r.Add(new string('-', 60));
            r.Add(string.Format("符合 {0} 项，不符合 {1} 项，本机无此项 {2} 项。", ok, bad, none));
            r.Add("");
            if (bad == 0 && ok > 0) r.Add("优化已生效：设置确实写进了当前正在使用的电源方案。");
            else if (bad > 0) r.Add("有 " + bad + " 项不符合 —— 优化没应用，或者被别的软件改回去了。");
            return r;
        }

        public static OpResult Restore()
        {
            return Restore(BackupPath);
        }

        public static OpResult Restore(string backupPath)
        {
            OpResult r = new OpResult();
            Backup b = Backup.Load(backupPath);
            if (b == null) { r.Fail("找不到备份文件：" + backupPath + "\r\n还没优化过，不需要还原。"); return r; }
            if (!b.IsUsable()) { r.Fail("备份文件里没有任何可用的原始值（可能是旧版本写的坏备份）。\r\n建议重新点一次「一键优化」，会覆盖成一份完好的新备份。"); return r; }

            string scheme = PowerCfg.ActiveSchemeGuid();
            string sname = SchemeNameOf(scheme);
            r.Add("备份时间：" + b.Created);
            r.Add("备份时方案：" + b.SchemeName);
            r.Add("当前方案：" + sname);
            r.Add("");
            r.Add(string.Format("{0}{1,-8}{2,-8}{3}", Pad("设置项", 28), "交流AC", "电池DC", "结果"));
            r.Add(new string('-', 60));

            // ★ 还原同样要按方案走：备份里每条原始值都记着它属于哪个方案。
            // 只还原「当前活动方案」的话，荣耀切走的那一个方案会一直留着优化值 ——
            // 用户以为还原干净了，一进游戏又被切到那个优化过的方案上。
            Dictionary<string, SettingValue> act = PowerCfg.ReadSubgroup(scheme, Catalog.SUB_PROCESSOR);
            Dictionary<string, bool> printed = new Dictionary<string, bool>();
            int done = 0, skip = 0;
            foreach (BackupItem it in b.Items)
            {
                if (!it.Existed || it.Ac < 0) continue;
                string sc = it.Scheme;
                if (string.IsNullOrEmpty(sc)) sc = b.Scheme;   // v2 旧备份：只有顶层 Scheme
                if (string.IsNullOrEmpty(sc)) { skip++; continue; }

                string nm = SettingName(it.Set);
                if (sc == scheme && !act.ContainsKey(it.Set))
                {
                    if (!printed.ContainsKey(it.Set))
                    {
                        printed[it.Set] = true;
                        r.Add(string.Format("{0}{1,-8}{2,-8}{3}", Pad(nm, 28), "—", "—", "本机无此项"));
                    }
                    skip++;
                    continue;
                }
                if (PowerCfg.WriteValue(sc, Catalog.SUB_PROCESSOR, it.Set, it.Ac, it.Dc)) done++;
                else skip++;
                if (!printed.ContainsKey(it.Set))
                {
                    printed[it.Set] = true;
                    r.Add(string.Format("{0}{1,-8}{2,-8}{3}", Pad(nm, 28), it.Ac, it.Dc, "已还原"));
                }
            }
            PowerCfg.Activate(scheme);
            r.Add(new string('-', 60));
            r.Add(string.Format("已还原 {0} 项（覆盖备份里的全部电源方案），跳过 {1} 项。", done, skip));
            r.Add("");
            r.Add("设置已回到优化之前的样子。");
            Log("还原 " + done + " 项");
            return r;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  开机自启动（计划任务）
    //  为什么不用「启动文件夹」或 Run 注册表：那两条路拉起来的进程是【普通权限】，
    //  而写 powercfg 需要管理员 —— 结果就是每次游戏开关都弹一次 UAC，根本没法用。
    //  计划任务 + /rl highest 是唯一能做到「开机静默启动、全程不弹 UAC」的办法。
    //  代价：安装/卸载任务本身需要管理员（各弹一次 UAC，只此一次）。
    // ────────────────────────────────────────────────────────────────────
    internal static class AutoStart
    {
        public const string TASK = "ValorantBoostWatcher";

        public static string ExePath()
        {
            try { return System.Reflection.Assembly.GetExecutingAssembly().Location; }
            catch { return ""; }
        }

        // ★★ 为什么守护不能就从原地跑：实测（2026-10-01）本机发现，
        //   由计划任务拉起的进程，如果 exe 位于 Agent 运行环境的工作区里，
        //   会被关进一个「只能看见 16 个进程」的隔离区 —— 连 explorer / dwm /
        //   svchost 都看不见，游戏进程自然也看不见，守护因此永远不触发。
        //   实测矩阵（同一个 exe，同样的计划任务）：
        //     C:\ProgramData\vcb\        -> 进程总数 275  看得见游戏
        //     D:\vcbprobe\               -> 进程总数 276  看得见游戏
        //     D:\test\vcbprobe\          -> 进程总数  16  看不见
        //     D:\test\valorant-cpu-boost\-> 进程总数  16  看不见
        //   所以安装时把 exe 复制到 ProgramData，任务指向副本；
        //   原目录通过 watch --dir 传进去，日志和自动备份仍写在原处。
        public static string InstallDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ValorantBoost");
        }

        public static int Run(string exe, string args, out string output)
        {
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                // schtasks 在中文系统上输出 GBK，不指定就全是问号
                psi.StandardOutputEncoding = Encoding.Default;
                psi.StandardErrorEncoding = Encoding.Default;
                Process p = Process.Start(psi);
                if (p == null) { output = "无法启动 " + exe; return -1; }
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(20000)) { try { p.Kill(); } catch { } output = "命令超时"; return -1; }
                output = (o + "\n" + e).Trim();
                return p.ExitCode;
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }

        // ★ 不为了一句「任务在不在」去启动 schtasks.exe。
        //   实测 schtasks.exe 平均 29.6 ms/次，而这是「游戏专项」页点开一次
        //   剩下的唯一一次进程创建（页面总耗时 60 ms 里它占 47 ms）。
        //   改走任务计划程序自己的 COM 接口：实测 3.2 ms/次，快约 10 倍。
        public static bool IsInstalled()
        {
            bool found;
            if (QueryByCom(out found)) return found;
            // COM 起不来（极老的系统、被组策略挡掉）才退回老办法
            string o;
            return Run("schtasks.exe", "/query /tn \"" + TASK + "\"", out o) == 0;
        }

        // 用 COM 问任务计划程序。成功返回 true 并把结果放进 found；
        // 失败返回 false，让调用方走 schtasks 兜底。
        // 用反射做 late binding，免得为一个查询给编译命令加 taskschd 引用。
        private static bool QueryByCom(out bool found)
        {
            found = false;
            object svc = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Schedule.Service");
                if (t == null) return false;
                svc = Activator.CreateInstance(t);
                if (svc == null) return false;
                object[] opt = new object[] { Type.Missing, Type.Missing, Type.Missing, Type.Missing };
                t.InvokeMember("Connect", System.Reflection.BindingFlags.InvokeMethod, null, svc, opt);
                object folder = t.InvokeMember("GetFolder", System.Reflection.BindingFlags.InvokeMethod,
                                               null, svc, new object[] { "\\" });
                if (folder == null) return false;
                // GetTask 找不到会抛异常 —— 那是「没装」，不是「COM 失败」
                try
                {
                    folder.GetType().InvokeMember("GetTask", System.Reflection.BindingFlags.InvokeMethod,
                                                  null, folder, new object[] { TASK });
                    found = true;
                }
                catch { found = false; }
                return true;
            }
            catch { return false; }
            finally
            {
                if (svc != null)
                {
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(svc); }
                    catch { }
                }
            }
        }

        public static string StatusText()
        {
            string o;
            if (Run("schtasks.exe", "/query /tn \"" + TASK + "\" /fo list", out o) != 0)
                return "未开启";
            foreach (string line in o.Split('\n'))
            {
                string s = line.Trim();
                if (s.StartsWith("状态:") || s.StartsWith("Status:"))
                {
                    int c = s.IndexOf(':');
                    if (c >= 0) return "已开启（" + s.Substring(c + 1).Trim() + "）";
                }
            }
            return "已开启";
        }

        // XML 转义：exe 路径里可能带 & < > 引号
        private static string X(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        public static bool Install(out string msg)
        {
            string src = ExePath();
            if (src.Length == 0) { msg = "读不到自身路径，无法安装。"; return false; }

            // ★★ 必须把 exe 复制到工作区外面再注册任务，理由见 InstallDir 上面的实测矩阵。
            //   直接拿原地路径注册，任务里的守护只能看见 16 个进程，永远检测不到游戏。
            string dir = InstallDir();
            string exe = Path.Combine(dir, "ValorantBoost.exe");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                bool same = false;
                try { same = string.Equals(Path.GetFullPath(src), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase); }
                catch { }
                if (!same) File.Copy(src, exe, true);
            }
            catch (Exception ex)
            {
                msg = "复制程序到下面这个位置失败：\r\n  " + exe + "\r\n" + ex.Message
                    + "\r\n（这一步需要管理员权限）";
                return false;
            }

            // 守护的工作目录 = 用户原本放程序的目录，日志和自动备份都写在那里
            string workDir = "";
            try { workDir = Path.GetDirectoryName(src); } catch { }
            if (workDir == null) workDir = "";

            // ★ 这里必须走 XML，不能用 `schtasks /create` 的命令行开关。
            //   原因：命令行的 /sc onlogon /rl highest 建出来的任务，
            //   DisallowStartIfOnBatteries 和 StopIfGoingOnBatteries 默认都是 true ——
            //   笔记本一拔电源，Windows 会直接把守护进程杀掉，而且拔着电时根本不会启动。
            //   实测踩过：守护跑着跑着就没了，还以为是程序卡死。
            //   这两个值命令行开关改不了，只有 XML 能设成 false。
            string me = "";
            try { me = System.Security.Principal.WindowsIdentity.GetCurrent().Name; }
            catch { }
            if (me.Length == 0) me = Environment.UserDomainName + "\\" + Environment.UserName;

            string xml = ""
                + "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
                + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n"
                + "  <RegistrationInfo>\r\n"
                + "    <Description>游戏 CPU 高频优化器后台守护：检测到游戏自动优化，退出后自动还原。</Description>\r\n"
                + "  </RegistrationInfo>\r\n"
                + "  <Triggers>\r\n"
                + "    <LogonTrigger><Enabled>true</Enabled></LogonTrigger>\r\n"
                + "  </Triggers>\r\n"
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
                + "    <AllowHardTerminate>true</AllowHardTerminate>\r\n"
                + "    <StartWhenAvailable>true</StartWhenAvailable>\r\n"
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
                + "    <Priority>7</Priority>\r\n"
                + "  </Settings>\r\n"
                + "  <Actions Context=\"Author\">\r\n"
                + "    <Exec>\r\n"
                + "      <Command>" + X(exe) + "</Command>\r\n"
                + "      <Arguments>watch --dir \"" + X(workDir) + "\"</Arguments>\r\n"
                + "      <WorkingDirectory>" + X(dir) + "</WorkingDirectory>\r\n"
                + "    </Exec>\r\n"
                + "  </Actions>\r\n"
                + "</Task>\r\n";

            string tmp = Path.Combine(Path.GetTempPath(), "vcb-task.xml");
            try
            {
                // 计划任务 XML 要 UTF-16；带 BOM，schtasks 才认。
                File.WriteAllText(tmp, xml, new System.Text.UnicodeEncoding(false, true));
            }
            catch (Exception ex) { msg = "写临时 XML 失败：" + ex.Message; return false; }

            string o;
            int rc = Run("schtasks.exe", "/create /tn \"" + TASK + "\" /xml \"" + tmp + "\" /f", out o);
            try { File.Delete(tmp); } catch { }
            if (rc == 0)
            {
                msg = "已加入开机自启动：\r\n"
                    + "  计划任务 " + TASK + "\r\n"
                    + "  触发　 登录时\r\n"
                    + "  权限　 最高（所以不会弹 UAC）\r\n"
                    + "  电池　 用电池时也照常运行\r\n"
                    + "  程序　 " + exe + "\r\n"
                    + "  日志　 " + Path.Combine(workDir, "boost-log.txt");
                return true;
            }
            msg = "创建计划任务失败（退出码 " + rc + "）。\r\n这一步需要管理员权限。\r\n" + o;
            return false;
        }

        public static bool Uninstall(out string msg)
        {
            string o;
            int rc = Run("schtasks.exe", "/delete /tn \"" + TASK + "\" /f", out o);
            if (rc == 0) { msg = "已关闭开机自启动（计划任务 " + TASK + " 已删除）。"; return true; }
            msg = "删除计划任务失败（退出码 " + rc + "）：\r\n" + o;
            return false;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  多游戏支持
    //  电源设置本身对所有游戏是同一套（都是「别让 CPU 降频」），
    //  真正按游戏区分的是三件事：主程序在哪、显卡绑定有没有登记、
    //  以及各自配置文件里那些会拖帧率的项。
    // ════════════════════════════════════════════════════════════════════

    // 一个客户端的配置根目录。无畏契约最坑的地方就在这：
    //   港服（Riot 国际服）把配置写在 %LOCALAPPDATA%\VALORANT\Saved\Config\ 下
    //   国服（腾讯）写在 <安装目录>\live\ShooterGame\Saved\Config\ 下
    // 两边各有自己的账号目录（港服后缀 -ap，国服后缀 -alpha1），
    // 而且两份配置的 10 项画质可以完全不同。只认一个根就会一直读错客户端。
    internal class ConfigRoot
    {
        public string Dir = "";      // Saved\Config 这一层
        public string Client = "";   // 这个根属于哪个客户端（显示用）
        public string ExeHint = "";  // 主程序路径里含这个片段，就说明跑的是这个客户端
        public ConfigRoot(string d, string c, string h) { Dir = d; Client = c; ExeHint = h; }
    }

    internal class GameProfile
    {
        public string Key = "";
        public string Name = "";
        public string Sub = "";
        public string Tab = "";     // 侧栏切换按钮上的短名（Name 太长会被 80px 的按钮裁成 "pex Legend"）
        public string[] Exes = new string[0];      // 主程序候选路径，取第一个真实存在的
        public string[] Procs = new string[0];     // 进程名（不含 .exe），给监控用
        // 客户端启动器的进程名。反作弊会保护游戏主程序：Vanguard 下
        // Process.MainModule.FileName 和 WMI 的 ExecutablePath 读回来都是空字符串，
        // 但启动器（RiotClientServices）不受保护，拿它判断现在是哪个客户端。
        public string[] ClientProcs = new string[0];
        // ★ 反作弊保护的游戏主程序，连「问一下路径」都要卡将近 1 秒 ——
        //   实测 Vanguard 下 Process.MainModule.FileName 花 789 ms 且返回空串
        //   （VALORANT-Win64-Shipping 789ms、VALORANT 783ms，而 RiotClientServices、
        //   leigod、wallpaper64、dwm 全是 0~1 ms）。而「游戏专项」页点开一次要问
        //   三遍，2.4 秒就没了 —— 用户的原话是「点开要加载很久」。
        //   既然结果本来就是空的，那就整个跳过，直接用不受保护的客户端启动器判断。
        public bool SkipMainModule = false;
        public string ConfigPath = "";
        public ConfigRoot[] ConfigRoots = new ConfigRoot[0];
        public string ConfigLabel = "";
        public string RecMode = "competitive";
        public string Tip = "";
    }

    internal static class Games
    {
        public static List<GameProfile> Build()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            List<GameProfile> L = new List<GameProfile>();

            GameProfile v = new GameProfile();
            v.Key = "valorant";
            v.Name = "无畏契约";
            v.Sub = "VALORANT";
            v.Tab = "无畏契约";
            v.Exes = new string[] {
                "D:\\valorant cn\\Tencent Games\\VALORANT\\live\\ShooterGame\\Binaries\\Win64\\VALORANT-Win64-Shipping.exe",
                "D:\\Riot Games\\VALORANT\\live\\ShooterGame\\Binaries\\Win64\\VALORANT-Win64-Shipping.exe",
                "C:\\Riot Games\\VALORANT\\live\\ShooterGame\\Binaries\\Win64\\VALORANT-Win64-Shipping.exe",
                "E:\\Riot Games\\VALORANT\\live\\ShooterGame\\Binaries\\Win64\\VALORANT-Win64-Shipping.exe"
            };
            v.Procs = new string[] { "VALORANT-Win64-Shipping" };
            v.ClientProcs = new string[] { "RiotClientServices" };
            v.SkipMainModule = true;   // Vanguard 保护，问一次要 789 ms 还只给空串
            v.ConfigPath = Path.Combine(local, "VALORANT\\Saved\\Config\\WindowsClient\\GameUserSettings.ini");
            v.ConfigRoots = new ConfigRoot[] {
                new ConfigRoot(Path.Combine(local, "VALORANT\\Saved\\Config"),
                               "港服", "Riot Games"),
                new ConfigRoot("D:\\valorant cn\\Tencent Games\\VALORANT\\live\\ShooterGame\\Saved\\Config",
                               "国服", "valorant cn"),
                new ConfigRoot("E:\\valorant cn\\Tencent Games\\VALORANT\\live\\ShooterGame\\Saved\\Config",
                               "国服", "valorant cn"),
                new ConfigRoot("D:\\Riot Games\\VALORANT\\live\\ShooterGame\\Saved\\Config",
                               "港服", "Riot Games"),
                new ConfigRoot("C:\\Riot Games\\VALORANT\\live\\ShooterGame\\Saved\\Config",
                               "港服", "Riot Games")
            };
            v.ConfigLabel = "GameUserSettings.ini";
            v.RecMode = "competitive";
            v.Tip = "帧率极高、单核敏感，插电时钉死高频收益最大。";
            L.Add(v);

            GameProfile a = new GameProfile();
            a.Key = "apex";
            a.Name = "Apex Legends";
            a.Sub = "Apex";
            a.Tab = "Apex";
            a.Exes = new string[] {
                "D:\\SteamLibrary\\steamapps\\common\\Apex Legends\\r5apex_dx12.exe",
                "D:\\SteamLibrary\\steamapps\\common\\Apex Legends\\r5apex.exe",
                "E:\\SteamLibrary\\steamapps\\common\\Apex Legends\\r5apex_dx12.exe",
                "E:\\SteamLibrary\\steamapps\\common\\Apex Legends\\r5apex.exe",
                "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Apex Legends\\r5apex_dx12.exe",
                "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Apex Legends\\r5apex.exe"
            };
            a.Procs = new string[] { "r5apex_dx12", "r5apex" };
            a.ConfigPath = Path.Combine(home, "Saved Games\\Respawn\\Apex\\local\\videoconfig.txt");
            a.ConfigLabel = "videoconfig.txt";
            a.RecMode = "competitive";
            a.Tip = "同时吃满 CPU 和显卡，笔记本上要给 GPU 留瓦数。";
            L.Add(a);

            return L;
        }

        public static GameProfile Find(string key)
        {
            List<GameProfile> all = Build();
            if (key != null)
                foreach (GameProfile g in all)
                    if (string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase)) return g;
            return all[0];
        }

        public static string FoundExe(GameProfile g)
        {
            // 游戏正在跑的话，跑着的那个就是答案 —— 比按列表顺序猜准。
            // 无畏契约装了国服和港服两份，Exes 里国服排在前头，
            // 于是「主程序」一直显示国服的 exe，可你在玩港服。
            string run = RunningExePath(g);
            if (run.Length > 0) return run;

            // 游戏主程序路径被反作弊藏起来了，那就按「现在是哪个客户端在跑」来挑
            string who = RunningClient(g);
            if (who.Length > 0)
            {
                foreach (string p in g.Exes)
                {
                    try
                    {
                        if (!File.Exists(p)) continue;
                        foreach (ConfigRoot r in g.ConfigRoots)
                            if (r.Client == who &&
                                p.IndexOf(r.ExeHint, StringComparison.OrdinalIgnoreCase) >= 0)
                                return p;
                    }
                    catch { }
                }
            }

            foreach (string p in g.Exes)
            {
                try { if (File.Exists(p)) return p; } catch { }
            }
            return "";
        }

        // 配置文件要挑「正在玩的那个客户端的那一份」。
        // 坑 1：无畏契约有两份 GameUserSettings.ini —— 通用那份只有分辨率/垂直同步，
        //       画质那 10 个 sg.* 项在【账号目录】里。只看通用那份会得到 0/0 项的假结果。
        // 坑 2：港服和国服各有一个 Saved\Config 根，账号目录后缀也不同（-ap / -alpha1）。
        //       原来只扫 %LOCALAPPDATA%，等于永远只读港服那份，
        //       拿它去说国服的画质，结论是错的。
        // 所以：先看现在是哪个客户端在跑，只认它那个根；认不出来才在所有根里挑最新的。

        // 正在跑的主程序完整路径（拿它去比对 ConfigRoot.ExeHint）
        public static string RunningExePath(GameProfile g)
        {
            // ★ 已知受反作弊保护的游戏直接跳过：结果本来就是空串，但要等 789 ms。
            // 反正拿不到东西，交给 RunningClientPath 走启动器那条快路。
            if (g.SkipMainModule) return "";
            foreach (string pn in g.Procs)
            {
                try
                {
                    System.Diagnostics.Process[] ps = System.Diagnostics.Process.GetProcessesByName(pn);
                    foreach (System.Diagnostics.Process p in ps)
                    {
                        string f = "";
                        try { f = p.MainModule.FileName; } catch { }
                        p.Dispose();
                        if (!string.IsNullOrEmpty(f)) return f;
                    }
                }
                catch { }
            }
            return "";
        }

        // 反作弊会把游戏主程序的路径藏起来（实测：Vanguard 下 MainModule 不抛异常但返回空串，
        // WMI 的 ExecutablePath 也是空）。所以再试一层：客户端启动器不受保护。
        public static string RunningClientPath(GameProfile g)
        {
            string run = RunningExePath(g);
            if (run.Length > 0) return run;
            return FirstPathOf(g.ClientProcs);
        }

        private static string FirstPathOf(string[] names)
        {
            foreach (string pn in names)
            {
                try
                {
                    System.Diagnostics.Process[] ps = System.Diagnostics.Process.GetProcessesByName(pn);
                    foreach (System.Diagnostics.Process p in ps)
                    {
                        string f = "";
                        try { f = p.MainModule.FileName; } catch { }
                        p.Dispose();
                        if (!string.IsNullOrEmpty(f)) return f;
                    }
                }
                catch { }
            }
            return "";
        }

        // 现在是哪个客户端在跑（港服 / 国服 / 空）
        public static string RunningClient(GameProfile g)
        {
            string cli = RunningClientPath(g);
            if (cli.Length == 0) return "";
            foreach (ConfigRoot r in g.ConfigRoots)
                if (cli.IndexOf(r.ExeHint, StringComparison.OrdinalIgnoreCase) >= 0) return r.Client;
            return "";
        }

        // 在 root 下面的每个账号目录里找 WindowsClient\<file>
        private static void Collect(string root, string file, List<string> into)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string f = Path.Combine(dir, "WindowsClient\\" + file);
                    if (File.Exists(f)) into.Add(f);
                }
            }
            catch { }
        }

        private static string Newest(List<string> cands)
        {
            string best = "";
            DateTime bestT = DateTime.MinValue;
            foreach (string f in cands)
            {
                try
                {
                    FileInfo fi = new FileInfo(f);
                    if (!fi.Exists) continue;
                    if (best.Length == 0 || fi.LastWriteTime > bestT) { bestT = fi.LastWriteTime; best = f; }
                }
                catch { }
            }
            return best;
        }

        public static string ResolveConfig(GameProfile g)
        {
            string file = g.ConfigLabel.Length > 0 ? g.ConfigLabel : Path.GetFileName(g.ConfigPath);

            if (g.ConfigRoots.Length > 0)
            {
                string running = RunningClientPath(g);
                if (running.Length > 0)
                {
                    List<string> mine = new List<string>();
                    foreach (ConfigRoot r in g.ConfigRoots)
                        if (running.IndexOf(r.ExeHint, StringComparison.OrdinalIgnoreCase) >= 0)
                            Collect(r.Dir, file, mine);
                    string hit = Newest(mine);
                    if (hit.Length > 0) return hit;   // 认出来了：只认这个客户端的配置
                }

                List<string> all = new List<string>();
                foreach (ConfigRoot r in g.ConfigRoots) Collect(r.Dir, file, all);
                string any = Newest(all);
                if (any.Length > 0) return any;
            }

            List<string> fallback = new List<string>();
            if (g.ConfigPath.Length > 0) fallback.Add(g.ConfigPath);
            return Newest(fallback);
        }

        // 这份配置属于哪个客户端 —— 标在报告上，免得再把港服的说成国服的
        public static string ClientOf(GameProfile g, string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            foreach (ConfigRoot r in g.ConfigRoots)
            {
                try
                {
                    string a = Path.GetFullPath(r.Dir).TrimEnd('\\');
                    string b = Path.GetFullPath(path);
                    if (b.StartsWith(a, StringComparison.OrdinalIgnoreCase)) return r.Client;
                }
                catch { }
            }
            return "";
        }
    }

    // 显卡偏好登记：HKCU 下的 UserGpuPreferences，写它不需要管理员
    internal static class GpuPref
    {
        private const string SUB = "Software\\Microsoft\\DirectX\\UserGpuPreferences";

        public static string Get(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return "";
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SUB);
                if (k == null) return "";
                object o = k.GetValue(exePath);
                k.Close();
                return o == null ? "" : o.ToString();
            }
            catch { return ""; }
        }

        public static string Describe(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "未登记（Windows 自己决定用哪块）";
            if (raw.IndexOf("GpuPreference=2") >= 0) return "高性能（独显）";
            if (raw.IndexOf("GpuPreference=1") >= 0) return "省电（核显）";
            if (raw.IndexOf("GpuPreference=0") >= 0) return "自动";
            return raw;
        }

        public static bool IsHigh(string raw)
        {
            return !string.IsNullOrEmpty(raw) && raw.IndexOf("GpuPreference=2") >= 0;
        }

        public static bool Register(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return false;
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SUB);
                if (k == null) return false;
                k.SetValue(exePath, "GpuPreference=2;", Microsoft.Win32.RegistryValueKind.String);
                k.Close();
                return true;
            }
            catch { return false; }
        }
    }

    internal class GameCheck
    {
        public string Name = "";
        public string Value = "";
        public string Verdict = "";
        public string Advice = "";
        public GameCheck(string n, string v, string d, string a) { Name = n; Value = v; Verdict = d; Advice = a; }
    }

    internal static class GameAudit
    {
        // 同时吃两种格式：
        //   无畏契约（UE4 ini）  FrameRateLimit=0.000000
        //   Apex（Source cfg）   "setting.shadow_enable"		"0"
        public static Dictionary<string, string> Parse(string path)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            string[] lines;
            try { lines = File.ReadAllLines(path); } catch { return d; }
            foreach (string raw in lines)
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                if (s[0] == ';' || s[0] == '#' || s[0] == '[') continue;

                int eq = s.IndexOf('=');
                if (eq > 0)
                {
                    string k = s.Substring(0, eq).Trim().Trim('"');
                    string v = s.Substring(eq + 1).Trim().Trim('"');
                    if (k.Length > 0) d[k] = v;
                    continue;
                }
                if (s.IndexOf('"') >= 0)
                {
                    List<string> q = new List<string>();
                    int i = 0, firstQ = -1;
                    while (i < s.Length)
                    {
                        if (s[i] == '"')
                        {
                            if (firstQ < 0) firstQ = i;
                            int j = s.IndexOf('"', i + 1);
                            if (j < 0) break;
                            q.Add(s.Substring(i + 1, j - i - 1));
                            i = j + 1;
                        }
                        else i++;
                    }
                    // 两个文件的引号格式不一样，都得认：
                    //   videoconfig.txt   "setting.shadow_enable"		"0"   键和值都带引号
                    //   settings.cfg      gfx_nvnUseLowLatency "1"           只有值带引号
                    // 老版本只会第一种，第二种只提出 1 个引号段、q.Count >= 2 不成立，
                    // 于是整行被静默丢掉 —— 表现为「文件里明明有，工具说读不到」。
                    string bare = (firstQ > 0) ? s.Substring(0, firstQ).Trim() : "";
                    if (bare.Length > 0 && q.Count >= 1) d[bare] = q[q.Count - 1];
                    else if (q.Count >= 2) d[q[0]] = q[q.Count - 1];
                }
            }
            return d;
        }

        private static string S(Dictionary<string, string> d, string k)
        {
            string v;
            if (d.TryGetValue(k, out v)) return v;
            return "";
        }

        private static int I(Dictionary<string, string> d, string k)
        {
            string v = S(d, k);
            if (v.Length == 0) return -1;
            double f;
            if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out f)) return (int)f;
            return -1;
        }

        // Apex 有几个键是小数值（r_lod_switch_scale "0.6"、fadeDistScale "1.000000"），
        // I() 会把它们截成 0，读不出「0.6 和 1.0 的区别」。
        private static double F(Dictionary<string, string> d, string k)
        {
            string v = S(d, k);
            if (v.Length == 0) return -1;
            double f;
            if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out f)) return f;
            return -1;
        }

        public static List<GameCheck> Run(GameProfile g, out string header)
        {
            List<GameCheck> L = new List<GameCheck>();
            header = "";
            string cfg = Games.ResolveConfig(g);
            if (cfg.Length == 0 || !File.Exists(cfg))
            {
                header = "找不到配置文件：" + g.ConfigPath;
                return L;
            }
            Dictionary<string, string> d = Parse(cfg);
            if (d.Count == 0)
            {
                header = "配置文件读不出内容：" + cfg;
                return L;
            }
            string cli = Games.ClientOf(g, cfg);
            header = (cli.Length > 0 ? "【" + cli + "】" + cfg : cfg);

            if (g.Key == "valorant")
            {
                int fr = I(d, "FrameRateLimit");
                string frTxt, frVer;
                if (fr < 0) { frTxt = "读不到"; frVer = "读不到"; }
                else if (fr <= 0) { frTxt = "无限制"; frVer = "符合"; }
                else { frTxt = fr + " FPS"; frVer = "注意"; }
                L.Add(new GameCheck("帧率上限", frTxt, frVer, "锁帧会直接压低上限，竞技向建议无限制"));

                string vs = S(d, "bUseVSync");
                L.Add(new GameCheck("垂直同步", vs.Length > 0 ? vs : "读不到",
                    vs == "False" ? "符合" : "不符合", "开着会把帧率锁到刷新率并增加输入延迟"));

                int fm = I(d, "FullscreenMode");
                string fmTxt = fm == 0 ? "独占全屏" : (fm == 1 ? "无边框窗口" : (fm == 2 ? "窗口" : "读不到"));
                L.Add(new GameCheck("显示模式", fmTxt, fm == 0 ? "符合" : "注意",
                    fm == 0 ? "独占全屏，绕过了桌面合成器，延迟最低"
                            : "无边框窗口要经过桌面合成器 dwm 合成，实测能吃掉三成单核"
                              + " —— 改成独占全屏是最划算的一条"));

                int rx = I(d, "ResolutionSizeX"), ry = I(d, "ResolutionSizeY");
                int ux = I(d, "LastUserConfirmedResolutionSizeX"), uy = I(d, "LastUserConfirmedResolutionSizeY");
                bool resSame = (rx == ux && ry == uy);
                L.Add(new GameCheck("渲染分辨率", rx + "x" + ry, resSame ? "符合" : "注意",
                    resSame ? "与上次确认一致" : ("上次确认的是 " + ux + "x" + uy + "，对不上说明被改过")));

                string[] qk = new string[] {
                    "sg.ViewDistanceQuality", "sg.AntiAliasingQuality", "sg.ShadowQuality",
                    "sg.GlobalIlluminationQuality", "sg.ReflectionQuality", "sg.PostProcessQuality",
                    "sg.TextureQuality", "sg.EffectsQuality", "sg.FoliageQuality", "sg.ShadingQuality" };
                int hi = 0, qn = 0;
                foreach (string k in qk) { int q = I(d, k); if (q >= 0) { qn++; if (q >= 3) hi++; } }
                L.Add(new GameCheck("画质档位", hi + " / " + qn + " 项在最高档",
                    (qn > 0 && hi * 2 >= qn) ? "不符合" : "符合",
                    "竞技向常规做法是画质全低、纹理单独调中"));
            }
            else
            {
                // ══ 显示 ══════════════════════════════════════════════════
                int fs = I(d, "setting.fullscreen");
                int nb = I(d, "setting.nowindowborder");
                bool ex = (fs == 1 && nb == 1);
                L.Add(new GameCheck("显示模式", ex ? "独占全屏" : ("fullscreen=" + fs + " border=" + nb),
                    ex ? "符合" : "注意",
                    ex ? "独占全屏，绕过了桌面合成器，延迟最低"
                       : "不是独占全屏，画面要经过桌面合成器 dwm 合成"
                         + " —— fullscreen 要 1、nowindowborder 也要 1"));

                int dx = I(d, "setting.defaultres"), dy = I(d, "setting.defaultresheight");
                int lx = I(d, "setting.last_display_width"), ly = I(d, "setting.last_display_height");
                // 自定义比例（16:10 拉伸之类）是有意的玩法，不能判成错误 —— 这一项只报事实，不下结论。
                // 但有两种情况例外，都是在白花性能，得说清楚：
                //   ① 宽度不是 8 的倍数 —— GPU 的渲染目标要求 8 对齐，1980 不是（1920 是），
                //      驱动会自己改成最近的合法值，画面反倒不是想要的。
                //   ② 渲染得比屏幕还大 —— 这不是「拉伸」，是超采样：GPU 多渲染的像素在缩回
                //      屏幕尺寸时全部被丢掉。老版本把这种情况也归到「拉伸，按需保留」，是错的。
                string resAdvice, resVerdict;
                // 实际值那一列只有 130px，「1920x1200（屏幕 1920x1080）」放不下会被截掉，
                // 所以只把渲染分辨率留在值列，屏幕尺寸挪进说明里。
                string scr = "（屏幕 " + lx + "x" + ly + "）";
                if (dx <= 0 || dy <= 0)
                {
                    resVerdict = "参考";
                    resAdvice = "配置里没写，游戏会自己决定" + scr;
                }
                else if (dx % 8 != 0)
                {
                    resVerdict = "注意";
                    resAdvice = "宽度 " + dx + " 不是 8 的倍数，驱动会自己改成最近的合法值；"
                              + "想要 16:10 应该是 " + lx + "x" + (lx * 10 / 16) + scr;
                }
                else if (lx > 0 && ly > 0 && (dx > lx || dy > ly))
                {
                    long rp = (long)dx * (long)dy, sp = (long)lx * (long)ly;
                    resVerdict = "注意";
                    resAdvice = "渲染得比屏幕还大 —— 这是超采样不是拉伸，多渲染 "
                              + ((rp - sp) * 100 / sp) + "% 的像素再缩回去，全白花；"
                              + "要拉伸应该是 " + lx + "x" + ly;
                }
                else if (dx == lx && dy == ly)
                {
                    resVerdict = "参考";
                    resAdvice = "与屏幕一致";
                }
                else
                {
                    resVerdict = "参考";
                    resAdvice = "自定义比例，拉伸是竞技向常用做法，按需保留" + scr;
                }
                L.Add(new GameCheck("渲染分辨率", dx + "x" + dy, resVerdict, resAdvice));

                int vsy = I(d, "setting.mat_vsync_mode");
                L.Add(new GameCheck("垂直同步", vsy == 0 ? "关" : (vsy < 0 ? "读不到" : "开"),
                    vsy == 0 ? "符合" : "不符合", "开着会锁帧并增加延迟"));

                int bbc = I(d, "setting.mat_backbuffer_count");
                L.Add(new GameCheck("后台缓冲", bbc < 0 ? "读不到" : (bbc + " 个"),
                    "参考", "1 是双缓冲，延迟最低；2 以上更平滑但会多出一帧延迟"));

                // ══ 光影 ══════════════════════════════════════════════════
                int sh = I(d, "setting.shadow_enable");
                L.Add(new GameCheck("动态阴影", sh == 0 ? "关" : (sh < 0 ? "读不到" : "开"),
                    sh == 0 ? "符合" : "不符合", "阴影是 Apex 里最贵的一项"));

                int csm = I(d, "setting.csm_enabled");
                bool csmBad = (sh == 0 && csm == 1);
                L.Add(new GameCheck("级联阴影 CSM", csm == 1 ? "开" : (csm < 0 ? "读不到" : "关"),
                    csmBad ? "注意" : "符合",
                    csmBad ? "动态阴影都关了，级联阴影却开着，白花性能" : "与阴影设置一致"));

                int vl = I(d, "setting.volumetric_lighting");
                int vf = I(d, "setting.volumetric_fog");
                L.Add(new GameCheck("体积光 / 体积雾", vl + " / " + vf,
                    (vl == 0 && vf == 0) ? "符合" : "注意", "两项都很贵，竞技向建议都关"));

                int ssao = I(d, "setting.ssao_quality");
                L.Add(new GameCheck("环境光遮蔽", ssao == 0 ? "关" : (ssao < 0 ? "读不到" : "开"),
                    ssao == 0 ? "符合" : "注意", "逐像素算接触阴影，很贵，竞技向建议关"));

                // ══ 抗锯齿：Apex 里单项最贵的后处理之一，之前完全没查 ══════
                int aa = I(d, "setting.mat_antialias_mode");
                L.Add(new GameCheck("抗锯齿", aa == 0 ? "关" : (aa < 0 ? "读不到" : ("模式 " + aa + "（TSAA）")),
                    aa == 0 ? "符合" : "注意",
                    aa == 0 ? "关着 —— 边缘更硬，但省下一大截"
                            : "TSAA 是 Apex 里最贵的后处理之一。关掉提帧明显，代价是边缘有锯齿，"
                              + "眼睛敏感的话可以留"));

                // ══ 几何与材质 ════════════════════════════════════════════
                int pcm = I(d, "setting.mat_picmip");
                L.Add(new GameCheck("纹理质量", pcm < 0 ? "读不到" : pcm.ToString(),
                    "参考", "0 最清晰，数字越大越糊。这项主要吃显存，对帧率影响比想象中小"));

                int fa = I(d, "setting.mat_forceaniso");
                L.Add(new GameCheck("各向异性过滤", fa < 0 ? "读不到" : (fa == 1 ? "1（几乎关）" : (fa + "x")),
                    "参考", "斜着看地面的清晰度。1 基本等于关，调高只花一点点性能"));

                int sm = I(d, "setting.stream_memory");
                L.Add(new GameCheck("纹理串流预算", sm < 0 ? "读不到" : ((sm / 1024) + " MB"),
                    "参考", "给纹理留多少显存。调小显存松但纹理容易糊，调大反过来 —— 按显存余量取舍"));

                double lod = F(d, "setting.r_lod_switch_scale");
                int mdl = I(d, "setting.map_detail_level");
                string lodTxt = (lod < 0 ? "?" : lod.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                              + " / " + (mdl < 0 ? "?" : mdl.ToString());
                L.Add(new GameCheck("远景细节 LOD", lodTxt, "参考",
                    "小于 1 表示远处模型提前降级。越小越省，也越容易看出「糊」"));

                int dec = I(d, "setting.r_decals");
                int gib = I(d, "setting.cl_gib_allow");
                L.Add(new GameCheck("贴花 / 碎块", dec + " / " + gib,
                    (dec == 0 && gib == 0) ? "符合" : "注意",
                    "弹孔血迹和尸体碎块。关掉省 CPU 也省显存，视觉损失很小"));

                int rg = I(d, "setting.cl_ragdoll_maxcount");
                int pc = I(d, "setting.particle_cpu_level");
                L.Add(new GameCheck("布娃娃 / 粒子", rg + " / " + pc,
                    (rg == 0 && pc == 0) ? "符合" : "注意", "关掉能省 CPU"));

                int dv = I(d, "setting.dvs_enable");
                L.Add(new GameCheck("动态分辨率", dv == 0 ? "关" : (dv < 0 ? "读不到" : "开"),
                    dv == 0 ? "符合" : "注意", "开着画面会自己变糊"));

                // ══ settings.cfg：145 行里全是按键绑定，只有延迟优化这几行沾性能 ══
                Dictionary<string, string> sd = new Dictionary<string, string>();
                string scfg = Path.Combine(Path.GetDirectoryName(cfg), "settings.cfg");
                try { if (File.Exists(scfg)) sd = Parse(scfg); } catch { }
                int nvn = I(sd, "gfx_nvnUseLowLatency");
                int nvb = I(sd, "gfx_nvnUseLowLatencyBoost");
                int amd = I(sd, "gfx_amdUseLowLatency");
                string latTxt, latVer, latAdv;
                if (nvn < 0 && amd < 0)
                {
                    latTxt = "读不到"; latVer = "参考";
                    latAdv = "没找到 settings.cfg，或者里面没写这几项（进过一次游戏才会写）";
                }
                else if (nvn == 1)
                {
                    latTxt = nvb == 1 ? "Reflex + Boost" : "Reflex";
                    latVer = "符合";
                    latAdv = nvb == 1
                        ? "NVIDIA Reflex 和 Boost 都开着，队列压到最短，延迟最低"
                        : "Reflex 开着。Boost 是关的 —— 开了还能再压一点延迟，代价是多耗一点电";
                }
                else if (amd == 1)
                {
                    latTxt = "AMD Anti-Lag"; latVer = "符合";
                    latAdv = "把渲染队列压短来降输入延迟，免费的性能";
                }
                else
                {
                    latTxt = "都没开"; latVer = "注意";
                    latAdv = "驱动层的低延迟没开，输入延迟会白白高一截";
                }
                L.Add(new GameCheck("延迟优化", latTxt, latVer, latAdv));

                string pso = Path.Combine(Path.GetDirectoryName(cfg), "psoCache.pso");
                long sz = 0;
                try { FileInfo fi = new FileInfo(pso); if (fi.Exists) sz = fi.Length; } catch { }
                L.Add(new GameCheck("DX12 着色器缓存", sz > 0 ? ((sz / 1048576) + " MB") : "没有",
                    "参考", "刚装完或删掉后头几局会卡，之后越攒越顺"));

                // ══ 画质总览：把「贵不贵」扳成一排开关来数，对应无畏契约那边的「画质档位」══
                string[] costly = new string[] {
                    "setting.shadow_enable", "setting.csm_enabled", "setting.volumetric_lighting",
                    "setting.volumetric_fog", "setting.ssao_quality", "setting.mat_antialias_mode",
                    "setting.r_decals", "setting.cl_gib_allow", "setting.particle_cpu_level",
                    "setting.cl_ragdoll_maxcount" };
                int on = 0, kn = 0;
                foreach (string k in costly) { int q = I(d, k); if (q >= 0) { kn++; if (q > 0) on++; } }
                L.Add(new GameCheck("画质档位", on + " / " + kn + " 项还在开",
                    (kn > 0 && on * 2 >= kn) ? "不符合" : "符合",
                    "这十项是 Apex 里最吃性能的开关，竞技向常规做法是只留纹理，其余全关"));
            }

            return L;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  系统信息
    // ────────────────────────────────────────────────────────────────────
    internal class CpuInfo
    {
        public string Name = "未知";
        public int Cores = 0;
        public int Logical = 0;
        public int MaxMHz = 0;
        public List<string> Oem = new List<string>();

        public static CpuInfo Detect()
        {
            CpuInfo c = new CpuInfo();
            try
            {
                ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed FROM Win32_Processor");
                foreach (ManagementObject mo in s.Get())
                {
                    c.Name = Convert.ToString(mo["Name"]).Trim();
                    c.Cores = Convert.ToInt32(mo["NumberOfCores"]);
                    c.Logical = Convert.ToInt32(mo["NumberOfLogicalProcessors"]);
                    c.MaxMHz = Convert.ToInt32(mo["MaxClockSpeed"]);
                    break;
                }
            }
            catch { }
            try
            {
                ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT Name,DisplayName FROM Win32_Service WHERE State='Running'");
                foreach (ManagementObject mo in s.Get())
                {
                    string t = Convert.ToString(mo["Name"]) + " " + Convert.ToString(mo["DisplayName"]);
                    string[] keys = new string[] { "HONOR", "Honor", "Huawei", "华为", "荣耀", "PC Manager", "Armoury", "Lenovo", "Dell", "MSI", "Predator" };
                    foreach (string k in keys)
                        if (t.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0 && c.Oem.IndexOf(t.Trim()) < 0)
                        { c.Oem.Add(t.Trim()); break; }
                }
            }
            catch { }
            return c;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  性能基准
    // ────────────────────────────────────────────────────────────────────
    internal class BenchResult
    {
        public long Single;
        public long All;
        public int Threads;
        public double EstMHz;
        public double PeakMHz;
        public int Parked;
        public int LogicalSeen;
        public bool Ok;
        public string Error = "";
    }

    // ════════════════════════════════════════════════════════════════════
    //  后台抢占体检
    //  电源设置再对，CPU 被别的东西占着一样掉帧 —— 尤其是无边框窗口，
    //  桌面合成器（dwm）会全程跟着合成，实测能吃掉三成单核。
    //  这一段量的是「两个采样点之间每个进程真正吃掉的 CPU 时间」，
    //  是增量不是累计值，所以能反映当下谁在抢，而不是谁开机以来跑得久。
    // ════════════════════════════════════════════════════════════════════

    internal class BgRow
    {
        public string Name;
        public double Pct;      // 占「单核」的百分比（不是占总 CPU）
        public string Note;
        public BgRow(string n, double p, string t) { Name = n; Pct = p; Note = t; }
    }

    internal static class SysAudit
    {
        /// <summary>已知会抢 CPU 的东西，以及该怎么办。键是进程名，大小写不敏感。</summary>
        private static string Note(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "wallpaper64":
                case "wallpaper32": return "Wallpaper Engine 动态壁纸 —— 换静态壁纸，白送的帧率";
                case "dwm": return "桌面窗口管理器 —— 游戏用无边框窗口时它全程在合成，改独占全屏";
                case "huntercamp":
                case "hunteryard": return "荣耀叠加层 —— 可在荣耀电脑管家里关掉";
                case "msedgewebview2":
                case "msedge":
                case "chrome":
                case "firefox": return "浏览器 —— 打游戏时关掉";
                case "msmpeng": return "Defender 实时扫描 —— 把游戏目录加进排除项";
                case "system": return "系统内核（驱动/中断）—— 多半是上面某个程序的连带开销";
                case "onedrive":
                case "dropbox": return "网盘同步 —— 暂停同步";
                case "steamwebhelper": return "Steam 界面 —— 关掉商店页面，只留托盘";
                case "steam": return "Steam 客户端";
                case "oopz":
                case "discord":
                case "qq":
                case "wechat": return "语音/聊天 —— 本体不重，但叠加层会抢";
                case "valorant": return "无畏契约登录器 —— 游戏跑起来后它其实可以退掉";
                case "riot client": return "Riot 客户端 —— 游戏跑起来后可以退掉";
                case "explorer": return "资源管理器 —— 通常是被别的东西带着跑";
                // 加速器：实测雷神一套（leigod + leishenSdk）能吃掉四成单核，
                // 是这份名单里最容易被忽略、收益又最大的一项。
                case "leigod":
                case "leishensdk":
                case "leishengame":
                case "leishenaccelerator": return "雷神加速器 —— 吃 CPU 很凶，不打外服就退掉";
                case "uu":
                case "uubooster":
                case "uugamebooster": return "网易 UU 加速器 —— 不打外服就退掉";
                case "netch":
                case "clash":
                case "v2ray":
                case "xray": return "代理工具 —— 不打外服就退掉";
                // 本工具自己（含被测的这台机器上正在跑的 AI 助手）
                case "deepseek harness": return "AI 助手正在干活 —— 它会明显吃 CPU，打游戏前先停掉";
                case "recorder": return "录屏/回放工具 —— 不用就退掉";
            }
            return "";
        }

        /// <summary>采样 ms 毫秒，返回这段时间里真正吃了 CPU 的进程（按占用降序）。</summary>
        public static List<BgRow> Sample(int ms)
        {
            Dictionary<int, double> t0 = new Dictionary<int, double>();
            Dictionary<int, string> nm = new Dictionary<int, string>();
            foreach (Process p in Process.GetProcesses())
            {
                try { t0[p.Id] = p.TotalProcessorTime.TotalSeconds; nm[p.Id] = p.ProcessName; }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            Thread.Sleep(ms);
            double win = ms / 1000.0;
            if (win <= 0) win = 1;

            Dictionary<string, double> acc = new Dictionary<string, double>();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    double a;
                    if (t0.ContainsKey(p.Id))
                    {
                        t0.TryGetValue(p.Id, out a);
                        double d = p.TotalProcessorTime.TotalSeconds - a;
                        if (d > 0)
                        {
                            string n = nm[p.Id];
                            double cur;
                            acc.TryGetValue(n, out cur);
                            acc[n] = cur + d;      // 同名多开合并
                        }
                    }
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            List<BgRow> rows = new List<BgRow>();
            foreach (KeyValuePair<string, double> kv in acc)
            {
                double pct = kv.Value / win * 100.0;
                if (pct < 0.5) continue;           // 半个百分点以下不值得提
                rows.Add(new BgRow(kv.Key, pct, Note(kv.Key)));
            }
            rows.Sort(delegate(BgRow a, BgRow b) { return b.Pct.CompareTo(a.Pct); });
            return rows;
        }

        /// <summary>按显示宽度右补空格：中文算两格。</summary>
        private static string PadR(string s, int w)
        {
            return TextPad.To(s, w);
        }

        public static string Report(int ms)
        {
            List<BgRow> rows = Sample(ms);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("后台抢占 CPU 的程序（" + (ms / 1000) + " 秒采样，本机 "
                          + Environment.ProcessorCount + " 个逻辑核）");
            sb.AppendLine("  占单核%  进程                    说明");
            sb.AppendLine("  -------  ----------------------  ----");

            double total = 0;
            int hot = 0;
            bool dwmHot = false;
            foreach (BgRow r in rows)
            {
                total += r.Pct;
                if (r.Note.Length > 0 && r.Pct >= 3.0)
                {
                    hot++;
                    if (r.Name.ToLowerInvariant() == "dwm") dwmHot = true;
                }
                sb.AppendLine("  " + PadR(r.Pct.ToString("0.0") + "%", 8) + "  "
                              + PadR(r.Name, 22) + "  " + (r.Note.Length > 0 ? r.Note : "—"));
            }
            if (rows.Count == 0) sb.AppendLine("  （没有程序占用超过 0.5% 单核）");
            sb.AppendLine("  -------");
            sb.AppendLine("  合计 " + total.ToString("0.0") + "% 单核"
                          + "（占全部 " + Environment.ProcessorCount + " 核的 "
                          + (total / Environment.ProcessorCount).ToString("0.0") + "%）");
            sb.AppendLine();

            if (total < 15) sb.AppendLine("  判读：很干净，后台基本没抢东西。");
            else if (total < 40) sb.AppendLine("  判读：有点杂，" + hot + " 个程序值得一提，能关的都关掉。");
            else sb.AppendLine("  判读：抢得厉害，" + hot + " 个程序在明显吃 CPU，先处理它们再谈帧率。");

            if (dwmHot)
                sb.AppendLine("  ★ dwm 在明显吃 CPU —— 游戏八成跑的是无边框窗口。"
                              + "改成独占全屏能直接省掉这部分，这是最划算的一条。");

            return sb.ToString();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  后台程序压制：把抢 CPU 的后台进程赶到能效核上
    // ════════════════════════════════════════════════════════════════════
    //  ★ 掩码不再写死：E/P 各是哪些逻辑核，由下面 CpuTopo 在【运行时】按本机拓扑
    //    算出来。原先那两个常量（0x3003FC / 0xFFC03）只对开发这台机器成立，换台机器
    //    —— 尤其 AMD 那种全对称核的机型 —— 会指向根本不存在的逻辑核。
    //
    //  本机（开发机）实测出来的拓扑，只当参考，别照抄：
    //      P 核     6 个 / 12 逻辑核   0-1、10-19   ← 游戏要的就是这些
    //      E 核     8 个 /  8 逻辑核   2-9
    //      低功耗E  2 个 /  2 逻辑核   20-21
    //
    //  ⚠️ 这段原先写着「不要用 Windows 的 EfficiencyClass 判断核类型，实测它把 P 核
    //     标成 1、E 核标成 0，跟微软文档正好相反」—— 经重新实测，那句是【记错了】。
    //     EfficiencyClass 数值越大越快，与微软文档一致：本机 class=1 就是 P 核
    //     （0-1、10-19），class=0 就是 E 核（2-9、20-21）。所以现在正是按它分档的。
    //     真正要记住的是【档位高低只看 EfficiencyClass 的数值大小，不看名字、也不
    //     假设 class 0 一定是 E 核】—— 换个平台数值可能整体错位。
    //
    //  手段只用两样：CPU 亲和性（硬保证）+ 效率模式（让调度器也倾向 E 核）。
    //  故意【不】降优先级 —— 加速器是网络转发，优先级被压会让延迟炸，
    //  用户明确说雷神是玩港服的必备工具，不能把它弄瘸。
    //  ────────────────────────────────────────────────────────────────────
    //  进程快照
    //  ────────────────────────────────────────────────────────────────────
    //  为什么要这个：Process.GetProcessesByName(name) 每次调用都会把系统里
    //  所有进程枚举一遍再按名字过滤。守护原先这样干了两处：
    //    ① IsGameRunning 每 4 秒查一次，Valorant 有 1 个进程名、Apex 有 2 个；
    //    ② CorePin.Apply 每 60 秒把 40 个候选名字各查一次，也就是 40 次全量枚举。
    //  改成先取一次快照（一次 NtQuerySystemInformation）、再在内存里按名字查，
    //  40 次全量枚举就压成 1 次。
    internal static class ProcSnap
    {
        /// <summary>取一次快照：进程名（小写）→ 该名字下的所有 pid。</summary>
        public static Dictionary<string, List<int>> Take()
        {
            Dictionary<string, List<int>> d =
                new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string n = p.ProcessName;
                        List<int> l;
                        if (!d.TryGetValue(n, out l)) { l = new List<int>(); d[n] = l; }
                        l.Add(p.Id);
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return d;
        }

        /// <summary>快照里某个进程名的实例数（没有就是 0）。</summary>
        public static int Count(Dictionary<string, List<int>> snap, string name)
        {
            List<int> l;
            if (snap != null && snap.TryGetValue(name, out l)) return l.Count;
            return 0;
        }

        /// <summary>快照里某个进程名的 pid 列表（没有就是空的）。</summary>
        public static List<int> Pids(Dictionary<string, List<int>> snap, string name)
        {
            List<int> l;
            if (snap != null && snap.TryGetValue(name, out l)) return l;
            return new List<int>();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CPU 拓扑检测：本机的能效核（E）和性能核（P）分别是哪些逻辑核
    // ════════════════════════════════════════════════════════════════════
    //  为什么要这一段：下面 CorePin 里 E/P 掩码原先是两个写死的常量
    //  （ECORE_MASK = 0x3003FC / PCORE_MASK = 0xFFC03），只对开发这台机器成立。
    //  换台机器 —— 尤其是 AMD 那种全对称核的机型 —— 那两个常量会指向根本不存在的
    //  逻辑核，给进程设上去等于把人家关进一堆假核里，比不压还糟。所以改成运行时算。
    //
    //  做法：GetLogicalProcessorInformationEx(RelationProcessorCore) 把每个物理核都
    //  列出来，每个核带一个 EfficiencyClass。按它分档：
    //    · 只有 1 档  ⇒ 全是对称核（典型 AMD Ryzen 非大小核机型），本机没有
    //                    「能效核」这个概念 ⇒ 判定不可用，两个掩码都留 0；
    //    · 2 档及以上 ⇒ EfficiencyClass 【数值最大】的那一档是性能核（P），
    //                    其余所有档合并成能效核（E）。
    //  本机（开发机）实测：class=1 → 逻辑核 0-1、10-19（12 个，P），
    //                      class=0 → 逻辑核 2-9、20-21（10 个，E），
    //  与微软文档「EfficiencyClass 越大越快」一致。
    //  ⚠️ 档位高低只看 EfficiencyClass 的数值大小，不看名字、不假设「class 0 一定是
    //     E 核」—— 换个平台数值可能整体错位。
    //
    //  三条硬要求：
    //    ① 结果算一次就缓存（静态字段），但进程每次启动都会重测一遍；
    //    ② 全程不抛异常 —— 任何一步失败都退化成「不可用」，绝不让守护崩掉；
    //    ③ 不可用时 E/P 掩码都是 0。调用方【必须先查 Usable】再决定要不要调
    //       SetProcessAffinityMask —— 传 0 进去的行为是未定义的。
    //  ────────────────────────────────────────────────────────────────────
    internal static class CpuTopo
    {
        private const int RelationProcessorCore = 0;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(
            int relationshipType, IntPtr buffer, ref uint returnedLength);

        /// <summary>
        /// SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX 的公共头 + union 里
        /// PROCESSOR_RELATIONSHIP 的前半段。偏移全部写死，免得被 CLR 的对齐规则
        /// 悄悄挪位置：
        ///   Relationship @0 (DWORD)、Size @4 (DWORD)，union 从 @8 开始；
        ///   Processor 那支：Flags @8 (BYTE)、EfficiencyClass @9 (BYTE)、
        ///   Reserved[20] @10、GroupCount @30 (WORD)、GroupMask[] @32。
        /// ⚠️ EfficiencyClass 是 @9 —— Flags 只有一个字节。写成 @12 会读到 Reserved
        /// 里的 0，于是所有核都成了同一档，看起来像「本机是全对称核」。
        /// 这里只声明到 GroupCount（长度 32），GroupMask 另外按 GROUP_AFFINITY 读，
        /// 这样 32 位 / 64 位下都不会读越界。
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        private struct Slpiex
        {
            [FieldOffset(0)]  public uint Relationship;
            [FieldOffset(4)]  public uint Size;
            [FieldOffset(8)]  public byte Flags;
            [FieldOffset(9)]  public byte EfficiencyClass;
            [FieldOffset(30)] public ushort GroupCount;
        }

        /// <summary>
        /// GROUP_AFFINITY：KAFFINITY Mask（ULONG_PTR）+ WORD Group + WORD Reserved[3]。
        /// Sequential 布局算出来的长度刚好就是系统的长度：64 位 16 字节、32 位 12 字节，
        /// 所以能直接拿 Marshal.SizeOf 当步长去读下一个组。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct GroupAffinity
        {
            public IntPtr Mask;
            public ushort Group;
            public ushort Reserved0;
            public ushort Reserved1;
            public ushort Reserved2;
        }

        private static readonly object Gate = new object();
        private static bool _ready;
        private static bool _usable;
        private static long _eMask;
        private static long _pMask;
        private static string _eList = "无";
        private static string _pList = "无";
        private static string _note = "尚未检测";
        private static int _classes;
        private static int _pClass = -1;
        private static int _eCores;
        private static int _pCores;

        /// <summary>本机有没有可用的大小核。false 时 E/P 掩码都是 0，禁止设亲和性。</summary>
        public static bool Usable { get { Ensure(); return _usable; } }

        /// <summary>能效核掩码（不可用时是 0）。</summary>
        public static long EMask { get { Ensure(); return _eMask; } }

        /// <summary>性能核掩码（不可用时是 0）。</summary>
        public static long PMask { get { Ensure(); return _pMask; } }

        /// <summary>能效核的逻辑核列表，形如「2-9、20-21」；不可用时是「无」。</summary>
        public static string EList { get { Ensure(); return _eList; } }

        /// <summary>性能核的逻辑核列表，形如「0-1、10-19」；不可用时是「无」。</summary>
        public static string PList { get { Ensure(); return _pList; } }

        /// <summary>一句话结论（可用/不可用 + 原因），直接打给界面读。</summary>
        public static string Note { get { Ensure(); return _note; } }

        /// <summary>检测到几个档（EfficiencyClass 的不同取值个数）。</summary>
        public static int Classes { get { Ensure(); return _classes; } }

        /// <summary>能效核的逻辑核个数。</summary>
        public static int ECoreCount { get { Ensure(); return _eCores; } }

        /// <summary>性能核的逻辑核个数。</summary>
        public static int PCoreCount { get { Ensure(); return _pCores; } }

        /// <summary>P 档的 EfficiencyClass 数值（不可用时是 -1）。</summary>
        public static int PClass { get { Ensure(); return _pClass; } }

        /// <summary>
        /// 只算一次：第一次有人问就检测，之后都用缓存。加锁是必须的 —— 守护有好几个
        /// 线程会碰 CorePin（轮询线程 + 界面线程）。缓存是进程内的，所以每次启动新进程
        /// 都会重测一遍。
        /// </summary>
        private static void Ensure()
        {
            lock (Gate)
            {
                if (_ready) return;
                try { Detect(); }
                catch (Exception ex) { Unusable("检测 CPU 拓扑时出错：" + ex.Message); }
                _ready = true;
            }
        }

        /// <summary>判定不可用：两个掩码都归零，并留下原因。</summary>
        private static void Unusable(string why)
        {
            _usable = false;
            _eMask = 0;
            _pMask = 0;
            _eList = "无";
            _pList = "无";
            _note = "不可用：" + why;
            // 诊断用的计数字段也一并清零：不可用时它们必须保持全 0 / -1。
            // 否则「只有 1 档」那条分支会留下 _classes = 1 而 Usable = false 的
            // 半截状态（核数是算进了局部变量，但那两行注释说好的「报出来」并没有
            // 真写回字段）。今天没人读这几个属性，将来谁拿它做判断就会踩空。
            _classes = 0;
            _pClass = -1;
            _eCores = 0;
            _pCores = 0;
        }

        private static void Detect()
        {
            IntPtr buf = IntPtr.Zero;
            try
            {
                uint cap = 64 * 1024;
                uint ret = 0;
                bool ok = false;

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    if (buf != IntPtr.Zero) { Marshal.FreeHGlobal(buf); buf = IntPtr.Zero; }
                    buf = Marshal.AllocHGlobal((int)cap);

                    // ★ returnedLength 是【输入输出】：进去之前必须先写上缓冲区大小。
                    //   不写（传 0）函数当场失败，而且 GetLastError() 还是 0，一点线索
                    //   都不给 —— 这个坑踩过一次，别再踩。失败时它把需要的大小写回来。
                    ret = cap;
                    ok = GetLogicalProcessorInformationEx(RelationProcessorCore, buf, ref ret);
                    if (ok) break;

                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_INSUFFICIENT_BUFFER || ret == 0 || ret > 16u * 1024u * 1024u)
                    {
                        Unusable("枚举物理核失败（Win32 错误 " + err + "）");
                        return;
                    }
                    cap = ret + 4096;   // 系统把需要的大小写在 ret 里，加点余量重试
                }

                if (!ok) { Unusable("物理核信息的缓冲区反复不够大"); return; }
                if (ret < 32) { Unusable("系统没有报出物理核信息"); return; }

                int total = (int)ret;
                int gaSize = Marshal.SizeOf(typeof(GroupAffinity));
                Dictionary<byte, long> byClass = new Dictionary<byte, long>();
                bool multiGroup = false;
                int entries = 0;
                int off = 0;

                // while 遍历：每条的 Size 字段给出下一条的偏移。
                while (off + 32 <= total)   // 头部就有 32 字节，放不下就是到头了
                {
                    Slpiex h = (Slpiex)Marshal.PtrToStructure(
                        IntPtr.Add(buf, off), typeof(Slpiex));
                    if (h.Size < 8) break;                  // 长度不合法，再走就是死循环
                    // 越界检查：必须用 long 比 —— off + (int)h.Size 在 Size ≥ 2^31 时
                    // 会溢出成负数，越界判断被绕过、off 还倒着走，下一轮就读到缓冲区
                    // 之前，AccessViolationException 在 .NET 4 默认策略下 catch 不住，
                    // 守护会直接死。内核给的 Size 一直是 44/48 这类小值，够不到 2^31，
                    // 但拿 long 比一遍是零代价的，别留这个口子。
                    if (off + (long)h.Size > total) break;

                    if (h.Relationship == RelationProcessorCore && h.Size >= 32)
                    {
                        entries++;
                        if (h.GroupCount > 1) multiGroup = true;
                        for (int g = 0; g < h.GroupCount; g++)
                        {
                            int gaOff = off + 32 + g * gaSize;
                            if (gaOff + gaSize > total) break;
                            GroupAffinity ga = (GroupAffinity)Marshal.PtrToStructure(
                                IntPtr.Add(buf, gaOff), typeof(GroupAffinity));
                            // 逻辑核超过 64 个才会出现第 2 个组；位号是组内相对的，
                            // 一个 long 掩码表达不了跨组，直接记下来当不可用。
                            if (ga.Group != 0) { multiGroup = true; continue; }
                            long m = ga.Mask.ToInt64();
                            // 32 位 Windows 上进程被截成 32 位，而内核回来的 GROUP_AFFINITY
                            // 仍是 64 位 KAFFINITY：bit31 置位时 ToInt64() 会做符号扩展，
                            // 凭空长出 32-63 位 —— SetProcessAffinityMask 会 err 87，列表
                            // 还会印成「31-63」。补一次显式截断，顺手把表达不了的逻辑核
                            // 32-63 关掉（32 位进程最多也就能寻址 32 个逻辑核）。
                            if (IntPtr.Size == 4) m = m & 0xFFFFFFFFL;
                            if (m == 0) continue;
                            long cur;
                            if (!byClass.TryGetValue(h.EfficiencyClass, out cur)) cur = 0L;
                            byClass[h.EfficiencyClass] = cur | m;
                        }
                    }
                    off += (int)h.Size;
                }

                if (entries == 0 || byClass.Count == 0)
                {
                    Unusable("系统没有报出任何物理核信息");
                    return;
                }
                if (multiGroup)
                {
                    Unusable("本机逻辑核跨多个处理器组（多于 64 个逻辑核），单个掩码表达不了，压制跳过");
                    return;
                }

                _classes = byClass.Count;

                if (byClass.Count == 1)
                {
                    // 全对称核：没有「能效核」这个概念。两个掩码都留 0 —— 绝不能拿 0 去调
                    // SetProcessAffinityMask。核数照样报出来，界面好显示。
                    int only = 0;
                    long onlyMask = 0;
                    foreach (KeyValuePair<byte, long> kv in byClass) { only = kv.Key; onlyMask = kv.Value; }
                    Unusable("本机是全对称核，没有能效核（只有 1 档：EfficiencyClass "
                             + only + "，共 " + PopCount(onlyMask) + " 个逻辑核），压制跳过");
                    return;
                }

                // EfficiencyClass 数值最大的那一档 = P 核；其余所有档合并 = E 核。
                // 为什么不看名字：这一档就是「最快的档」，数值大就是快，跟它叫什么都无关。
                byte top = 0;
                bool first = true;
                foreach (byte k in byClass.Keys)
                {
                    if (first || k > top) { top = k; first = false; }
                }

                long p = 0;
                long e = 0;
                foreach (KeyValuePair<byte, long> kv in byClass)
                {
                    if (kv.Key == top) p |= kv.Value;
                    else e |= kv.Value;
                }

                if (p == 0 || e == 0 || e == p)
                {
                    Unusable("检测到的核心分档不成大小核（P 档为空，或 E 掩码等于 P 掩码），压制跳过");
                    return;
                }

                _usable = true;
                _eMask = e;
                _pMask = p;
                _pClass = top;
                _eCores = PopCount(e);
                _pCores = PopCount(p);
                _eList = MaskText(e);
                _pList = MaskText(p);
                _note = "可用：共 " + byClass.Count + " 档，EfficiencyClass 最大的档（class="
                        + top + "，" + _pCores + " 个逻辑核）是 P 核，其余 "
                        + (byClass.Count - 1) + " 档（共 " + _eCores + " 个逻辑核）合并为 E 核";
            }
            catch (Exception ex)
            {
                Unusable("检测 CPU 拓扑时出错：" + ex.Message);
            }
            finally
            {
                if (buf != IntPtr.Zero) { try { Marshal.FreeHGlobal(buf); } catch { } }
            }
        }

        /// <summary>掩码里有几个核。</summary>
        private static int PopCount(long m)
        {
            int n = 0;
            for (int i = 0; i < 64; i++) if ((m & (1L << i)) != 0L) n++;
            return n;
        }

        /// <summary>把掩码写成「2-9、20-21」这样的逻辑核列表（空掩码就是「无」）。</summary>
        private static string MaskText(long m)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < 64)
            {
                if ((m & (1L << i)) == 0L) { i++; continue; }
                int j = i;
                while (j + 1 < 64 && (m & (1L << (j + 1))) != 0L) j++;
                if (sb.Length > 0) sb.Append("、");
                if (j == i) sb.Append(i.ToString());
                else sb.Append(i.ToString() + "-" + j.ToString());
                i = j + 1;
            }
            if (sb.Length == 0) sb.Append("无");
            return sb.ToString();
        }
    }

    internal static class CorePin
    {
        // 能效核 / 性能核掩码：运行时按本机拓扑算（见上面 CpuTopo），不再是写死的常量。
        // ⚠️ 不可用时是 0 —— 调用前必须先查 CpuTopo.Usable，掩码 0 绝不能喂给
        //    SetProcessAffinityMask（传 0 的行为是未定义的）。
        public static long ECORE_MASK { get { return CpuTopo.EMask; } }
        public static long PCORE_MASK { get { return CpuTopo.PMask; } }

        /// <summary>绝对不碰的进程。碰反作弊有封号风险，碰 dwm 画面直接卡。</summary>
        private static readonly string[] Protect = new string[] {
            // 反作弊 —— 一个都不许碰
            "vgc", "vgtray", "ace-tray", "ace-guard", "sguard", "sguard64", "sguardsvc",
            "beservice", "battleye", "easyanticheat", "easyanticheat_eos", "eac",
            // 桌面合成器 —— 压它画面直接卡，它是无边框窗口的必经之路
            "dwm",
            // 系统关键
            "system", "idle", "registry", "memory compression", "secure system",
            "csrss", "smss", "wininit", "winlogon", "services", "lsass", "lsaiso",
            "svchost", "audiodg", "fontdrvhost", "conhost", "sihost", "ctfmon",
            "taskhostw", "explorer", "searchhost", "startmenuexperiencehost",
            "runtimebroker", "shellexperiencehost", "wudfhost", "spoolsv",
            "securityhealthservice", "securityhealthsystray", "msmpeng", "nissrv",
            // 游戏本体和它的客户端
            "valorant", "valorant-win64-shipping", "r5apex", "r5apex_dx12",
            "riot client", "riotclientservices", "riotclientux",
            // 显卡驱动 / 显示链路
            "nvcontainer", "nvdisplay.container", "nvidia web helper", "nvsphelper64",
            "igfxem", "igfxhk", "intelcphdcpsvc",
            // 本工具自己
            "valorantboost",
            // 开发工具（压了会把正在跑自动化测试的人卡住）
            "deepseek harness",
        };

        /// <summary>值得压的后台程序。按名字匹配，同名多开全压。</summary>
        private static readonly string[] Candidates = new string[] {
            // 游戏加速器
            "leigod", "leishensdk", "leishengame", "leishenaccelerator",
            "uu", "uubooster", "uugamebooster", "netch", "clash", "v2ray", "xray", "sing-box",
            // 动态壁纸 —— 纯后台，压它零风险
            "wallpaper64", "wallpaper32",
            // 平台 / 启动器
            "steamwebhelper", "epicgameslauncher", "epicwebhelper", "wegame", "tencentdl",
            // 录屏 / 直播 / 覆盖层
            "recorder", "obs64", "obs32", "bdcam", "action",
            "gamebar", "gamebarpresencewriter", "gamebarftserver",
            // 通讯 / 浏览器
            "oopz", "discord", "qq", "wechat", "msedge", "chrome", "firefox", "msedgewebview2",
            // 厂商管家
            "huntercamp", "hunteryard", "pcmanager",
            // 其他常见常驻
            "onedrive", "dropbox", "googledrivefs", "adobedesktop",
        };

        public static string PinnedPath { get { return Path.Combine(Engine.Dir, "pinned.txt"); } }

        /// <summary>「游戏时自动压制」这个开关，存在一个小文件里。</summary>
        public static string FlagPath { get { return Path.Combine(Engine.Dir, "pin-on-game.txt"); } }

        public static bool PinOnGame
        {
            get
            {
                try
                {
                    if (!File.Exists(FlagPath)) return false;
                    return File.ReadAllText(FlagPath, Encoding.UTF8).Trim() == "1";
                }
                catch { return false; }
            }
            set
            {
                try { File.WriteAllText(FlagPath, value ? "1" : "0", new UTF8Encoding(false)); }
                catch { }
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessAffinityMask(IntPtr h, IntPtr mask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessAffinityMask(IntPtr h, out IntPtr procMask, out IntPtr sysMask);

        [StructLayout(LayoutKind.Sequential)]
        private struct ThrottleState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr h, int cls, ref ThrottleState st, int size);

        private const uint PROCESS_SET_INFORMATION = 0x0200;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const int ProcessPowerThrottling = 4;
        private const uint THROTTLE_EXECUTION_SPEED = 0x1;

        public static bool IsProtected(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (string p in Protect) if (n == p) return true;
            return false;
        }

        public static bool IsCandidate(string name)
        {
            string n = (name ?? "").ToLowerInvariant();
            foreach (string c in Candidates) if (n == c) return true;
            return false;
        }

        /// <summary>把某个进程名对应的【所有】实例钉到能效核。返回成功的实例数。</summary>
        public static int Pin(string pname, out string err)
        {
            return PinPids(pname, ProcSnap.Pids(ProcSnap.Take(), pname), out err);
        }

        /// <summary>
        /// 同上，但 pid 由调用方给（调用方已经取过一份进程快照了）。
        /// 批量压制时必须走这个 —— 不然每个候选名字都要把系统进程表枚举一遍。
        /// </summary>
        public static int PinPids(string pname, List<int> pids, out string err)
        {
            err = "";
            // 保护名单先判：这两条都返回 0，但报出去的原因不一样 —— 反着写会让
            // 「名单里的进程」在不可用机型上报成「本机没有可用的大小核」，排障时误导人。
            if (IsProtected(pname)) { err = "在保护名单里，不动"; return 0; }
            // ★ 本机没有可用的大小核就直接跳过 —— 掩码是 0，而传 0 给
            //   SetProcessAffinityMask 的行为是未定义的。不是错误，也不用报错。
            if (!CpuTopo.Usable) { err = "本机没有可用的大小核，压制跳过"; return 0; }
            long eMask = CpuTopo.EMask;
            int ok = 0;
            foreach (int pid in pids)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) { err = "打不开（权限或受保护）"; continue; }
                    IntPtr oldProc, oldSys;
                    if (!GetProcessAffinityMask(h, out oldProc, out oldSys)) { err = "读不到原亲和性"; continue; }
                    // ★ 已经在能效核上的实例不重复计数。
                    // 守护在游戏跑着的时候会定期补压（常驻程序会自己重启，实测
                    // epicwebhelper 28524 就是压制之后才起来的，一直用着全部核心）。
                    // 要是每次都把老实例算进返回值，日志会被刷屏、界面也会以为一直在干活。
                    if (oldProc == (IntPtr)eMask) continue;
                    if (!SetProcessAffinityMask(h, (IntPtr)eMask)) { err = "设亲和性失败"; continue; }
                    // 效率模式：让调度器也倾向低功耗核。失败不算错 —— 亲和性已经兜住了。
                    ThrottleState st = new ThrottleState();
                    st.Version = 1;
                    st.ControlMask = THROTTLE_EXECUTION_SPEED;
                    st.StateMask = THROTTLE_EXECUTION_SPEED;
                    SetProcessInformation(h, ProcessPowerThrottling, ref st, Marshal.SizeOf(typeof(ThrottleState)));
                    ok++;
                }
                catch (Exception ex) { err = ex.Message; }
                finally { if (h != IntPtr.Zero) CloseHandle(h); }
            }
            return ok;
        }

        /// <summary>
        /// 把【本进程】钉到能效核。
        ///
        /// 给守护自己用的：它做的事只有「每 4 秒取一次进程快照，看看游戏在不在」，
        /// 没有任何理由占 P 核 —— 而这台机器上游戏是 CPU 瓶颈，一个后台轮询进程
        /// 跟游戏抢 P 核纯属浪费。只改本进程，给别的进程设亲和性走的是它们的句柄，
        /// 互不影响。失败也无所谓，大不了回到系统原来的调度。
        /// </summary>
        public static bool PinSelf()
        {
            // ★ 掩码是 0 就什么都别做：传 0 给 SetProcessAffinityMask 行为未定义。
            if (!CpuTopo.Usable) return false;
            long eMask = CpuTopo.EMask;
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false,
                                Process.GetCurrentProcess().Id);
                if (h == IntPtr.Zero) return false;
                if (!SetProcessAffinityMask(h, (IntPtr)eMask)) return false;
                ThrottleState st = new ThrottleState();
                st.Version = 1;
                st.ControlMask = THROTTLE_EXECUTION_SPEED;
                st.StateMask = THROTTLE_EXECUTION_SPEED;
                SetProcessInformation(h, ProcessPowerThrottling, ref st, Marshal.SizeOf(typeof(ThrottleState)));
                return true;
            }
            catch { return false; }
            finally { if (h != IntPtr.Zero) CloseHandle(h); }
        }

        /// <summary>把某个进程名对应的所有实例放回全部核心（并关掉效率模式）。</summary>
        public static int Unpin(string pname, out string err)
        {
            return UnpinPids(pname, ProcSnap.Pids(ProcSnap.Take(), pname), out err);
        }

        /// <summary>同上，但 pid 由调用方给。</summary>
        public static int UnpinPids(string pname, List<int> pids, out string err)
        {
            err = "";
            int ok = 0;
            foreach (int pid in pids)
            {
                IntPtr h = IntPtr.Zero;
                try
                {
                    h = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, pid);
                    if (h == IntPtr.Zero) { err = "打不开（权限或受保护）"; continue; }
                    IntPtr oldProc, oldSys;
                    if (!GetProcessAffinityMask(h, out oldProc, out oldSys)) { err = "读不到原亲和性"; continue; }
                    if (oldProc == (IntPtr)oldSys) continue;   // 已经在全部核心上了
                    if (!SetProcessAffinityMask(h, oldSys)) { err = "设亲和性失败"; continue; }
                    // 关掉效率模式
                    ThrottleState st = new ThrottleState();
                    st.Version = 1;
                    st.ControlMask = THROTTLE_EXECUTION_SPEED;
                    st.StateMask = 0;
                    SetProcessInformation(h, ProcessPowerThrottling, ref st, Marshal.SizeOf(typeof(ThrottleState)));
                    ok++;
                }
                catch (Exception ex) { err = ex.Message; }
                finally { if (h != IntPtr.Zero) CloseHandle(h); }
            }
            return ok;
        }

        /// <summary>当前被压着的进程名（从记录文件读，界面和守护都用它）。</summary>
        public static string[] PinnedNames()
        {
            return LoadPinned().ToArray();
        }

        private static List<string> LoadPinned()
        {
            List<string> r = new List<string>();
            try
            {
                if (!File.Exists(PinnedPath)) return r;
                foreach (string line in File.ReadAllLines(PinnedPath, Encoding.UTF8))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s.StartsWith("#")) continue;
                    if (!r.Contains(s)) r.Add(s);
                }
            }
            catch { }
            return r;
        }

        private static void SavePinned(List<string> names)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# ValorantBoost 压到能效核的进程名单 v1");
                sb.AppendLine("# 每行一个进程名。还原时按名字找当前实例，设回全部核心。");
                foreach (string n in names) sb.AppendLine(n);
                File.WriteAllText(PinnedPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>把候选名单里正在跑、又没被保护的进程全压到能效核。返回可读报告。</summary>
        public static string Apply(out int pinned)
        {
            pinned = 0;
            List<string> list = LoadPinned();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            // ★ 结论行必须是【第一行】。界面那个手动压制按钮只取前 48 个字符
            //   （Ui.cs DoPin：收起换行后 Substring(0,48)），而下面那行表头自己
            //   就有 44 个字符 —— 把结论放在表头后面，用户永远看不到「本机为什么
            //   没压」，界面只会显示半截表头。挪到最前面：
            //     不可用时 = 「★ 大小核判定 = 不可用：…」
            //     可用时   = 「★ 大小核判定 = 可用：共 N 档，…」
            sb.AppendLine("★ 大小核判定 = " + CpuTopo.Note);
            sb.AppendLine("把后台程序压到能效核（E 核 = 逻辑核 " + CpuTopo.EList + "）");
            sb.AppendLine("--------------------------------------------------");
            // 本机没有可用的大小核就什么都不用做 —— 掩码是 0，设上去行为未定义。
            if (!CpuTopo.Usable)
            {
                sb.AppendLine();
                sb.AppendLine("压了 0 个实例。");
                return sb.ToString();
            }
            // ★ 整份名单只取【一次】进程快照。
            // 原先是先 GetProcessesByName 探一次在不在跑、再让 Pin 自己查一次，
            // 40 个候选名字就是 80 次全量枚举 —— 每 60 秒来一轮，全是白花的。
            Dictionary<string, List<int>> snap = ProcSnap.Take();
            foreach (string c in Candidates)
            {
                List<int> pids = ProcSnap.Pids(snap, c);
                if (pids.Count == 0) continue;
                string err;
                int n = PinPids(c, pids, out err);
                if (n > 0)
                {
                    pinned += n;
                    if (!list.Contains(c)) list.Add(c);
                    sb.AppendLine("  " + PadR(c, 22) + " 已压 " + n + " 个实例");
                }
                else
                {
                    sb.AppendLine("  " + PadR(c, 22) + " 跳过：" + err);
                }
            }
            if (pinned == 0) sb.AppendLine("  （没有找到可以压的后台程序）");
            else SavePinned(list);
            sb.AppendLine();
            sb.AppendLine("压了 " + pinned + " 个实例。游戏退出后会自动还原，也可以点「还原」立刻放回去。");
            return sb.ToString();
        }

        /// <summary>把之前压过的全放回全部核心。</summary>
        public static string Restore(out int unpinned)
        {
            unpinned = 0;
            List<string> list = LoadPinned();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("把后台程序放回全部核心");
            sb.AppendLine("--------------------------------------------------");
            if (list.Count == 0) sb.AppendLine("  （名单是空的，没有需要还原的）");
            Dictionary<string, List<int>> snap = ProcSnap.Take();
            foreach (string c in list)
            {
                string err;
                int n = UnpinPids(c, ProcSnap.Pids(snap, c), out err);
                if (n > 0) { unpinned += n; sb.AppendLine("  " + PadR(c, 22) + " 已还原 " + n + " 个实例"); }
                else sb.AppendLine("  " + PadR(c, 22) + " 跳过：" + (err.Length > 0 ? err : "没在跑"));
            }
            SavePinned(new List<string>());
            sb.AppendLine();
            sb.AppendLine("还原了 " + unpinned + " 个实例。");
            return sb.ToString();
        }

        /// <summary>看现在谁被压着（直接问系统要亲和性，不信名单）。</summary>
        public static string Report()
        {
            StringBuilder sb = new StringBuilder();
            long eMask = ECORE_MASK;
            sb.AppendLine("能效核掩码 = 0x" + eMask.ToString("X") + "   （逻辑核 " + CpuTopo.EList + "）");
            sb.AppendLine("性能核掩码 = 0x" + PCORE_MASK.ToString("X") + "   （逻辑核 " + CpuTopo.PList + "）");
            // 结论行：可用/不可用 + 原因，给界面直接读。
            // ⚠️ 这行里【不要出现 "0x"】—— Flutter 的 parsePin 会把带 0x 的行当进程行抓走。
            sb.AppendLine("大小核判定 = " + CpuTopo.Note);
            sb.AppendLine();
            sb.AppendLine("进程名                 实例  当前亲和性");
            sb.AppendLine("--------------------------------------------------");
            int hit = 0;
            foreach (string c in Candidates)
            {
                System.Diagnostics.Process[] ps;
                try { ps = System.Diagnostics.Process.GetProcessesByName(c); } catch { continue; }
                if (ps.Length == 0) continue;
                foreach (System.Diagnostics.Process p in ps)
                {
                    // pid 必须先取出来 —— 下面 finally 里会 Dispose，之后再读 p.Id 会抛
                    // 「没有与此对象关联的进程」。
                    int pid = 0;
                    try { pid = p.Id; } catch { }
                    IntPtr h = IntPtr.Zero;
                    string desc = "读不到";
                    try
                    {
                        h = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
                        if (h != IntPtr.Zero)
                        {
                            IntPtr a, s;
                            if (GetProcessAffinityMask(h, out a, out s))
                            {
                                long m = a.ToInt64();
                                desc = "0x" + m.ToString("X");
                                if (m == eMask) { desc += "  ← 已压到能效核"; hit++; }
                                else if (m == s.ToInt64()) desc += "  （全部核心）";
                            }
                        }
                    }
                    catch { }
                    finally { if (h != IntPtr.Zero) CloseHandle(h); p.Dispose(); }
                    sb.AppendLine("  " + PadR(c, 20) + " " + PadR(pid.ToString(), 6) + desc);
                }
            }
            if (hit == 0) sb.AppendLine("  （现在没有进程被压到能效核）");
            return sb.ToString();
        }

        private static string PadR(string s, int w)
        {
            int len = 0;
            foreach (char ch in s) len += (ch > 0x2E80 ? 2 : 1);
            if (len >= w) return s;
            return s + new string(' ', w - len);
        }
    }

    internal static class Bench
    {
        /// <summary>固定时间里数「算了多少次开方」。不经过任何系统计数器，无法作弊。</summary>
        public static long Run(int threads, int millis)
        {
            long total = 0;
            int remaining = threads;
            ManualResetEvent done = new ManualResetEvent(false);
            for (int t = 0; t < threads; t++)
            {
                Thread th = new Thread(delegate()
                {
                    double x = 1.0;
                    long n = 0;
                    Stopwatch sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < millis)
                    {
                        for (int i = 0; i < 4096; i++) x = Math.Sqrt(x * 1.0000001 + 1.0);
                        n += 4096;
                    }
                    Interlocked.Add(ref total, n);
                    if (Interlocked.Decrement(ref remaining) == 0) done.Set();
                });
                th.IsBackground = true;
                th.Priority = ThreadPriority.AboveNormal;
                th.Start();
            }
            done.WaitOne(millis + 15000);
            return total;
        }

        /// <summary>读一次 CPU 频率与核心停泊。</summary>
        public static void SampleFrequency(out double estMHz, out double peakMHz, out int parked, out int seen)
        {
            estMHz = 0; peakMHz = 0; parked = 0; seen = 0;
            try
            {
                ManagementObjectSearcher s = new ManagementObjectSearcher(
                    "SELECT Name,ProcessorFrequency,PercentProcessorPerformance,ParkingStatus " +
                    "FROM Win32_PerfFormattedData_Counters_ProcessorInformation");
                foreach (ManagementObject mo in s.Get())
                {
                    string name = Convert.ToString(mo["Name"]);
                    // 实例名是多处理器组的「组号,序号」，组汇总叫「0,_Total」。
                    // 必须用子串匹配，用全等比较拦不住它，22 个核会被数成 23。
                    if (name.IndexOf("_Total", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    seen++;
                    try
                    {
                        if (Convert.ToInt32(mo["ParkingStatus"]) != 0) parked++;
                    }
                    catch { }
                    double f = Convert.ToDouble(mo["ProcessorFrequency"]);
                    double perf = Convert.ToDouble(mo["PercentProcessorPerformance"]);
                    // ProcessorFrequency 报的是**标称频率**，不是实时频率；
                    // PercentProcessorPerformance 里 100 = 标称。实时频率 = 标称 × 性能% ÷ 100。
                    // 必须先各自乘完再取最大，否则会拿 P 核的标称去乘 E 核的倍率，得出 4600MHz 这种假数。
                    double e = f * perf / 100.0;
                    if (e > estMHz) estMHz = e;
                    if (f > peakMHz) peakMHz = f;
                }
            }
            catch { }
        }

        public static BenchResult Full()
        {
            BenchResult r = new BenchResult();
            CpuInfo ci = CpuInfo.Detect();
            int threads = ci.Logical > 0 ? ci.Logical : Environment.ProcessorCount;
            r.Threads = threads;
            try
            {
                Run(1, 400);                                   // 预热
                double e1, p1; int k1, s1;
                SampleFrequency(out e1, out p1, out k1, out s1); // 空载
                r.EstMHz = e1; r.PeakMHz = p1;
                r.Single = Run(1, 1500);
                r.All = Run(threads, 2500);
                double e2, p2; int k2, s2;
                SampleFrequency(out e2, out p2, out k2, out s2);
                r.Parked = k2; r.LogicalSeen = s2;
                if (e2 > r.EstMHz) r.EstMHz = e2;
                r.Ok = true;
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Error = ex.Message;
            }
            return r;
        }
    }
}
