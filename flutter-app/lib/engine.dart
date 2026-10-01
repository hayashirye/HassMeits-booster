// 引擎桥 —— Flutter 侧唯一和系统打交道的地方。
//
// ★★ 为什么不在这里写 FFI：现有 C# 引擎是 2273 行踩过几十个坑的 Win32 代码
//    （Vanguard 保护进程上 MainModule 要 789ms 且只返回空串、能效核掩码
//    0x3003FC 是本机实测出来的、计划任务的电池标志、每 4 秒起一个 powercfg.exe
//    会把守护的 CPU 开销顶到 390ms/分钟……）。用 dart:ffi 重写一遍等于把这些
//    坑重新踩一遍，而且换不来任何好处 —— 引擎本来就是无头的，CLI 契约很干净。
//
// ★ 走 `--out <文件>` 而不是读 stdout：C# 那侧用 WriteAllText + UTF8Encoding(false)
//   写文件，字节确定；而 Process.run 抓 stdout 会经过一层 Windows 控制台编码，
//   中文容易变成问号。
import 'dart:convert';
import 'dart:io';

import 'models.dart';

class Engine {
  Engine._();
  static final Engine i = Engine._();

  /// 自己所在的目录（Release 文件夹）。
  static String get _selfDir =>
      File(Platform.resolvedExecutable).parent.path;

