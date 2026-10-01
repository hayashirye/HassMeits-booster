// 数据模型 —— 全部对应现有 C# 引擎返回的结构。
//
// ★ 关键取舍：这里不做任何 Win32 调用。所有事实都由 ValorantBoost.exe 的无头
//   CLI 产出，Dart 侧只负责「跑命令 + 解析文本」。理由见 README。

/// 优化档位。key 直接就是 CLI 的 `apply <key>` 参数。
class ModeProfile {
  final String key;
  final String name;
  final String desc;
  const ModeProfile(this.key, this.name, this.desc);

  /// 和 C# 的 Modes.Build() 一致
  static const List<ModeProfile> all = [
    ModeProfile('competitive', '竞技', '插电时锁死高频，不插电时保持体面。最推荐。'),
    ModeProfile('max', '极限', '插不插电都拉满，最热最费电。台式机或一直插电的笔记本用。'),
    ModeProfile('balanced', '均衡', '日常使用与游戏兼顾，空闲时允许 CPU 省电。'),
    ModeProfile('safe', '保守', '只动最关键的三项，风险最小，随时可退。'),
  ];

  static ModeProfile byKey(String k) =>
      all.firstWhere((m) => m.key == k, orElse: () => all.first);
}

/// 体检页电源表的一行
class PowerRow {
  final String name;
  final String ac;
  final String dc;
  final String verdict;
  const PowerRow(this.name, this.ac, this.dc, this.verdict);

  bool get hasValue => ac.isNotEmpty && ac != '—' && ac != '--';
  int get kind => verdict == '符合' ? 0 : (verdict == '不符合' ? 1 : 2);
}

/// 后台抢 CPU 的一行
class BgRow {
  final String name;
  final double pct; // 占单核百分比
  final String note;
  const BgRow(this.name, this.pct, this.note);
}

/// 游戏配置检查的一行
class GameCheck {
  final String name;
  final String value;
  final String verdict;
  final String advice;
  const GameCheck(this.name, this.value, this.verdict, this.advice);

  int get kind =>
      verdict == '不符合' ? 1 : (verdict == '注意' ? 3 : (verdict == '符合' ? 0 : 2));
}

/// 被压到能效核的进程实例
class PinRow {
  final String name;
  final int pid;
  final String mask;
  const PinRow(this.name, this.pid, this.mask);

  /// ★ 以前这里写死了 `startsWith('0X3003FC')` —— 那是引擎在这台机器上的
  ///   能效核掩码。换台机器掩码就不是这个数了，写死会让「已压制」永远为假。
  ///   现在由调用方把引擎实际报的掩码传进来比对。
  bool pinnedAs(int? ecoreMask) {
    if (ecoreMask == null) return false;
    final s = mask.toUpperCase().replaceAll('0X', '');
    final v = int.tryParse(s, radix: 16);
    return v != null && v == ecoreMask;
  }
}

/// `pinstat` 的完整输出：两个掩码 + 进程表
class PinReport {
  final int? ecoreMask;
  final int? pcoreMask;
  final String ecoreDesc; // 「逻辑核 2-9、20-21」
  final String pcoreDesc;
  final List<PinRow> rows;

  const PinReport({
    this.ecoreMask,
    this.pcoreMask,
    this.ecoreDesc = '',
    this.pcoreDesc = '',
    this.rows = const [],
  });

  static const PinReport empty = PinReport();

  /// 引擎报掩码了吗（老引擎没有这两行）
  bool get hasMasks => ecoreMask != null && pcoreMask != null;
}

/// 一个游戏的完整状态
class GameInfo {
  final String key; // valorant / apex
  final String name;
  final String exe;
  final String procs;
  final String tier;
  final String note;
  final String configPath;
  final bool configFound;
  final List<GameCheck> checks;
  const GameInfo({
    required this.key,
    required this.name,
    required this.exe,
    required this.procs,
    required this.tier,
    required this.note,
    required this.configPath,
    required this.configFound,
    required this.checks,
  });

