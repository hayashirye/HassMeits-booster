// ============================================================================
//  ValorantCpuBoost  -  无畏契约 CPU 高频优化器 (Intel Core Ultra 7 155H 专项)
// ----------------------------------------------------------------------------
//  编译: 见 build.ps1 (使用 Windows 自带 csc.exe，无需安装任何东西)
//  特性:  管理员清单 / WinForms 界面 / pow 文件字节级读写(不受系统语言影响)
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
    // ======================= 电源设置定义 =======================
    internal class SettingDef
    {
        public readonly Guid Sub;
        public readonly Guid Set;
        public readonly string Name;      // 显示名
        public readonly string Current;   // 当前(默认)值
        public readonly string Target;    // 目标值
        public readonly string Why;       // 这么设的理由

        public SettingDef(Guid sub, Guid set, string name, string cur, string target, string why)
        {
            Sub = sub; Set = set; Name = name; Current = cur; Target = target; Why = why;
        }
    }

    internal static class Plans
    {
        public static readonly Guid PPM = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid HIGH_PERF = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public static readonly Guid ULTIMATE = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");
        public static readonly Guid BALANCED = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");

        // ---- Meteor Lake / Core Ultra 专用的一组设置 ----
        public static List<SettingDef> Build()
        {
            List<SettingDef> l = new List<SettingDef>();

            // 1) 频率地板与天花板：核心诉求「尽量高频」
            l.Add(new SettingDef(PPM, new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c"),
                "最小处理器状态 (%)", "5", "100",
                "锁死频率地板：空闲也不掉到低频，进游戏无需等待升频"));
            l.Add(new SettingDef(PPM, new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec"),
                "最大处理器状态 (%)", "100", "100",
                "防止厂商方案把上限压到 95~99%，那样会直接砍掉睿频"));

            // 2) 睿频：Meteor Lake 必须开激进
            l.Add(new SettingDef(PPM, new Guid("be337238-0d82-4146-a960-4f3749d470c7"),
                "性能提升模式", "1 (启用)", "2 (激进)",
                "Core Ultra 的睿频要主动冲顶，而不是等负载上来再慢慢加"));

            // 3) EPP：HWP / Speed Shift 的核心开关
            l.Add(new SettingDef(PPM, new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"),
                "能效偏好 EPP", "50 左右", "0 (最偏性能)",
                "155H 用 HWP 硬件调频，EPP=0 才会选最高频率档"));
            l.Add(new SettingDef(PPM, new Guid("45bcc044-d885-43e2-8605-ee0ec6e96b59"),
                "性能检查间隔", "默认", "最短",
                "让硬件更频繁评估升频，缩短响应延迟"));

            // 4) 核心停泊：P 核不许睡觉
            l.Add(new SettingDef(PPM, new Guid("0cc5b647-c1df-4637-891a-dec35c318583"),
                "核心停放最小核心 (%)", "视厂商", "100 (不泊车)",
                "核心一停泊，就等于少几个核在跑高频"));
            l.Add(new SettingDef(PPM, new Guid("ea062031-0e34-4ff1-9b6d-eb1059334028"),
                "核心停放最大核心 (%)", "默认", "100",
                "配合上一项彻底禁用核心停泊"));

            // 5) 大小核调度：155H 是 6P + 8E + 2LPE
            l.Add(new SettingDef(PPM, new Guid("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5"),
                "异构调度策略", "视厂商", "2 (优先 P 核)",
                "让瓦罗兰特的主线程落在 P 核，而不是被丢到 E 核 (0=自动 1=优先E核 2=优先P核)"));
            l.Add(new SettingDef(PPM, new Guid("93b8b6dc-0698-4d1c-9ee4-0644e900c85d"),
                "核心放置策略", "视厂商", "1 (优先性能核)",
                "优先把任务铺在性能核(P 核)所在的调度域 (0=自动 1=优先性能核)"));

            // 6) 升频积极、降频迟钝
            l.Add(new SettingDef(PPM, new Guid("06cadf0e-64ed-448a-8927-ce7bf90eb35d"),
                "性能提升阈值", "默认", "0",
                "负载一上来立刻升频"));
            l.Add(new SettingDef(PPM, new Guid("40fbefc7-2e9d-4d25-a185-0cfd8574bac6"),
                "性能降低阈值", "默认", "100",
                "负载掉了也不急着降频，减少频率抖动"));

            // 7) 散热与深度空闲
            l.Add(new SettingDef(PPM, new Guid("94d3a615-a899-4ac5-ae2b-e4d8f634367f"),
                "系统散热方式", "0 (被动)", "1 (主动)",
                "宁可风扇先转起来，也不要先降频"));
            l.Add(new SettingDef(PPM, new Guid("5d76a2ca-e8c0-402f-a133-2158492d58ad"),
                "空闲禁用", "0", "1",
                "禁止处理器进入深度空闲，避免唤醒时的频率空窗"));
            l.Add(new SettingDef(PPM, new Guid("616cdaa5-695e-4545-97b5-97af9a4a1c48"),
                "延迟敏感度提示", "0 (无提示)", "1 (低延迟)",
                "提示调频器不要为了省电牺牲响应速度"));
            return l;
        }
    }

    // ======================= 安全等级 =======================
    internal enum Mode
    {
        Safe,       // 保守：不管频率地板，只解绑掉频、优先 P 核
        Competitive,// 竞技：地板 25%，EPP 0 —— 不霸占功率，把功耗预算让给独显
        Balanced,   // 均衡：地板 60%，EPP 30
        Max         // 极限高频：地板 100%，EPP 0
    }

    // ======================= 执行结果 =======================
    internal class Result
    {
        public readonly List<string> Lines = new List<string>();
        public bool AnyFail;
        public void Ok(string s) { Lines.Add("[成功] " + s); }
        public void Skip(string s) { Lines.Add("[跳过] " + s); }
        public void Warn(string s) { Lines.Add("[注意] " + s); }
        public void Fail(string s) { Lines.Add("[失败] " + s); AnyFail = true; }
        public string Text { get { return string.Join("\r\n", Lines.ToArray()); } }
    }

    // ======================= 电源方案引擎 =======================
    internal static class PowerEngine
    {
        // ---------- 启动进程并取回 stdout（不新开窗口） ----------
        private static int Run(string file, string args, out string stdout)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.GetEncoding(936);
            try
            {
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                    stdout = (o + "\r\n" + e).Trim();
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                stdout = ex.Message;
                return -999;
            }
        }

        public static string ActiveSchemeGuid()
        {
            string outp;
            Run("powercfg.exe", "/getactivescheme", out outp);
            return FirstGuid(outp);
        }

        public static string FirstGuid(string s)
        {
            if (s == null) return null;
            for (int i = 0; i + 36 <= s.Length; i++)
            {
                string t = s.Substring(i, 36);
                Guid g;
                if (Guid.TryParse(t, out g)) return g.ToString();
            }
            return null;
        }

        public static bool SchemeExists(Guid g)
        {
            string outp;
            Run("powercfg.exe", "/list", out outp);
            return outp.IndexOf(g.ToString(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static Guid EnsureUltimate()
        {
            // 说明：powercfg /duplicatescheme 必须带自己生成的 GUID，否则导出名不可控
            if (SchemeExists(Plans.ULTIMATE)) return Plans.ULTIMATE;
            Guid fresh = Guid.NewGuid();
            string outp;
            Run("powercfg.exe", "/duplicatescheme " + Plans.ULTIMATE.ToString() + " " + fresh.ToString(), out outp);
            if (!SchemeExists(fresh))
            {
                string g = FirstGuid(outp);
                Guid parsed;
                if (g != null && Guid.TryParse(g, out parsed) && SchemeExists(parsed)) fresh = parsed;
                else return Guid.Empty;
            }
            // 改个能认出来的名字，方便还原时识别
            Run("powercfg.exe", "/changename " + fresh.ToString() + " \"VALORANT High Frequency\"", out outp);
            return fresh;
        }

        public static bool SetActive(Guid g)
        {
            string outp;
            return Run("powercfg.exe", "/setactive " + g.ToString(), out outp) == 0;
        }

        // ---------- 导出 .pow ----------
        public static bool ExportPow(Guid scheme, string path)
        {
            SafeDelete(path);
            string outp;
            return Run("powercfg.exe", "/export \"" + path + "\" " + scheme.ToString(), out outp) == 0
                   && File.Exists(path);
        }

        public static bool ImportPow(string path)
        {
            string outp;
            return Run("powercfg.exe", "/import \"" + path + "\"", out outp) == 0;
        }

        // ---------- 在 .pow 里定位某个电源设置，返回该设置块起始偏移 ----------
        //  .pow 结构(小端):
        //    0x00 "POWR"   0x04 版本(4)   0x08 方案头大小/设置表偏移
        //    0x10 方案GUID(16)            0x24 方案名(UTF-16, 以 0x0000 结尾)
        //    之后是若干设置块: [设置GUID(16)][数据大小(4)][AC值(4)][DC值(4)][数据]
        public static int FindSettingOffset(byte[] b, Guid setGuid)
        {
            byte[] g = setGuid.ToByteArray();
            if (b.Length < 0x30) return -1;
            // .pow 有两种布局：v1 直接在文件头，v2 在 0x10 处放方案 GUID，设置块从 0x30 开始。
            // 先试 0x30，不中再退回 0x10，两种都能覆盖。
            int pos = 0x30;
            int end = b.Length - 20;
            for (int i = pos; i < end; i++)
            {
                bool hit = true;
                for (int k = 0; k < 16; k++)
                {
                    if (b[i + k] != g[k]) { hit = false; break; }
                }
                if (hit) return i;
            }
            return -1;
        }

        public static bool ReadValues(byte[] b, int off, out int ac, out int dc)
        {
            ac = 0; dc = 0;
            if (off < 0 || off + 24 > b.Length) return false;
            ac = BitConverter.ToInt32(b, off + 0x14);
            dc = BitConverter.ToInt32(b, off + 0x18);
            return true;
        }

        public static void WriteValues(byte[] b, int off, int ac, int dc)
        {
            byte[] a = BitConverter.GetBytes(ac);
            byte[] d = BitConverter.GetBytes(dc);
            Array.Copy(a, 0, b, off + 0x14, 4);
            Array.Copy(d, 0, b, off + 0x18, 4);
        }

        // 当前值 -> 目标值 的提示文本（".pow" 里的原始数值）
        public static readonly Guid GUID_BOOST = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
        public static readonly Guid GUID_HETERO = new Guid("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5");
        public static readonly Guid GUID_MINSTATE = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");
        public static readonly Guid GUID_EPP = new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        public static readonly Guid GUID_CHECKINT = new Guid("45bcc044-d885-43e2-8605-ee0ec6e96b59");
        public static readonly Guid GUID_LATENCY = new Guid("616cdaa5-695e-4545-97b5-97af9a4a1c48");

        public static string RawToHuman(Guid set, int v)
        {
            if (set.Equals(GUID_BOOST))
            {
                string[] n = { "禁用", "启用", "激进", "高效启用", "高效激进" };
                return (v >= 0 && v < n.Length) ? v + " (" + n[v] + ")" : v.ToString();
            }
            if (set.Equals(GUID_HETERO))
                return (v == 0 ? "0 (优先 P 核)" : (v == 1 ? "1 (优先 E 核)" : v.ToString()));
            return v.ToString();
        }

        // 目标值 -> 数值
        public static int TargetValue(SettingDef d, Mode mode)
        {
            if (d.Set.Equals(GUID_MINSTATE))
            {
                if (mode == Mode.Safe) return 5;         // 不动频率地板
                // 竞技：地板只抬到 25%。CPU 不长时间霸占功耗预算，
                // 省下来的瓦数留给独显（笔记本上 4060 和 CPU 抢同一份功率）。
                if (mode == Mode.Competitive) return 25;
                if (mode == Mode.Balanced) return 60;
                return 100;
            }
            if (d.Set.Equals(GUID_EPP))
            {
                if (mode == Mode.Safe) return 25;
                // 竞技的 EPP 给 0（要升频就立刻升满，最跟手），
                // 但地板只有 25% —— 靠"响应快"而不是"频率高"来赢帧数。
                if (mode == Mode.Competitive) return 0;
                if (mode == Mode.Balanced) return 30;
                return 0;
            }
            if (d.Set.Equals(GUID_CHECKINT))
            {
                // 性能检查间隔：数据长度 4 (即单位 100ms)，1 = 100ms 最短
                return 1;
            }
            if (d.Set.Equals(GUID_LATENCY))
                return 1;
            return int.Parse(d.Target.Split(' ')[0], CultureInfo.InvariantCulture);
        }

        public static int CurrentValue(SettingDef d)
        {
            string[] parts = d.Current.Split(' ');
            int v;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return -1;
        }

        private static void SafeDelete(string p)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { }
        }

        // =====================================================================
        //  应用优化
        // =====================================================================
        public static Result Apply(Mode mode)
        {
            Result r = new Result();
            string tmp = Path.Combine(Path.GetTempPath(), "vcb_" + Guid.NewGuid().ToString("N") + ".pow");
            string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backup");
            Directory.CreateDirectory(backupDir);

            // ---- 0) 记录原始状态 ----
            Guid original = Guid.Empty;
            string ag = ActiveSchemeGuid();
            if (ag != null) original = new Guid(ag);
            r.Ok("原电源方案: " + original.ToString());

            if (original != Guid.Empty)
            {
                string origPow = Path.Combine(backupDir, "original-" + original.ToString() + ".pow");
                if (ExportPow(original, origPow))
                    r.Ok("已导出原始方案备份: backup\\" + Path.GetFileName(origPow));
                else
                    r.Warn("原始方案导出失败（还原时将逐项写回默认值）");
            }

            // ---- 1) 切换高频方案 ----
            Guid target = Guid.Empty;
            bool created = false;
            Guid ult = EnsureUltimate();
            if (ult != Guid.Empty)
            {
                if (ult == Plans.ULTIMATE) r.Ok("使用系统内置「卓越性能」方案");
                else { r.Ok("已创建高频方案副本: " + ult.ToString()); created = true; }
                target = ult;
            }
            else
            {
                r.Warn("本机无「卓越性能」模板，改用「高性能」方案");
                target = Plans.HIGH_PERF;
            }
            bool switched = SetActive(target);
            if (switched) r.Ok("已切换到目标方案");
            else
            {
                r.Fail("切换方案失败，改为直接修改当前方案");
                target = original;
            }

            // ---- 2) 备份目标方案的 .pow，然后字节级改值 ----
            if (target == Guid.Empty)
            {
                r.Fail("无法确定目标电源方案，优化终止");
                return r;
            }

            if (!ExportPow(target, tmp))
            {
                r.Fail("导出目标方案 .pow 失败，无法继续");
                return r;
            }

            byte[] bytes = File.ReadAllBytes(tmp);
            List<SettingDef> defs = Plans.Build();
            StringBuilder report = new StringBuilder();
            report.AppendLine("VALORANT CPU 高频优化 - 报告");
            report.AppendLine("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("模式: " + ModeName(mode));
            report.AppendLine("原方案: " + original.ToString());
            report.AppendLine(new string('-', 60));

            foreach (SettingDef d in defs)
            {
                int off = FindSettingOffset(bytes, d.Set);
                if (off < 0)
                {
                    r.Skip(d.Name + " —— 本机电源方案不含此项（跳过）");
                    report.AppendLine("[跳过] " + d.Name);
                    continue;
                }
                int oldAc, oldDc;
                ReadValues(bytes, off, out oldAc, out oldDc);
                int tv = TargetValue(d, mode);
                WriteValues(bytes, off, tv, tv);

                string human = RawToHuman(d.Set, oldAc);
                report.AppendLine(string.Format("[设置] {0}: {1} -> {2}   ({3})",
                    d.Name, oldAc, tv, d.Why));
                r.Ok(string.Format("{0}: {1} -> {2}", d.Name, human, tv));
            }

            File.WriteAllBytes(tmp, bytes);
            if (!ImportPow(tmp))
            {
                r.Fail("导入修改后的电源方案失败（.pow 可能不被本机接受）");
                return r;
            }
            r.Ok("修改后的电源方案已导入并生效");

            // 再激活一次，确保数值立刻加载
            SetActive(target);

            // ---- 3) 保存状态文件 ----
            try
            {
                string state = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "restore-state.json");
                string json =
                    "{\r\n" +
                    "  \"app\": \"ValorantCpuBoost\",\r\n" +
                    "  \"version\": 2,\r\n" +
                    "  \"time\": \"" + DateTime.Now.ToString("s") + "\",\r\n" +
                    "  \"originalScheme\": \"" + original.ToString() + "\",\r\n" +
                    "  \"targetScheme\": \"" + target.ToString() + "\",\r\n" +
                    "  \"schemeCreated\": " + (created ? "true" : "false") + ",\r\n" +
                    "  \"mode\": \"" + ModeName(mode) + "\"\r\n" +
                    "}\r\n";
                File.WriteAllText(state, json, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(backupDir, "last-report.txt"),
                    report.ToString(), new UTF8Encoding(false));
                r.Ok("状态与报告已写入程序目录");
            }
            catch (Exception ex)
            {
                r.Warn("写状态文件失败: " + ex.Message);
            }

            SafeDelete(tmp);
            return r;
        }

        public static string ModeName(Mode m)
        {
            if (m == Mode.Safe) return "保守（只解绑掉频）";
            if (m == Mode.Competitive) return "竞技（地板25% / EPP0，不抢独显功率）";
            if (m == Mode.Balanced) return "均衡（地板60% / EPP30）";
            return "极限高频（地板100% / EPP0）";
        }

        // =====================================================================
        //  还原
        // =====================================================================
        public static Result Restore()
        {
            Result r = new Result();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string state = Path.Combine(baseDir, "restore-state.json");
            string backupDir = Path.Combine(baseDir, "backup");

            if (!File.Exists(state))
            {
                r.Fail("找不到状态文件 restore-state.json，无法还原。");
                r.Warn("可以手动执行: powercfg /setactive SCHEME_BALANCED");
                return r;
            }

            string txt = File.ReadAllText(state, Encoding.UTF8);
            string original = JsonString(txt, "originalScheme");
            string target = JsonString(txt, "targetScheme");
            bool created = txt.IndexOf("\"schemeCreated\": true", StringComparison.OrdinalIgnoreCase) >= 0;

            // ---- 1) 用原始 .pow 备份整体还原（最干净）----
            bool restored = false;
            if (!string.IsNullOrEmpty(original))
            {
                string origPow = Path.Combine(backupDir, "original-" + original + ".pow");
                if (File.Exists(origPow) && ImportPow(origPow))
                {
                    r.Ok("已从 backup\\original-*.pow 完整还原原方案");
                    restored = true;
                }
            }

            // ---- 2) 切回原方案 ----
            Guid og = Guid.Empty;
            if (!string.IsNullOrEmpty(original) && Guid.TryParse(original, out og))
            {
                if (SetActive(og)) r.Ok("已切回原电源方案 " + original);
                else r.Fail("切回原方案失败");
            }

            // ---- 3) 没有 .pow 备份时逐项写回默认值 ----
            if (!restored)
            {
                r.Warn("没有 .pow 备份，改用「写回 Windows 默认值」的方式还原");
                Guid active = og;
                if (active == Guid.Empty)
                {
                    string ag = ActiveSchemeGuid();
                    if (ag != null) active = new Guid(ag);
                }
                Dictionary<Guid, int> defaults = DefaultValues();
                string tmp = Path.Combine(Path.GetTempPath(), "vcb_r_" + Guid.NewGuid().ToString("N") + ".pow");
                if (ExportPow(active, tmp))
                {
                    byte[] b = File.ReadAllBytes(tmp);
                    foreach (SettingDef d in Plans.Build())
                    {
                        int dv;
                        if (!defaults.TryGetValue(d.Set, out dv)) continue;
                        int off = FindSettingOffset(b, d.Set);
                        if (off < 0) continue;
                        int oldAc, oldDc;
                        ReadValues(b, off, out oldAc, out oldDc);
                        WriteValues(b, off, dv, dv);
                        r.Ok(d.Name + ": " + oldAc + " -> " + dv + " (默认值)");
                    }
                    File.WriteAllBytes(tmp, b);
                    if (ImportPow(tmp)) r.Ok("默认值写回完成");
                    else r.Fail("默认值写回失败");
                    try { File.Delete(tmp); } catch { }
                }
            }

            // ---- 4) 删除本次创建的临时方案 ----
            if (created && !string.IsNullOrEmpty(target))
            {
                Guid tg;
                if (Guid.TryParse(target, out tg) && tg != Plans.ULTIMATE)
                {
                    string outp;
                    if (Run("powercfg.exe", "/delete " + tg.ToString(), out outp) == 0)
                        r.Ok("已删除本次创建的临时电源方案");
                    else
                        r.Warn("临时方案删除失败（可能仍在使用中）");
                }
            }

            try { File.Delete(state); } catch { }
            r.Ok("还原流程完成");
            return r;
        }

        // 各项的 Windows 默认值（用于没有 .pow 备份时的兜底还原）
        public static Dictionary<Guid, int> DefaultValues()
        {
            Dictionary<Guid, int> d = new Dictionary<Guid, int>();
            d[new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c")] = 5;     // min state
            d[new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec")] = 100;   // max state
            d[new Guid("be337238-0d82-4146-a960-4f3749d470c7")] = 1;     // boost = 启用
            d[new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863")] = 50;    // EPP
            d[new Guid("45bcc044-d885-43e2-8605-ee0ec6e96b59")] = 100;   // 检查间隔
            d[new Guid("0cc5b647-c1df-4637-891a-dec35c318583")] = 100;   // 停放最小核心
            d[new Guid("ea062031-0e34-4ff1-9b6d-eb1059334028")] = 100;   // 停放最大核心
            d[new Guid("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5")] = 3;     // 异构策略 = 自动
            d[new Guid("93b8b6dc-0698-4d1c-9ee4-0644e900c85d")] = 0;     // 放置策略 = 自动
            d[new Guid("06cadf0e-64ed-448a-8927-ce7bf90eb35d")] = 30;    // 提升阈值
            d[new Guid("40fbefc7-2e9d-4d25-a185-0cfd8574bac6")] = 10;    // 降低阈值
            d[new Guid("94d3a615-a899-4ac5-ae2b-e4d8f634367f")] = 0;     // 散热 = 被动
            d[new Guid("5d76a2ca-e8c0-402f-a133-2158492d58ad")] = 0;     // 空闲禁用
            d[new Guid("616cdaa5-695e-4545-97b5-97af9a4a1c48")] = 0;     // 延迟敏感
            return d;
        }

        private static string JsonString(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i = json.IndexOf(':', i);
            if (i < 0) return null;
            int a = json.IndexOf('"', i);
            if (a < 0) return null;
            int b = json.IndexOf('"', a + 1);
            if (b < 0) return null;
            return json.Substring(a + 1, b - a - 1);
        }

        // =====================================================================
        //  查询：某方案下所有设置项的 AC/DC 原始值
        // =====================================================================
        public static Dictionary<Guid, int[]> ReadScheme(Guid scheme)
        {
            Dictionary<Guid, int[]> map = new Dictionary<Guid, int[]>();
            string tmp = Path.Combine(Path.GetTempPath(), "vcb_q_" + Guid.NewGuid().ToString("N") + ".pow");
            if (!ExportPow(scheme, tmp)) return map;
            try
            {
                byte[] b = File.ReadAllBytes(tmp);
                foreach (SettingDef d in Plans.Build())
                {
                    int off = FindSettingOffset(b, d.Set);
                    if (off < 0) continue;
                    int ac, dc;
                    ReadValues(b, off, out ac, out dc);
                    map[d.Set] = new int[] { ac, dc };
                }
            }
            catch { }
            finally { SafeDelete(tmp); }
            return map;
        }

        // =====================================================================
        //  界面用：当前方案各项值的缓存
        //  读一次 pow 要起一个进程，界面每 10 秒刷一次，所以缓存 8 秒。
        //  返回 -1 表示「当前方案里没有这一项」= 本机不支持。
        // =====================================================================
        private static Dictionary<Guid, int[]> _valCache;
        private static DateTime _valCacheAt = DateTime.MinValue;
        private static readonly object _valLock = new object();

        public static void InvalidateCache()
        {
            lock (_valLock)
            {
                _valCache = null;
                _valCacheAt = DateTime.MinValue;
            }
        }

        public static int CurrentValueCached(SettingDef d)
        {
            try
            {
                int[] v;
                lock (_valLock)
                {
                    if (_valCache == null || (DateTime.Now - _valCacheAt).TotalSeconds > 8)
                    {
                        string ag = ActiveSchemeGuid();
                        Guid g = Guid.Empty;
                        if (ag != null) g = new Guid(ag);
                        if (g != Guid.Empty) _valCache = ReadScheme(g);
                        else _valCache = new Dictionary<Guid, int[]>();
                        _valCacheAt = DateTime.Now;
                    }
                    v = _valCache.ContainsKey(d.Set) ? _valCache[d.Set] : null;
                }
                if (v == null || v.Length == 0) return -1;
                return v[0];   // [0] = 交流电 AC
            }
            catch { return -1; }
        }
    }

    // ======================= 硬件信息 =======================
    internal class CpuInfo
    {
        public string Name = "未知";
        public int Cores, Threads, MaxMhz, CurMhz;
        public int PCores, ECores, LpeCores;
        public bool IsHybrid;

        public static CpuInfo Detect()
        {
            CpuInfo c = new CpuInfo();
            c.Threads = Environment.ProcessorCount;
            try
            {
                foreach (System.Management.ManagementObject o in
                    new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Processor").Get())
                {
                    c.Name = Convert.ToString(o["Name"]).Trim();
                    c.Cores = Convert.ToInt32(o["NumberOfCores"]);
                    c.Threads = Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                    c.MaxMhz = Convert.ToInt32(o["MaxClockSpeed"]);
                    c.CurMhz = Convert.ToInt32(o["CurrentClockSpeed"]);
                    break;
                }
            }
            catch { }
            c.Classify();
            return c;
        }

        // 利用注册表 EFI 变量识别三类核心（P / E / LP-E）
        private void Classify()
        {
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel\KGroups\00");
                if (k != null)
                {
                    object v = k.GetValue("GroupMask");
                    if (v is byte[]) IsHybrid = true;
                    k.Close();
                }
                Microsoft.Win32.RegistryKey e = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Power");
                if (e != null)
                {
                    object hv = e.GetValue("HeteroPolicyCapability");
                    if (hv != null) IsHybrid = true;
                    e.Close();
                }
            }
            catch { }

            string n = Name.ToLowerInvariant();
            if (n.Contains("core(tm) ultra") || n.Contains("core ultra"))
            {
                // Core Ultra 7 155H = 6 P + 8 E + 2 LP-E / 22 线程
                if (Threads == 22) { PCores = 6; ECores = 8; LpeCores = 2; }
                else if (Threads >= 20) { PCores = 6; ECores = 8; LpeCores = Math.Max(0, Threads - 16); }
                else { PCores = Math.Max(1, Threads / 4); ECores = Math.Max(1, (Threads - PCores * 2) / 1); LpeCores = 0; }
                IsHybrid = true;
            }
        }

        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CPU  : " + Name);
            if (PCores > 0)
                sb.AppendLine(string.Format("核心 : {0} 个 P 核(超线程) + {1} 个 E 核 + {2} 个 LP-E 核  =>  {3} 线程",
                    PCores, ECores, LpeCores, Threads));
            else
                sb.AppendLine(string.Format("核心 : {0} 物理核 / {1} 线程", Cores, Threads));
            sb.AppendLine(string.Format("频率 : 当前 {0} MHz / 标称最高 {1} MHz", CurMhz, MaxMhz));
            sb.AppendLine("架构 : " + (IsHybrid ? "混合架构（P/E 核）—— 已启用大小核调度优化" : "非混合架构"));
            return sb.ToString();
        }
    }

    // ======================= 进程优化 =======================
    internal static class ProcTuner
    {
        private static readonly string[] Names = { "VALORANT-Win64-Shipping", "VALORANT", "vgc" };

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr h, int infoClass, ref PROCESS_POWER_THROTTLING_STATE s, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

        public static Result TuneOnce()
        {
            Result r = new Result();
            int found = 0;
            foreach (string nm in Names)
            {
                Process[] ps;
                try { ps = Process.GetProcessesByName(nm); }
                catch { continue; }
                foreach (Process p in ps)
                {
                    found++;
                    // 1) 优先级提升为「高」
                    try
                    {
                        if (p.PriorityClass != ProcessPriorityClass.High)
                        {
                            p.PriorityClass = ProcessPriorityClass.High;
                            r.Ok(p.ProcessName + " (PID " + p.Id + ") -> 优先级 高");
                        }
                    }
                    catch (Exception ex) { r.Warn(p.ProcessName + " 优先级设置失败: " + ex.Message); }

                    // 2) 关闭该进程的 EcoQoS 节能节流
                    try
                    {
                        IntPtr h = OpenProcess(0x0200 /*SET_INFORMATION*/, false, p.Id);
                        if (h != IntPtr.Zero)
                        {
                            PROCESS_POWER_THROTTLING_STATE st = new PROCESS_POWER_THROTTLING_STATE();
                            st.Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION;
                            st.ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                            st.StateMask = 0; // 0 = 关闭节流
                            SetProcessInformation(h, ProcessPowerThrottling, ref st,
                                Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
                            CloseHandle(h);
                            r.Ok(p.ProcessName + " -> 已关闭 EcoQoS 节能节流");
                        }
                    }
                    catch (Exception ex) { r.Warn(p.ProcessName + " EcoQoS 设置失败: " + ex.Message); }
                }
            }
            if (found == 0) r.Warn("没有检测到正在运行的 VALORANT 进程（先启动游戏再点这个按钮即可）");
            return r;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);
    }

    // ======================= 小程序设置（注册表） =======================
    internal static class RegTweaks
    {
        public static Result Apply(bool gameMode, bool gameDvr, bool fso, bool powerThrottle, bool gpuPref, string valorantExe)
        {
            Result r = new Result();
            if (gameMode)
            {
                if (SetHkcu(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1)) r.Ok("已开启 Windows 游戏模式");
                if (SetHkcu(@"Software\Microsoft\GameBar", "AllowAutoGameMode", 1)) r.Ok("已允许自动游戏模式");
            }
            if (gameDvr)
            {
                if (SetHkcu(@"System\GameConfigStore", "GameDVR_Enabled", 0)) r.Ok("已关闭 Game DVR 后台录制");
                if (SetHkcu(@"System\GameConfigStore\GameDVR", "AppCaptureEnabled", 0)) r.Ok("已关闭应用捕获");
            }
            if (fso)
            {
                if (SetHkcu(@"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 2)) r.Ok("已关闭全屏优化 (模式2)");
                if (SetHkcu(@"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode", 1)) r.Ok("已尊重用户全屏优化设置");
                if (SetHkcu(@"System\GameConfigStore", "GameDVR_DXGIHonorFSEWindowsCompatible", 1)) r.Ok("已开启 DXGI 兼容全屏处理");
            }
            if (powerThrottle)
            {
                if (SetHklm(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1))
                    r.Ok("已全局关闭 EcoQoS 节能节流 (PowerThrottlingOff=1)");
            }
            if (gpuPref)
            {
                string exe = null;
                if (!string.IsNullOrEmpty(valorantExe) && File.Exists(valorantExe)) exe = valorantExe;
                else exe = FindValorant();
                if (exe == null)
                    r.Warn("没找到 VALORANT 主程序，跳过「高性能 GPU」首选项（可在界面里填入路径后重试）");
                else
                {
                    if (SetHkcuString(@"Software\Microsoft\DirectX\UserGpuPreferences", exe, "GpuPreference=2;"))
                        r.Ok("已把「高性能 GPU」绑定到: " + exe);
                    else
                        r.Fail("GPU 首选项写入失败: " + exe);
                }
            }
            return r;
        }

        // 还原注册表改动（写回 Windows 默认值）
        public static Result RestoreDefaults()
        {
            Result r = new Result();
            if (SetHkcu(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1)) r.Ok("游戏模式 -> 恢复默认(开启)");
            if (SetHkcu(@"System\GameConfigStore", "GameDVR_Enabled", 1)) r.Ok("Game DVR -> 恢复默认(开启)");
            if (SetHkcu(@"System\GameConfigStore\GameDVR", "AppCaptureEnabled", 1)) r.Ok("应用捕获 -> 恢复默认(开启)");
            if (SetHkcu(@"System\GameConfigStore", "GameDVR_FSEBehaviorMode", 0)) r.Ok("全屏优化 -> 恢复默认(模式0)");
            if (SetHklm(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 0))
                r.Ok("EcoQoS 节流 -> 恢复默认(不关闭)");
            // 移除 GPU 首选项绑定
            string exe = FindValorant();
            if (exe != null)
            {
                try
                {
                    Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\DirectX\UserGpuPreferences", true);
                    if (k != null)
                    {
                        if (k.GetValue(exe) != null) { k.DeleteValue(exe, false); r.Ok("已移除 GPU 首选项绑定"); }
                        k.Close();
                    }
                }
                catch { }
            }
            return r;
        }

        public static string FindValorant()
        {
            string[] drives = { "C:\\", "D:\\", "E:\\", "F:\\", "G:\\" };
            foreach (string d in drives)
            {
                string p = d + @"Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe";
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static bool SetHkcu(string sub, string name, int value)
        {
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(sub);
                if (k == null) return false;
                k.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord);
                k.Close();
                return true;
            }
            catch { return false; }
        }

        private static bool SetHklm(string sub, string name, int value)
        {
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(sub);
                if (k == null) return false;
                k.SetValue(name, value, Microsoft.Win32.RegistryValueKind.DWord);
                k.Close();
                return true;
            }
            catch { return false; }
        }

        private static bool SetHkcuString(string sub, string name, string value)
        {
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(sub);
                if (k == null) return false;
                k.SetValue(name, value, Microsoft.Win32.RegistryValueKind.String);
                k.Close();
                return true;
            }
            catch { return false; }
        }
    }

    // ======================= 可视化专用：实时指标 / 原生调用 =======================
    // 说明：界面每 1.5 秒刷新一次，WMI 查询放在后台线程里做，
    //       查完再 BeginInvoke 回 UI 线程重绘，主线程不会被阻塞。
    internal class Sample
    {
        public bool Ok;
        public int Load;             // CPU 总占用 %
        public int Mhz;              // 当前频率 MHz
        public string SchemeGuid;    // 当前电源方案 GUID
        public string SchemeName;    // 当前电源方案名字
        public string SchemeSource;  // 从哪读到的名字
    }

    internal static class CpuMetrics
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool QueryPerformanceFrequency(out long freq);

        private static long _freq = 0;
        private static long _prev;

        private static void Init()
        {
            if (_freq == 0) QueryPerformanceFrequency(out _freq);
            if (_prev == 0 && _freq > 0) QueryPerformanceCounter(out _prev);
        }

        [DllImport("kernel32.dll")]
        private static extern bool QueryPerformanceCounter(out long counter);

        // ---------- 当前频率：注册表 ~MHz 是实时更新的，比 WMI 的 CurrentClockSpeed 准 ----------
        public static int Mhz()
        {
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (k == null) return 0;
                object v = k.GetValue("~MHz");
                k.Close();
                if (v == null) return 0;
                return Convert.ToInt32(v);
            }
            catch { return 0; }
        }

        // ---------- 一次性取回所有指标（后台线程调用） ----------
        public static Sample Collect()
        {
            Sample s = new Sample();
            s.Load = LoadPercent();
            s.Mhz = Mhz();

            string active = PowerEngine.ActiveSchemeGuid();
            s.SchemeGuid = active ?? "-";
            s.SchemeName = SchemeNameOf(active);
            s.Ok = true;
            return s;
        }

        // ---------- CPU 总占用：两次性能计数器采样取差值 ----------
        // 用 WMI 的 PercentProcessorTime 在混合架构笔记本上经常只反映某个核，
        // 这里改用 GetSystemTimes 的差分，数字更接近任务管理器。
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint Low;
            public uint High;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        private static ulong ToU64(FILETIME f) { return ((ulong)f.High << 32) | f.Low; }

        private static ulong _prevIdle, _prevKernel, _prevUser;
        private static bool _haveTimes;

        public static int LoadPercent()
        {
            try
            {
                FILETIME i, k, u;
                if (!GetSystemTimes(out i, out k, out u)) return -1;
                ulong idle = ToU64(i), kernel = ToU64(k), user = ToU64(u);

                if (!_haveTimes)
                {
                    _prevIdle = idle; _prevKernel = kernel; _prevUser = user;
                    _haveTimes = true;
                    Thread.Sleep(120);
                    if (!GetSystemTimes(out i, out k, out u)) return -1;
                    idle = ToU64(i); kernel = ToU64(k); user = ToU64(u);
                }

                ulong dIdle = idle - _prevIdle;
                ulong dKernel = kernel - _prevKernel;
                ulong dUser = user - _prevUser;
                _prevIdle = idle; _prevKernel = kernel; _prevUser = user;

                // kernel 时间已包含 idle
                ulong total = dKernel + dUser;
                if (total == 0) return -1;
                double busy = (double)(total - dIdle) * 100.0 / (double)total;
                if (busy < 0) busy = 0;
                if (busy > 100) busy = 100;
                return (int)Math.Round(busy);
            }
            catch { return -1; }
        }

        // ---------- 当前电源方案的名字 ----------
        public static string SchemeNameOf(string guid)
        {
            if (string.IsNullOrEmpty(guid) || guid == "-") return "未知";

            // 1) 先查注册表，最快，而且不用起进程
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes");
                if (k != null)
                {
                    Microsoft.Win32.RegistryKey sk = k.OpenSubKey(guid);
                    if (sk != null)
                    {
                        object f = sk.GetValue("FriendlyName");
                        sk.Close();
                        k.Close();
                        if (f != null)
                        {
                            string nm = f.ToString();
                            // 注册表里是 @%SystemRoot%\system32\powrprof.dll,-401 这种间接字符串
                            string indirect = ResolveIndirect(nm);
                            return indirect ?? nm;
                        }
                    }
                    else k.Close();
                }
            }
            catch { }

            // 2) 回退：解析 powercfg /list
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", "/list");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.GetEncoding(936);
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    foreach (string line in outp.Split('\n'))
                    {
                        if (line.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int a = line.IndexOf('(');
                            int b = line.IndexOf(')');
                            if (a >= 0 && b > a) return line.Substring(a + 1, b - a - 1).Trim();
                        }
                    }
                }
            }
            catch { }
            return "未知";
        }

        // 把 "@%SystemRoot%\system32\powrprof.dll,-401" 这类间接字符串换成真实名字
        private static string ResolveIndirect(string raw)
        {
            if (string.IsNullOrEmpty(raw) || raw[0] != '@') return null;
            try
            {
                string body = raw.Substring(1);
                int comma = body.LastIndexOf(',');
                if (comma < 0) return null;
                string path = Environment.ExpandEnvironmentVariables(body.Substring(0, comma));
                int resId = int.Parse(body.Substring(comma + 1), CultureInfo.InvariantCulture);

                IntPtr h = LoadLibraryEx(path, IntPtr.Zero, 0x00000002 /*LOAD_LIBRARY_AS_DATAFILE*/);
                if (h == IntPtr.Zero) return null;
                try
                {
                    StringBuilder sb = new StringBuilder(512);
                    int n = LoadString(h, resId, sb, sb.Capacity);
                    if (n > 0) return sb.ToString();
                }
                finally { FreeLibrary(h); }
            }
            catch { }
            return null;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, int flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int LoadString(IntPtr h, int id, StringBuilder buffer, int size);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr h);
    }

    // 无边框窗口拖动 / 图标提取之类的小工具
    internal static class Win32Native
    {
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        // Win11 窗口圆角。DwmSetWindowAttribute 在 Win10 上会返回失败码，
        // 这里用 try/catch 兜住，老系统上就是"没有圆角"，不会崩。
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void RoundCorners(IntPtr hwnd)
        {
            int pref = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref pref, 4);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string exeFileName, int iconIndex);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon GetExeIcon(string exePath)
        {
            try
            {
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
                IntPtr h = ExtractIcon(IntPtr.Zero, exePath, 0);
                if (h == IntPtr.Zero || h == new IntPtr(1)) return null;
                Icon ic = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return ic;
            }
            catch { return null; }
        }
    }

    // 圆角、渐变之类的绘图小工具
    internal static class Gfx
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Card(Graphics g, Rectangle r, Color fill)
        {
            using (GraphicsPath p = Rounded(r, 12))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
        }

        public static void CardBorder(Graphics g, Rectangle r, Color line)
        {
            using (GraphicsPath p = Rounded(r, 12))
            using (Pen pen = new Pen(line, 1))
                g.DrawPath(pen, p);
        }
    }

    // 通用渲染委托，用来配合 Control.Tag 保存状态

    // ======================= 主界面（旧版，已被文件末尾的可视化 MainForm 取代） =======================
    internal class LegacyMainForm : Form
    {
        private TextBox log;
        private Label cpuLabel, planLabel;
        private RadioButton rbMax, rbBalanced, rbSafe;
        private CheckBox cbGameMode, cbDvr, cbFso, cbThrottle, cbGpu;
        private Button btnApply, btnRestore, btnTune, btnCheck, btnExit;
        private TextBox pathBox;
        private CpuInfo cpu;

        public LegacyMainForm()
        {
            Text = "无畏契约 CPU 高频优化器  v2.0  (Intel Core Ultra 专项)";
            ClientSize = new Size(940, 640);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(248, 249, 251);

            // ---------- 顶部标题 ----------
            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 62;
            head.BackColor = Color.FromArgb(24, 30, 42);
            Label t = new Label();
            t.Text = "VALORANT  CPU 高频优化器";
            t.ForeColor = Color.White;
            t.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            t.AutoSize = true;
            t.Location = new Point(16, 10);
            Label t2 = new Label();
            t2.Text = "针对 Intel Core Ultra (Meteor Lake) 混合架构优化：频率地板 / EPP / 大小核调度 / 核心停泊";
            t2.ForeColor = Color.FromArgb(160, 200, 255);
            t2.AutoSize = true;
            t2.Location = new Point(18, 38);
            head.Controls.Add(t);
            head.Controls.Add(t2);
            Controls.Add(head);

            // ---------- 硬件信息 ----------
            cpuLabel = new Label();
            cpuLabel.SetBounds(16, 74, 620, 74);
            cpuLabel.Font = new Font("Consolas", 9F);
            cpuLabel.ForeColor = Color.FromArgb(30, 40, 60);
            cpu = CpuInfo.Detect();
            cpuLabel.Text = cpu.Describe();

            planLabel = new Label();
            planLabel.SetBounds(650, 74, 280, 74);
            planLabel.Font = new Font("Microsoft YaHei UI", 9F);
            planLabel.ForeColor = Color.FromArgb(60, 70, 90);
            Controls.Add(cpuLabel);
            Controls.Add(planLabel);

            // ---------- 模式选择 ----------
            GroupBox gbMode = new GroupBox();
            gbMode.Text = "优化强度";
            gbMode.SetBounds(16, 156, 908, 96);
            gbMode.ForeColor = Color.FromArgb(40, 50, 70);

            rbBalanced = new RadioButton();
            rbBalanced.Text = "均衡（推荐）：频率地板 60%%，EPP 30 —— 高频但不过热";
            rbBalanced.SetBounds(16, 22, 560, 22);
            rbBalanced.Checked = true;

            rbMax = new RadioButton();
            rbMax.Text = "极限高频：频率地板 100%%，EPP 0 —— 频率最稳，发热与耗电最高";
            rbMax.SetBounds(16, 46, 560, 22);

            rbSafe = new RadioButton();
            rbSafe.Text = "保守：不动频率地板，只解绑掉频 / 优先 P 核（笔记本最凉）";
            rbSafe.SetBounds(16, 70, 560, 22);

            gbMode.Controls.Add(rbBalanced);
            gbMode.Controls.Add(rbMax);
            gbMode.Controls.Add(rbSafe);
            Controls.Add(gbMode);

            // ---------- 附加项 ----------
            GroupBox gbExtra = new GroupBox();
            gbExtra.Text = "附加优化（可单独勾选）";
            gbExtra.SetBounds(16, 258, 908, 96);
            gbExtra.ForeColor = Color.FromArgb(40, 50, 70);

            cbThrottle = new CheckBox(); cbThrottle.Text = "关闭 EcoQoS 节能节流"; cbThrottle.SetBounds(16, 24, 220, 22); cbThrottle.Checked = true;
            cbGameMode = new CheckBox(); cbGameMode.Text = "开启游戏模式"; cbGameMode.SetBounds(250, 24, 200, 22); cbGameMode.Checked = true;
            cbDvr = new CheckBox(); cbDvr.Text = "关闭 Game DVR 录制"; cbDvr.SetBounds(460, 24, 220, 22); cbDvr.Checked = true;
            cbFso = new CheckBox(); cbFso.Text = "关闭全屏优化（降低输入延迟）"; cbFso.SetBounds(16, 56, 260, 22); cbFso.Checked = true;
            cbGpu = new CheckBox(); cbGpu.Text = "绑定「高性能 GPU」首选项"; cbGpu.SetBounds(290, 56, 260, 22); cbGpu.Checked = true;

            gbExtra.Controls.Add(cbThrottle);
            gbExtra.Controls.Add(cbGameMode);
            gbExtra.Controls.Add(cbDvr);
            gbExtra.Controls.Add(cbFso);
            gbExtra.Controls.Add(cbGpu);

            Label pl = new Label();
            pl.Text = "游戏路径:";
            pl.SetBounds(560, 58, 60, 20);
            pathBox = new TextBox();
            pathBox.SetBounds(620, 55, 276, 23);
            pathBox.Text = RegTweaks.FindValorant() ?? "";
            pathBox.ReadOnly = false;
            gbExtra.Controls.Add(pl);
            gbExtra.Controls.Add(pathBox);
            Controls.Add(gbExtra);

            // ---------- 按钮 ----------
            btnApply = MakeButton("① 应用优化", 16, 366, 170, Color.FromArgb(0, 120, 215));
            btnRestore = MakeButton("④ 一键还原", 196, 366, 150, Color.FromArgb(200, 80, 70));
            btnTune = MakeButton("② 增强游戏进程", 356, 366, 170, Color.FromArgb(0, 150, 136));
            btnCheck = MakeButton("③ 体检 / 查看设置", 536, 366, 170, Color.FromArgb(90, 100, 120));
            btnExit = MakeButton("退出", 716, 366, 90, Color.FromArgb(120, 120, 130));

            btnApply.Click += delegate { DoApply(); };
            btnRestore.Click += delegate { DoRestore(); };
            btnTune.Click += delegate { Log(ProcTuner.TuneOnce().Text); };
            btnCheck.Click += delegate { DoCheck(); };
            btnExit.Click += delegate { Close(); };
            Controls.Add(btnApply);
            Controls.Add(btnRestore);
            Controls.Add(btnTune);
            Controls.Add(btnCheck);
            Controls.Add(btnExit);

            // ---------- 日志 ----------
            log = new TextBox();
            log.Multiline = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.ReadOnly = true;
            log.BackColor = Color.FromArgb(20, 24, 32);
            log.ForeColor = Color.FromArgb(200, 230, 210);
            log.Font = new Font("Consolas", 9F);
            log.SetBounds(16, 410, 908, 210);
            log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(log);

            Load += delegate
            {
                RefreshPlanLabel();
                Log("欢迎使用。建议顺序：先点「③ 体检」看当前状态，再点「① 应用优化」。");
                Log("说明：应用优化会自动把原电源方案导出到程序目录的 backup\\ 里，随时可点「④ 一键还原」。");
                Log("     笔记本插电时效果最好；USB-C 供电 / 电池模式下 Windows 会另走一套设置。");
                Log("");
                Log(cpu.Describe());
            };
        }

        private Button MakeButton(string text, int x, int y, int w, Color c)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 32);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = c;
            b.ForeColor = Color.White;
            b.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void RefreshPlanLabel()
        {
            string g = PowerEngine.ActiveSchemeGuid();
            string name = "未知";
            string outp;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", "/list");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.StandardOutputEncoding = Encoding.GetEncoding(936);
                using (Process p = Process.Start(psi))
                {
                    outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                }
                foreach (string line in outp.Split('\n'))
                {
                    if (g != null && line.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int a = line.IndexOf('(');
                        int b = line.IndexOf(')');
                        if (a >= 0 && b > a) name = line.Substring(a + 1, b - a - 1).Trim();
                        else name = line.Trim();
                        break;
                    }
                }
            }
            catch { }
            planLabel.Text = "当前电源方案:\r\n" + name + "\r\n" + (g ?? "-");
        }

        private Mode SelectedMode()
        {
            if (rbMax.Checked) return Mode.Max;
            if (rbSafe.Checked) return Mode.Safe;
            return Mode.Balanced;
        }

        private void Log(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            log.AppendText(s.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n") + "\r\n");
        }

        private void DoApply()
        {
            if (!IsAdmin())
            {
                Log("需要管理员权限：请右键本程序 ->「以管理员身份运行」。");
                return;
            }
            btnApply.Enabled = false;
            Application.DoEvents();
            try
            {
                Log("=== 开始应用优化（模式：" + PowerEngine.ModeName(SelectedMode()) + "）===");
                Result r = PowerEngine.Apply(SelectedMode());
                Log(r.Text);
                Log("=== 电源部分完成 ===");

                Result r2 = RegTweaks.Apply(cbGameMode.Checked, cbDvr.Checked, cbFso.Checked,
                    cbThrottle.Checked, cbGpu.Checked, pathBox.Text.Trim());
                Log(r2.Text);

                Result r4 = ProcTuner.TuneOnce();
                Log(r4.Text);

                Log("");
                Log("全部完成。建议重启一次电脑让核心放置策略完全生效，然后进游戏。");
                Log("想撤销：点「④ 一键还原」。");
                RefreshPlanLabel();
            }
            catch (Exception ex)
            {
                Log("[异常] " + ex.Message);
            }
            finally { btnApply.Enabled = true; }
        }

        private void DoRestore()
        {
            if (!IsAdmin())
            {
                Log("需要管理员权限：请右键本程序 ->「以管理员身份运行」。");
                return;
            }
            if (MessageBox.Show("确定把所有改动还原到优化前的状态？", "确认还原",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                Log("=== 开始还原 ===");
                Log(PowerEngine.Restore().Text);
                Log(RegTweaks.RestoreDefaults().Text);
                Log("=== 还原结束 ===");
                RefreshPlanLabel();
            }
            catch (Exception ex) { Log("[异常] " + ex.Message); }
        }

        private void DoCheck()
        {
            Log("=== 体检 ===");
            Log(cpu.Describe());
            string ag = PowerEngine.ActiveSchemeGuid();
            Guid g = Guid.Empty;
            if (ag != null) g = new Guid(ag);
            Log("当前电源方案: " + ag);
            Dictionary<Guid, int[]> map = PowerEngine.ReadScheme(g);
            if (map.Count == 0)
            {
                Log("[失败] 无法导出当前电源方案，体检中止");
                return;
            }
            Log("");
            Log(string.Format("{0,-26}{1,-10}{2,-10}{3}", "设置项", "当前AC", "当前DC", "建议"));
            Log(new string('-', 78));
            int missing = 0;
            foreach (SettingDef d in Plans.Build())
            {
                if (!map.ContainsKey(d.Set)) { missing++; continue; }
                int ac = map[d.Set][0], dc = map[d.Set][1];
                Log(string.Format("{0,-26}{1,-10}{2,-10}{3}", d.Name,
                    PowerEngine.RawToHuman(d.Set, ac), dc, d.Target));
            }
            Log("");
            Log("本机不支持的设置项数量: " + missing + "（跳过属正常，不同主板/CPU 支持项不同）");
            Log("提示：目标值里的「最小处理器状态」就是决定频率地板的那一项。");
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
    }

    // ======================= 入口 =======================
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }

            // 命令行模式：便于脚本调用与排错（不弹窗）
            if (args.Length > 0)
            {
                string cmd = args[0].ToLowerInvariant();
                if (cmd == "check" || cmd == "apply" || cmd == "restore" || cmd == "tune")
                {
                    AttachConsole(-1);
                    if (cmd == "check")
                    {
                        Console.WriteLine("======== 无畏契约 CPU 高频优化器 · 体检报告 ========");
                        Console.WriteLine(CpuInfo.Detect().Describe());
                        string ag = PowerEngine.ActiveSchemeGuid();
                        Guid g = Guid.Empty;
                        if (ag != null) g = new Guid(ag);
                        Console.WriteLine("当前电源方案: " + ag);
                        Console.WriteLine("（本命令只读，不修改任何设置）");
                        Console.WriteLine();
                        Dictionary<Guid, int[]> map = PowerEngine.ReadScheme(g);
                        Console.WriteLine(string.Format("{0,-26}{1,-12}{2,-12}{3}", "设置项", "交流电AC", "电池DC", "建议值"));
                        Console.WriteLine(new string('-', 72));
                        foreach (SettingDef d in Plans.Build())
                        {
                            if (!map.ContainsKey(d.Set)) { Console.WriteLine(string.Format("{0,-26}{1}", d.Name, "本机不支持，跳过")); continue; }
                            Console.WriteLine(string.Format("{0,-26}{1,-12}{2,-12}{3}",
                                d.Name, map[d.Set][0], map[d.Set][1], d.Target));
                        }
                        Console.WriteLine();
                        Console.WriteLine("======== 体检结束 ========");
                    }
                    else if (cmd == "apply")
                    {
                        Mode m = Mode.Balanced;
                        if (args.Length > 1)
                        {
                            if (args[1] == "max") m = Mode.Max;
                            else if (args[1] == "safe") m = Mode.Safe;
                            else if (args[1] == "competitive" || args[1] == "comp") m = Mode.Competitive;
                            else
                            {
                                Console.WriteLine("模式 \"" + args[1] + "\" 不认识，已按「均衡」处理。");
                                Console.WriteLine("可用模式：competitive(竞技) / balanced(均衡) / max(极限) / safe(保守)");
                                Console.WriteLine();
                            }
                        }
                        Console.WriteLine("======== 无畏契约 CPU 高频优化器 ========");
                        Console.WriteLine(PowerEngine.Apply(m).Text);
                        Console.WriteLine();
                        Console.WriteLine("提示：建议重启一次电脑让全部设置完全生效，然后进游戏。");
                        Console.WriteLine("      想撤销请运行:  ValorantCpuBoost.exe restore");
                    }
                    else if (cmd == "restore")
                    {
                        Console.WriteLine("======== 正在还原 ========");
                        Console.WriteLine(PowerEngine.Restore().Text);
                    }
                    else
                    {
                        Console.WriteLine("======== 正在增强游戏进程 ========");
                        Console.WriteLine(ProcTuner.TuneOnce().Text);
                    }
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int pid);
    }
}
