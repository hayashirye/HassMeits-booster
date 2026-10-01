// 全局状态。一个 ChangeNotifier，页面用 ListenableBuilder 订阅。
//
// 刻意不引 provider / riverpod —— 这个工具只有一个状态对象，加一层依赖管理
// 框架不划算，而且少一个包就少一个装不上的风险。
import 'package:flutter/material.dart';

import 'engine.dart';
import 'hardware.dart';
import 'models.dart';

class AppState extends ChangeNotifier {
  final Engine eng = Engine.i;

  // ---- 引擎位置 ----
  bool engineReady = false;
  String enginePath = '';

  // ---- 当前选中的游戏 ----
  String gameKey = 'valorant';

  // ---- 当前档位 ----
  String modeKey = 'competitive';

  // ---- 体检 ----
  CheckSummary check = CheckSummary.empty;
  List<BgRow> bg = [];
  String bgFooter = '';
  String bgJudge = '';
  bool checking = false;

  // ---- 游戏专项 ----
  final Map<String, GameInfo> games = {};
  bool auditing = false;

  // ---- 亲和性 ----
  List<PinRow> pins = [];
  bool pinning = false;
  /// 引擎报回来的两个掩码 + 进程表（掩码是引擎实际会用的，不是我们猜的）
  PinReport pinReport = PinReport.empty;

  // ---- 本机环境（每次启动都重新检测）----
  MachineInfo? machine;
  bool probing = false;

  // ---- 自启动 ----
  bool autoStart = false;

  // ---- 性能测试 ----
  BenchResult? bench;

  // ---- 日志 ----
  List<String> logLines = [];

  // ---- 最近一次动作的输出（主页那张「结果」卡） ----
  String lastOutput = '';
  String lastTitle = '还没有操作。';
  bool busy = false;

  Future<void> init() async {
    engineReady = eng.locate();
    enginePath = eng.exe ?? '';
    notifyListeners();
    // ★ 硬件检测不依赖引擎，先起头 —— 就算引擎没找到，也能把「这台机器是什么」
    //   显示出来（FFI 7 ms + 两次 reg query，合计约 300 ms）。
    //   **每次启动都重新测，不缓存**：用户可能换机器、插拔电源、改电源计划，
    //   而别人装的机器更是完全未知。
    final probing = probeMachine();
    if (!engineReady) {
      await probing;
      return;
    }
    // ★ 「游戏专项」页的数据是懒加载的（ensureGame）。这里必须主动拉一次，
    //   否则那一页会一直空着 —— 显示「还没有登记 / 没有找到配置文件」，
    //   因为只有切换游戏标签或点「重新检查」才会触发它，而启动时两者都没发生。
    await Future.wait<void>([
      refreshCheck(),
      refreshAutoStart(),
      refreshLog(),
      ensureGame(gameKey),
      refreshPins(),
      probing,
    ]);
  }

  /// 每次打开程序都重测本机环境（CPU 拓扑 / 型号 / 厂商 / 供电）。
  Future<void> probeMachine() async {
    probing = true;
    notifyListeners();
    try {
      machine = await detectMachine();
    } finally {
      probing = false;
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- 体检

  Future<void> refreshCheck() async {
    if (!engineReady) return;
    checking = true;
    notifyListeners();
    try {
      check = await eng.check();
      final r = await eng.run(['bgcpu'], timeout: const Duration(seconds: 30));
      bg = Engine.parseBg(r.text);
      bgFooter = Engine.bgFooter(r.text);
      bgJudge = Engine.bgJudge(r.text);
    } finally {
      checking = false;
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- 游戏

  Future<GameInfo> ensureGame(String key) async {
    if (games.containsKey(key)) return games[key]!;
    auditing = true;
    notifyListeners();
    try {
      final g = await eng.audit(key);
      games[key] = g;
      return g;
    } finally {
      auditing = false;
      notifyListeners();
    }
  }

  Future<void> reloadGame(String key) async {
    games.remove(key);
    await ensureGame(key);
  }

  void selectGame(String key) {
    gameKey = key;
    notifyListeners();
    ensureGame(key);
  }

  GameInfo get currentGame => games[gameKey] ?? GameInfo.empty;

  // ------------------------------------------------------------ 亲和性

  Future<void> refreshPins() async {
    if (!engineReady) return;
    pinReport = await eng.pinstat();
    pins = pinReport.rows;
    notifyListeners();
  }

  /// 这台机器上「压制到能效核」到底能不能用 —— 由本机拓扑 + 引擎实报的掩码现算。
  PinAssessment? get pinAssessment {
    final m = machine;
    if (m == null) return null;
    return assessPin(m, pinReport);
  }

  Future<void> doPin() async {
    busy = true;
    notifyListeners();
    try {
      final r = await eng.pin();
      lastTitle = '后台程序压制';
      lastOutput = r.text;
      await refreshPins();
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> doUnpin() async {
    busy = true;
    notifyListeners();
    try {
      final r = await eng.unpin();
      lastTitle = '放回全部核心';
      lastOutput = r.text;
      await refreshPins();
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  // ------------------------------------------------------------ 自启动

  Future<void> refreshAutoStart() async {
    autoStart = await eng.autoStartInstalled();
    notifyListeners();
  }

  Future<void> toggleAutoStart() async {
    busy = true;
    notifyListeners();
    try {
      final r = autoStart ? await eng.uninstall() : await eng.install();
      lastTitle = autoStart ? '关闭守护' : '开启守护';
      lastOutput = r.text;
      await refreshAutoStart();
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- 档位

  void selectMode(String key) {
    modeKey = key;
    notifyListeners();
  }

  Future<void> applyMode() async {
    busy = true;
    notifyListeners();
    try {
      final r = await eng.apply(modeKey);
      lastTitle = '应用 ${ModeProfile.byKey(modeKey).name}';
      lastOutput = r.text;
      check = await eng.check();
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> restoreAll() async {
    busy = true;
    notifyListeners();
    try {
      final r = await eng.restore();
      lastTitle = '还原初始设置';
      lastOutput = r.text;
      check = await eng.check();
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  Future<void> doGpuPref() async {
    busy = true;
    notifyListeners();
    try {
      final r = await eng.gpupref();
      lastTitle = '登记显卡绑定';
      lastOutput = r.text;
      games.remove(gameKey);
      await ensureGame(gameKey);
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- 跑分

  Future<void> runBench() async {
    busy = true;
    notifyListeners();
    try {
      bench = await eng.bench();
      lastTitle = '性能测试';
      if (bench == null) {
        lastOutput = '测试没有返回结果。';
      } else {
        // 头条数字用中位数（CLI 的判读也是按它给的），衰减跟在后面。
        final b = bench!;
        final tail =
            b.rounds.isEmpty ? '' : '，首→末衰减 ${b.dropPct.toStringAsFixed(1)}%';
        lastOutput = '中位数 ${groupNum(b.median)} 次/秒$tail';
      }
    } finally {
      busy = false;
      notifyListeners();
    }
  }

  // ---------------------------------------------------------------- 日志

  Future<void> refreshLog() async {
    logLines = await eng.readLog();
    notifyListeners();
  }

  /// 「优化好了没」—— 拿体检结论当判据
  bool get optimized => check.badCount == 0 && check.power.isNotEmpty;
}