  static const GameInfo empty = GameInfo(
    key: '',
    name: '',
    exe: '',
    procs: '',
    tier: '',
    note: '',
    configPath: '',
    configFound: false,
    checks: [],
  );

  int get badCount => checks.where((c) => c.verdict == '不符合').length;
  int get warnCount => checks.where((c) => c.verdict == '注意').length;
}

/// 给数字加千位分隔符：17165254656 → `17,165,254,656`。
///
/// 性能测试的数字有 11 位，不分节根本读不出量级；C# 那边
/// `Bench.Full()` 的 `Fmt()` 也是这么显示的，保持一致。
String groupNum(double v) {
  final neg = v < 0;
  final s = v.abs().toStringAsFixed(0);
  final b = StringBuffer(neg ? '-' : '');
  for (var i = 0; i < s.length; i++) {
    if (i > 0 && (s.length - i) % 3 == 0) b.write(',');
    b.write(s[i]);
  }
  return b.toString();
}

/// 一次「持续负载」性能测试的结果。
///
/// ★★ 这跟 C# GUI 上那个「开始性能测试」按钮**不是同一个测试**：
///    按钮走 `Bench.Full()`（`Ui.cs:1923`），报的是瞬时吞吐
///    （单线程 + 全核各一个数，形如「8,644,628,480 次/秒」）；
///    这里对应的是 CLI 的 `bench`（`CmdBench`，`Ui.cs:2887`）——
///    5 轮 × 6 秒的持续负载，看的是**掉不掉频**，输出里根本没有「次/秒」。
class BenchResult {
  final double median; // 中位数 —— 头条数字
  final double first; // 首轮
  final double last; // 末轮
  final double dropPct; // 首→末衰减（%）
  final List<double> rounds; // 每轮原始数字
  final String verdict; // 引擎给的判读
  final String cpuName;
  final int logical;
  final int roundSecs;
  final double startMhz; // 测试前估算频率
  final double endMhz; // 测试后估算频率
  final int parkedStart;
  final int parkedEnd;
  final String when;

  const BenchResult({
    required this.median,
    required this.first,
    required this.last,
    required this.dropPct,
    required this.rounds,
    required this.verdict,
    required this.cpuName,
    required this.logical,
    required this.roundSecs,
    required this.startMhz,
    required this.endMhz,
    required this.parkedStart,
    required this.parkedEnd,
    required this.when,
  });

  BenchResult withWhen(String ts) => BenchResult(
        median: median,
        first: first,
        last: last,
        dropPct: dropPct,
        rounds: rounds,
        verdict: verdict,
        cpuName: cpuName,
        logical: logical,
        roundSecs: roundSecs,
        startMhz: startMhz,
        endMhz: endMhz,
        parkedStart: parkedStart,
        parkedEnd: parkedEnd,
        when: ts,
      );

  /// 阈值与 C# 的判读一致：>8% 算真降频，3%~8% 算轻微。
  bool get throttled => dropPct > 8;
  bool get slightDrop => dropPct > 3 && dropPct <= 8;
  int get kind => throttled ? 1 : (slightDrop ? 3 : 0);

  /// 本次测试实际耗时（秒）
  int get totalSecs => roundSecs * rounds.length;
}

/// 体检页的整体结论
class CheckSummary {
  final String schemeName;
  final String schemeGuid;
  final List<PowerRow> power;
  final int okCount;
  final int badCount;
  final int naCount;
  final String footer;
  const CheckSummary({
    required this.schemeName,
    required this.schemeGuid,
    required this.power,
    required this.okCount,
    required this.badCount,
    required this.naCount,
    required this.footer,
  });

  static const CheckSummary empty = CheckSummary(
    schemeName: '',
    schemeGuid: '',
    power: [],
    okCount: 0,
    badCount: 0,
    naCount: 0,
    footer: '',
  );
}

/// 引擎一次调用的结果
class RunResult {
  final int code;
  final String out;
  final String err;
  final int ms;
  const RunResult(this.code, this.out, this.err, this.ms);

  bool get ok => code == 0;
  String get text => out.trim().isEmpty ? err.trim() : out.trim();
}