  /// 拼路径。**不用 package:path** —— 这项目一直守着「不引第三方包」，
  /// 而这里只需要 Windows 的反斜杠拼接，`Directory`/`File` 都认。
  static String _j(String a, String b) =>
      a.endsWith(r'\') ? '$a$b' : '$a\\$b';

  /// 引擎 exe 的位置。按顺序找，第一个存在的就用。
  ///
  /// ★★ 这里**不许写死任何一台机器的路径**。
  ///    之前 `_candidates` 第一项是 `D:\test\valorant-cpu-boost\ValorantBoost.exe`，
  ///    在我这台机器上永远命中，于是从没暴露过问题；但把整个文件夹拷给别人，
  ///    他的机器上没有 D:\test 这一层，界面就会显示「没找到引擎」，五个页面全空。
  static List<String> candidates() {
    final out = <String>[];

    // ① 环境变量显式指定 —— 绿色部署时最省事的一条
    final home = Platform.environment['VALORANT_BOOST_HOME'];
    if (home != null && home.trim().isNotEmpty) {
      out.add(_j(home.trim(), 'ValorantBoost.exe'));
    }

    // ② 和自己放在同一个文件夹 —— 最自然的「解压就能用」
    final self = _selfDir;
    out.add(_j(self, 'ValorantBoost.exe'));

    // ③ 往上找 1..6 级 —— 覆盖开发时的
    //    flutter-app\build\windows\x64\runner\Release\ 这种深布局
    //    （Release 往上 5 级正好是仓库根，引擎就在那儿），
    //    以及「flutter-app 和引擎平级」「引擎在 build\ 里」这些情况。
    var up = self;
    for (var i = 1; i <= 6; i++) {
      up = _j(up, '..');
      out.add(_j(up, 'ValorantBoost.exe'));
    }

    // ④ 正式安装位置（install 装出来的地方）
    out.add(r'C:\ProgramData\ValorantBoost\ValorantBoost.exe');

    // ⑤ 引擎自己的兜底目录（one\Core.cs:556 —— exe 目录不可写时退到这里）
    final appdata = Platform.environment['APPDATA'];
    if (appdata != null && appdata.isNotEmpty) {
      out.add(_j(_j(appdata, 'ValorantCpuBoost'), 'ValorantBoost.exe'));
    }
    final local = Platform.environment['LOCALAPPDATA'];
    if (local != null && local.isNotEmpty) {
      out.add(_j(_j(local, 'ValorantBoost'), 'ValorantBoost.exe'));
    }

    // ⑥ 我开发时用的老位置。放最后 —— 只在别处都找不到时才轮到它，
    //    这样它在别人机器上不会「抢答」出一个不存在的路径。
    out.add(r'D:\test\valorant-cpu-boost\ValorantBoost.exe');

    return out;
  }

  String? _exe;
  String? get exe => _exe;

  /// 最近一次错误，给界面显示用
  String lastError = '';

  /// 日志/备份所在的目录。`locate()` 时定下来。
  String _dataDir = '';

  // ★★ 日志和备份的路径**不能写死**，得跟着引擎走。
  //
  //    引擎那边的规则（one\Core.cs:549-563）是：
  //      目录 = exe 所在目录；那个目录不可写时退到 %APPDATA%\ValorantCpuBoost。
  //    而守护任务是 install 的时候用 `watch --dir "<当时的目录>"`
  //    把目录烤进计划任务的（one\Ui.cs:1095），所以守护写的日志
  //    可能并不挨着 exe（本机就是这样：exe 在 C:\ProgramData\ValorantBoost，
  //    日志却在 D:\test\valorant-cpu-boost）。
  //
  //    与其去猜这条规则，不如把所有「可能的位置」列出来，谁的最新用谁。
  List<String> _dataDirCandidates() {
    final out = <String>[];
    final home = Platform.environment['VALORANT_BOOST_HOME'];
    if (home != null && home.trim().isNotEmpty) out.add(home.trim());
    if (_exe != null) out.add(File(_exe!).parent.path);
    out.add(_selfDir);
    out.add(r'C:\ProgramData\ValorantBoost');
    final appdata = Platform.environment['APPDATA'];
    if (appdata != null && appdata.isNotEmpty) {
      out.add(_j(appdata, 'ValorantCpuBoost'));
    }
    final local = Platform.environment['LOCALAPPDATA'];
    if (local != null && local.isNotEmpty) {
      out.add(_j(local, 'ValorantBoost'));
    }
    out.add(r'D:\test\valorant-cpu-boost'); // 老位置，兜底
    return out;
  }

  /// 在候选目录里挑一个有 `boost-log.txt` 且最新的；都没有就用第一个候选。
  void _resolveDataDir() {
    String? best;
    DateTime? bestTime;
    for (final d in _dataDirCandidates()) {
      try {
        final f = File(_j(d, 'boost-log.txt'));
        if (!f.existsSync()) continue;
        final t = f.lastModifiedSync();
        if (bestTime == null || t.isAfter(bestTime)) {
          bestTime = t;
          best = d;
        }
      } catch (_) {}
    }
    // 一个都不存在（全新机器，还没跑过守护）→ 用引擎所在目录，
    // 这样引擎一写日志我们就读得到。
    _dataDir = best ??
        (_exe != null ? File(_exe!).parent.path : _selfDir);
  }

  File get _logFile => File(_j(_dataDir, 'boost-log.txt'));
  File get _backupFile => File(_j(_dataDir, 'boost-backup.txt'));

  bool locate() {
    for (final c in candidates()) {
      if (File(c).existsSync()) {
        _exe = c;
        _resolveDataDir();
        return true;
      }
    }
    _exe = null;
    _resolveDataDir();
    lastError = '找不到引擎：${candidates().join(' 或 ')}';
    return false;
  }

  /// 跑一条引擎命令。`args` 不含 --out，这里自己加。
  Future<RunResult> run(List<String> args, {Duration? timeout}) async {
    final sw = Stopwatch()..start();
    if (_exe == null && !locate()) {
      return RunResult(-1, '', lastError, sw.elapsedMilliseconds);
    }
    final tmp = File(
        '${Directory.systemTemp.path}\\vcb-flutter-${DateTime.now().microsecondsSinceEpoch}.txt');
    try {
      final r = await Process.run(
        _exe!,
        [...args, '--out', tmp.path],
        stdoutEncoding: null,
        stderrEncoding: null,
      ).timeout(timeout ?? const Duration(seconds: 60));

      var text = '';
      if (tmp.existsSync()) {
        final b = await tmp.readAsBytes();
        text = _stripBom(utf8.decode(b, allowMalformed: true));
      }
      if (text.trim().isEmpty) {
        // 兜底：极老的引擎没有 --out，就走 stdout
        final b = r.stdout as List<int>?;
        if (b != null && b.isNotEmpty) {
          text = _stripBom(utf8.decode(b, allowMalformed: true));
        }
      }
      return RunResult(r.exitCode, text, '', sw.elapsedMilliseconds);
    } catch (e) {
      return RunResult(-1, '', '$e', sw.elapsedMilliseconds);
    } finally {
      try {
        if (tmp.existsSync()) tmp.deleteSync();
      } catch (_) {}
    }
  }

  static String _stripBom(String s) =>
      s.startsWith('\uFEFF') ? s.substring(1) : s;

  // ---------------------------------------------------------------- 体检

  Future<CheckSummary> check() async {
    final r = await run(['check']);
    return parseCheck(r.text);
  }

  static CheckSummary parseCheck(String text) {
    final lines = text.split(RegExp(r'\r?\n'));
    String scheme = '';
    String guid = '';
    final rows = <PowerRow>[];
    int ok = 0, bad = 0, na = 0;
    final footer = <String>[];

    for (final raw in lines) {
      final s = raw.trimRight();
      if (s.trim().isEmpty) continue;

      if (s.startsWith('当前电源方案')) {
        final i = s.indexOf('：');
        scheme = i >= 0 ? s.substring(i + 1).trim() : '';
        continue;
      }
      if (s.startsWith('方案 GUID')) {
        final i = s.indexOf('：');
        guid = i >= 0 ? s.substring(i + 1).trim() : '';
        continue;
      }
      if (s.startsWith('---')) continue;
      if (s.startsWith('设置项')) continue; // 表头
      if (s.startsWith('---')) continue;

      if (s.startsWith('符合 ') && s.contains('项')) {
        final m = RegExp(r'符合\s*(\d+)\s*项，不符合\s*(\d+)\s*项，本机无此项\s*(\d+)\s*项')
            .firstMatch(s);
        if (m != null) {
          ok = int.parse(m.group(1)!);
          bad = int.parse(m.group(2)!);
          na = int.parse(m.group(3)!);
        }
        continue;
      }
      if (s.contains('项不符合')) {
        footer.add(s.trim());
        continue;
      }

      final p = s.split(RegExp(r'\s+')).where((x) => x.isNotEmpty).toList();
      if (p.length < 4) continue;

      // ★ 从右边数三个字段，剩下的全算名字 —— 名字里可能有空格（「能效偏好 EPP」）
      final n = p.length;
      final name = p.sublist(0, n - 3).join(' ');
      rows.add(PowerRow(name, p[n - 3], p[n - 2], p[n - 1]));
    }

    return CheckSummary(
      schemeName: scheme,
      schemeGuid: guid,
      power: rows,
      okCount: ok,
      badCount: bad,
      naCount: na,
      footer: footer.join('\n'),
    );
  }

  // ------------------------------------------------------------ 后台抢 CPU

  Future<List<BgRow>> bgcpu({Duration timeout = const Duration(seconds: 30)}) async {
    final r = await run(['bgcpu'], timeout: timeout);
    return parseBg(r.text);
  }

  /// 解析 `bgcpu` / `SysAudit.Report` 的输出。表尾那行「合计 xxx% 单核」单独取。
  static List<BgRow> parseBg(String text) {
    final out = <BgRow>[];
    for (final raw in text.split(RegExp(r'\r?\n'))) {
      final s = raw.trim();
      if (s.isEmpty) continue;
      if (s.startsWith('---') || s.startsWith('占单核') || s.startsWith('进程')) {
        continue;
      }
      // 实际输出形如：「98.4%     DeepSeek Harness        AI 助手正在干活 —— …」
      //
      // ★★ 不能用 `\S+` 取名字。进程名里真的有空格 —— C# 侧 SysAudit.Note()
      //    的查表键里就写着 "deepseek harness"（两个词），而它恰好是本机占
      //    单核最高的那一行。用 \S+ 会把这一行切成 name="DeepSeek"、
      //    note="Harness  AI 助手正在干活 …"，名字和说明双双错位。
      //    C# 侧是定宽填充（PadR），名字列与说明列之间必然隔着 ≥2 个空格，
      //    而名字内部的空格只可能是一个。所以按「2 个以上空格」切才稳。
      final m = RegExp(r'^([\d.]+)%\s+(.*)$').firstMatch(s);
      if (m == null) continue;
      final parts = m.group(2)!.split(RegExp(r'\s{2,}'));
      final name = parts[0].trim();
      if (name.isEmpty) continue;
      out.add(BgRow(
        name,
        double.tryParse(m.group(1)!) ?? 0,
        parts.length > 1 ? parts.sublist(1).join('  ').trim() : '',
      ));
    }
    return out;
  }

  static String bgFooter(String text) {
    for (final raw in text.split(RegExp(r'\r?\n'))) {
      if (raw.contains('合计') && raw.contains('单核')) return raw.trim();
    }
    return '';
  }

  /// `bgcpu` 表尾的判读行 —— 以「判读：」开头的总评，和以「★」开头的专项提醒。
  ///
  /// ★ C# 界面的 `_bgNote` 是把这两行一起显示出来的，而带 ★ 的那条往往才是
  ///   最可操作的一句（例如实测到 dwm 在吃 CPU ⇒ 说明游戏跑的是无边框窗口，
  ///   改独占全屏能直接省掉这部分）。只取「合计」那一行会把它丢掉。
  static String bgJudge(String text) {
    final out = <String>[];
    for (final raw in text.split(RegExp(r'\r?\n'))) {
      final s = raw.trim();
      if (s.startsWith('判读：') || s.startsWith('★')) out.add(s);
    }
    return out.join('\n');
  }

  // ---------------------------------------------------------------- 游戏

  Future<GameInfo> audit(String gameKey) async {
    final r = await run(['audit', gameKey]);
    return parseAudit(r.text, gameKey);
  }

  static GameInfo parseAudit(String text, String key) {
    final lines = text.split(RegExp(r'\r?\n'));
    String exe = '', procs = '', tier = '', note = '', cfg = '';
    var found = false;
    final checks = <GameCheck>[];
    var cfgSeen = false;

    for (final raw in lines) {
      final s = raw.trimRight();
      if (s.trim().isEmpty) continue;

      if (s.startsWith('主程序')) {
        exe = _afterColon(s);
        continue;
      }
      if (s.startsWith('显卡绑定')) continue;
      if (s.startsWith('进程名')) {
        procs = _afterColon(s);
        continue;
      }
      if (s.startsWith('推荐档位')) {
        tier = _afterColon(s);
        continue;
      }
      if (s.startsWith('说明')) {
        note = _afterColon(s);
        continue;
      }
      if (s.startsWith('配置文件')) {
        cfg = _afterColon(s);
        cfgSeen = true;
        continue;
      }
      if (s.startsWith('=====')) continue;
      if (s.startsWith('不符合') || s.startsWith('值得注意')) continue;

      // 「  动态阴影 | 关 | 符合 | 阴影是 Apex 里最贵的一项」
      final p = s.split('|').map((x) => x.trim()).toList();
      if (p.length < 3) continue;
      found = true;
      checks.add(GameCheck(
        p[0],
        p[1],
        p[2],
        p.length >= 4 ? p.sublist(3).join(' | ') : '',
      ));
    }

    return GameInfo(
      key: key,
      name: key == 'apex' ? 'Apex Legends' : '无畏契约',
      exe: exe,
      procs: procs,
      tier: tier,
      note: note,
      configPath: cfg,
      configFound: cfgSeen && cfg.isNotEmpty && found,
      checks: checks,
    );
  }

  static String _afterColon(String s) {
    final i = s.indexOf('：');
    if (i >= 0) return s.substring(i + 1).trim();
    final j = s.indexOf(':');
    return j >= 0 ? s.substring(j + 1).trim() : '';
  }

  // ---------------------------------------------------------------- 亲和性

  Future<PinReport> pinstat() async {
    final r = await run(['pinstat']);
    return parsePin(r.text);
  }

  /// 解析 `pinstat` 的完整输出。
  ///
  /// 引擎会先打两行掩码（`one\Core.cs:2428-2429`）：
  /// ```
  /// 能效核掩码 = 0x3003FC   （逻辑核 2-9、20-21）
  /// 性能核掩码 = 0xFFC03   （逻辑核 0-1、10-19）
  /// ```
  /// ★ 这两行是**引擎自己报的、它实际会用的掩码**，不是我们猜的。
  ///   以前这里把它们 `continue` 掉了，界面于是只能写死 `0x3003FC` 去比对 ——
  ///   换台机器（尤其 AMD）就永远显示「没有进程被压」。
  static PinReport parsePin(String text) {
    final rows = <PinRow>[];
    int? em;
    int? pm;
    var ed = '';
    var pd = '';
    for (final raw in text.split(RegExp(r'\r?\n'))) {
      final s = raw.trim();
      if (s.isEmpty || s.startsWith('---') || s.startsWith('进程名')) continue;
      if (s.startsWith('能效核掩码')) {
        em = _maskOf(s);
        ed = _paren(s);
        continue;
      }
      if (s.startsWith('性能核掩码')) {
        pm = _maskOf(s);
        pd = _paren(s);
        continue;
      }
      if (s.startsWith('（')) continue;
      // 「  leigod     24292  0x3FFFFF  （全部核心）」
      final m = RegExp(r'^(\S+)\s+(\d+)\s+(0x[0-9A-Fa-f]+)').firstMatch(s);
      if (m == null) continue;
      rows.add(PinRow(m.group(1)!, int.parse(m.group(2)!), m.group(3)!));
    }
    return PinReport(
      ecoreMask: em,
      pcoreMask: pm,
      ecoreDesc: ed,
      pcoreDesc: pd,
      rows: rows,
    );
  }

  static int? _maskOf(String s) {
    final m = RegExp(r'0x([0-9A-Fa-f]+)').firstMatch(s);
    return m == null ? null : int.tryParse(m.group(1)!, radix: 16);
  }

  static String _paren(String s) {
    final i = s.indexOf('（');
    final j = s.indexOf('）');
    if (i >= 0 && j > i) return s.substring(i + 1, j).trim();
    return '';
  }

  // ---------------------------------------------------------------- 动作

  Future<RunResult> apply(String modeKey) => run(['apply', modeKey]);
  Future<RunResult> restore() => run(['restore']);
  Future<RunResult> pin() => run(['pin'], timeout: const Duration(seconds: 90));
  Future<RunResult> unpin() => run(['unpin'], timeout: const Duration(seconds: 90));
  Future<RunResult> install() => run(['install']);
  Future<RunResult> uninstall() => run(['uninstall']);
  Future<RunResult> gpupref() => run(['gpupref']);
  Future<RunResult> autocheck() => run(['autocheck'], timeout: const Duration(seconds: 60));

  /// 持续负载性能测试 —— 跑 CLI 的 `bench`。
  ///
  /// ★★ 这**不是** C# GUI 上那个「开始性能测试」按钮的测试：
  ///    按钮走 `Bench.Full()`（`Ui.cs:1923`），报的是瞬时吞吐，
  ///    日志里形如「性能测试：8,644,628,480 次/秒」；
  ///    而 CLI 的 `bench` 走 `CmdBench`（`Ui.cs:2887`），是 5 轮 × 6 秒的
  ///    **持续负载**报告，输出里根本没有「次/秒」三个字。
  ///
  ///    我一开始照 GUI 的格式写了 `([\d,]+)\s*次/秒`，实测一个都匹配不到，
  ///    整个页面会永远空着。对 Flutter 版来说 CLI 这份反而更有价值 ——
  ///    它直接回答「这台机器在持续负载下会不会降频」。
  ///
  ///    [rounds] 可选，对应 `CmdBench` 的那个可选参数（默认 5 轮）。
  Future<BenchResult?> bench({int? rounds}) async {
    final args = rounds == null ? <String>['bench'] : <String>['bench', '$rounds'];
    // 每轮 6 秒，5 轮实测约 39 秒；给足余量，别让超时把结果吃掉。
    final r = await run(args, timeout: const Duration(minutes: 4));
    final b = parseBench(r.text);
    if (b == null) return null;
    final now = DateTime.now();
    final ts = '${now.year}-${_pad2(now.month)}-${_pad2(now.day)} '
        '${_pad2(now.hour)}:${_pad2(now.minute)}';
    return b.withWhen(ts);
  }

  /// 解析 `CmdBench`（`Ui.cs:2887-2948`）的报告。
  /// 取不到「中位数」就返回 null —— 说明命令失败或 C# 那边改了格式。
  static BenchResult? parseBench(String text) {
    double grab(String s) {
      final m = RegExp(r'([\d,]+(?:\.\d+)?)').firstMatch(s);
      if (m == null) return -1;
      return double.tryParse(m.group(1)!.replaceAll(',', '')) ?? -1;
    }

    double median = -1, first = -1, last = -1, drop = -1;
    double mhzStart = 0, mhzEnd = 0;
    int logical = 0, roundSecs = 0, parkStart = 0, parkEnd = 0;
    var rounds = <double>[];
    var cpu = '', verdict = '';

    for (final raw in text.split(RegExp(r'\r?\n'))) {
      final s = raw.trim();
      if (s.isEmpty) continue;

      // 「CPU        : Intel(R) Core(TM) Ultra 7 155H」
      if (s.startsWith('CPU')) {
        final i = s.indexOf(':');
        if (i >= 0) cpu = s.substring(i + 1).trim();
        continue;
      }
      if (s.startsWith('逻辑处理器')) {
        logical = grab(s).toInt();
        continue;
      }
      if (s.startsWith('每轮')) {
        roundSecs = grab(s).toInt();
        continue;
      }

      // 「第 1 轮     17,416,617,984    100.0%」
      final rp = RegExp(r'^第\s*\d+\s*轮\s+([\d,]+)').firstMatch(s);
      if (rp != null) {
        final v = double.tryParse(rp.group(1)!.replaceAll(',', ''));
        if (v != null) rounds.add(v);
        continue;
      }

      if (s.startsWith('中位数')) {
        median = grab(s);
        continue;
      }
      if (s.startsWith('首轮')) {
        first = grab(s);
        continue;
      }
      if (s.startsWith('末轮')) {
        last = grab(s);
        continue;
      }
      if (s.startsWith('首→末衰减')) {
        drop = grab(s);
        continue;
      }

      // 「开始前  估算频率 4,466 MHz   停泊 2/22」
      // 「结束时频率 : 估算 4,634 MHz   停泊 2/22」
      if (s.startsWith('开始前') || s.startsWith('结束时频率')) {
        // ★★ 这两行的写法不一样，别用同一个正则硬套：
        //    开始前  估算频率 4,466 MHz   停泊 2/22     ← 「估算频率」连着，没冒号
        //    结束时频率 : 估算 4,634 MHz   停泊 2/22   ← 「估算」后面直接跟数字
        //    我第一版只写了 `估算\s*([\d,]+)`，开始前那行匹配不到，
        //    结果 startMhz 恒为 0（是 parse_test 的频率断言抓出来的）。
        final fq = RegExp(r'估算(?:频率)?\s*([\d,]+)').firstMatch(s);
        final pk = RegExp(r'停泊\s*(\d+)').firstMatch(s);
        final fr = fq == null
            ? 0.0
            : (double.tryParse(fq.group(1)!.replaceAll(',', '')) ?? 0);
        final pr = pk == null ? 0 : (int.tryParse(pk.group(1)!) ?? 0);
        if (s.startsWith('开始前')) {
          mhzStart = fr;
          parkStart = pr;
        } else {
          mhzEnd = fr;
          parkEnd = pr;
        }
        continue;
      }

      if (s.startsWith('判读：')) {
        verdict = s;
        continue;
      }
    }

    if (median < 0) return null;
    if (first < 0) first = rounds.isEmpty ? median : rounds.first;
    if (last < 0) last = rounds.isEmpty ? median : rounds.last;
    if (drop < 0) drop = 0;

    return BenchResult(
      median: median,
      first: first,
      last: last,
      dropPct: drop,
      rounds: rounds,
      verdict: verdict,
      cpuName: cpu,
      logical: logical,
      roundSecs: roundSecs,
      startMhz: mhzStart,
      endMhz: mhzEnd,
      parkedStart: parkStart,
      parkedEnd: parkEnd,
      when: '',
    );
  }

  /// 两位补零（原来叫 `_2`，analyzer 的 non_constant_identifier_names 不喜欢）
  static String _pad2(int n) => n < 10 ? '0$n' : '$n';

  /// 自启动是否已装（读计划任务，用 schtasks 的输出判断，不启 powershell）
  Future<bool> autoStartInstalled() async {
    try {
      final r = await Process.run('schtasks', ['/query', '/tn', 'ValorantBoostWatcher'],
          stdoutEncoding: null, stderrEncoding: null);
      return r.exitCode == 0;
    } catch (_) {
      return false;
    }
  }

  // ---------------------------------------------------------------- 日志

  Future<List<String>> readLog({int maxLines = 400}) async {
    try {
      if (!_logFile.existsSync()) return [];
      final s = _stripBom(await _logFile.readAsString(encoding: utf8));
      final all = s.split(RegExp(r'\r?\n')).where((l) => l.trim().isNotEmpty).toList();
      if (all.length <= maxLines) return all.reversed.toList();
      return all.sublist(all.length - maxLines).reversed.toList();
    } catch (e) {
      lastError = '$e';
      return [];
    }
  }

  String get logPath => _logFile.path;
  String get backupPath => _backupFile.path;

  bool get hasLog => _logFile.existsSync();

  /// 打开资源管理器并选中文件
  Future<void> revealLog() async {
    if (!_logFile.existsSync()) return;
    await Process.run('explorer.exe', ['/select,', _logFile.path]);
  }

  /// 用默认程序打开文件（配置文件的「打开」按钮）
  Future<void> openPath(String path) async {
    if (path.isEmpty) return;
    await Process.run('cmd', ['/c', 'start', '', path]);
  }
}
