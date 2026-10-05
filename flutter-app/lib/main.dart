// 入口。外壳 = 左侧导航（自写的 Material You 侧栏）+ 右侧内容，
// 内容切页时从侧边滑进来（26px / 200ms）。
//
// ★ 侧栏为什么不用 Flutter 的 NavigationRail：
//   试过 extended: true + leading + trailing + groupAlignment 那套，
//   结果整个侧栏塌成一片黑，只剩 trailing 里的下拉框可见，stderr 还刷了
//   47 条 TransformLayer is constructed with an invalid matrix。
//   现在改成自己拼：整条侧栏只有一个左基准线 kRailPad = 16，
//   标题、游戏切换、导航项、页脚全部从这同一个 16px 起 —— 这正好也解决了
//   用户提的「左边的栏没对齐」（原来是标题 18px、导航文字 26px，差 8px）。
import 'package:flutter/material.dart';

import 'app_state.dart';
import 'models.dart';
import 'seed.dart';
import 'theme.dart';
import 'widgets/nav_item.dart';
import 'pages/home_page.dart';
import 'pages/check_page.dart';
import 'pages/bench_page.dart';
import 'pages/game_page.dart';
import 'pages/log_page.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  final st = AppState();
  // 种子色 + 主题模式要在第一帧之前就定下来，否则会先闪一下默认色
  final seedKey = await loadSeedKey();
  final seedColor = await resolveSeed(seedKey);

  runApp(ValorantBoostApp(st, seedKey: seedKey, seedColor: seedColor));
  // 窗口先出来再干活 —— 引擎探测要几百毫秒，别让用户对着白屏等
  st.init();
}

// 窗口标题 / 最小尺寸：Flutter 自带的 runner（windows/runner/main.cpp）里
// 已经把标题设成了 "valorant_boost"。想改成「游戏 CPU 高频优化器」并设最小尺寸，
// 两个办法：
//   1) 直接改 windows/runner/main.cpp 里的 window.Create(L"...", origin, size)
//   2) 加 window_manager 包（见 README）
// 这里刻意不引第三方包 —— 一个用不上的依赖只要拉不下来就会让 pub get 整个失败。

class ValorantBoostApp extends StatefulWidget {
  final AppState st;
  final String seedKey;
  final Color seedColor;
  const ValorantBoostApp(
    this.st, {
    super.key,
    required this.seedKey,
    required this.seedColor,
  });

  @override
  State<ValorantBoostApp> createState() => _ValorantBoostAppState();
}

class _ValorantBoostAppState extends State<ValorantBoostApp> {
  late String _seedKey = widget.seedKey;
  late Color _seedColor = widget.seedColor;
  Brightness _brightness = Brightness.dark;

  Future<void> _pickSeed(String key) async {
    if (key == _seedKey) return;
    final c = await resolveSeed(key);
    await saveSeedKey(key);
    if (!mounted) return;
    setState(() {
      _seedKey = key;
      _seedColor = c;
    });
  }

  void _toggleBrightness() => setState(() {
        _brightness = _brightness == Brightness.dark
            ? Brightness.light
            : Brightness.dark;
      });

  @override
  Widget build(BuildContext context) {
    final theme = buildM3Theme(_seedColor, _brightness);

    // ★ 关键一步：把 ColorScheme 铺进 T 的那组静态量。
    //   必须在任何子节点 build 之前跑 —— 根节点的 build 天生就是最先的，
    //   所以放在这里最稳。铺完之后页面里那些 T.fg / T.accent 就跟着种子走了，
    //   一个字都不用改。
    T.apply(theme.colorScheme);

    return MaterialApp(
      title: '游戏 CPU 高频优化器',
      debugShowCheckedModeBanner: false,
      theme: theme,
      home: Shell(
        widget.st,
        seedKey: _seedKey,
        onSeed: _pickSeed,
        brightness: _brightness,
        onToggleBrightness: _toggleBrightness,
      ),
    );
  }
}

class Shell extends StatefulWidget {
  final AppState st;
  final String seedKey;
  final ValueChanged<String> onSeed;
  final Brightness brightness;
  final VoidCallback onToggleBrightness;

  const Shell(
    this.st, {
    super.key,
    required this.seedKey,
    required this.onSeed,
    required this.brightness,
    required this.onToggleBrightness,
  });

  @override
  State<Shell> createState() => _ShellState();
}

class _ShellState extends State<Shell> with SingleTickerProviderStateMixin {
  int _page = 0;
  late final AnimationController _slide;

  /// 侧栏宽度。改这一个数，内容区跟着变。
  static const double kRailWidth = 236;

  // ★ Material 3 的导航惯例：选中时图标从「描边」变「实心」。
  //   这是 M3 一个很小但很好认的细节，比只换个颜色更像原生的。
  static const _navItems = [
    (Icons.home_outlined, Icons.home, '主页'),
    (Icons.health_and_safety_outlined, Icons.health_and_safety, '体检'),
    (Icons.speed_outlined, Icons.speed, '性能测试'),
    (Icons.sports_esports_outlined, Icons.sports_esports, '游戏专项'),
    (Icons.article_outlined, Icons.article, '日志'),
  ];

  @override
  void initState() {
    super.initState();
    _slide = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: T.dPageSlide),
      value: 1,
    );
  }

  @override
  void dispose() {
    _slide.dispose();
    super.dispose();
  }

  void _go(int i) {
    if (i == _page) return;
    setState(() => _page = i);
    _slide.forward(from: 0);
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Scaffold(
      backgroundColor: T.bg,
      body: Row(
        // ★ 必须 stretch：默认的 center 只给侧栏「0..高」的松约束，
        //   里面那些 Expanded 就没有确定高度可分了。
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _sidebar(cs),
          Expanded(
            child: ClipRect(
              child: AnimatedBuilder(
                animation: _slide,
                builder: (_, child) => Transform.translate(
                  // 位移只有 26px，再大会变成「等它飘过来」
                  offset: Offset(
                    T.pageSlidePx * (1 - Curves.easeOut.transform(_slide.value)),
                    0,
                  ),
                  child: child,
                ),
                child: _pages(),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _pages() {
    // IndexedStack 而不是每次重建 —— 页面里的滚动位置、输入内容都留着
    return IndexedStack(
      index: _page,
      children: [
        HomePage(widget.st),
        CheckPage(widget.st),
        BenchPage(widget.st),
        GamePage(widget.st),
        LogPage(widget.st),
      ],
    );
  }

  // ================================================================
  // 侧栏
  //
  // ★ 三组内容，每组自己套一个 horizontal: kRailPad 的 Padding，
  //   除此之外不加任何别缩进 —— 这就是「对齐」的全部秘密。
  //   导航项是唯一例外：它的药丸要往外多探 8px（M3 的选中指示器本来就比
  //   文字宽），所以 NavItem 内部用 horizontal: 8，药丸里再垫 8，
  //   图标左边缘照样落在 16px 上，和标题对齐。
  // ================================================================
  Widget _sidebar(ColorScheme cs) {
    return Container(
      width: kRailWidth,
      decoration: BoxDecoration(
        color: T.sideBg,
        border: Border(right: BorderSide(color: T.line)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _railHeader(cs),
          const SizedBox(height: 6),
          Expanded(
            child: ListView(
              padding: EdgeInsets.zero,
              children: [
                for (var i = 0; i < _navItems.length; i++)
                  NavItem(
                    icon: _navItems[i].$1,
                    selectedIcon: _navItems[i].$2,
                    label: _navItems[i].$3,
                    selected: _page == i,
                    onTap: () => _go(i),
                  ),
              ],
            ),
          ),
          _railFooter(cs),
        ],
      ),
    );
  }

  Widget _railHeader(ColorScheme cs) {
    return ListenableBuilder(
      listenable: widget.st,
      builder: (_, __) => Padding(
        padding: const EdgeInsets.fromLTRB(kRailPad, 18, kRailPad, 6),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              widget.st.currentGame.name.isEmpty
                  ? '游戏 CPU 高频优化器'
                  : widget.st.currentGame.name,
              style: TextStyle(
                color: T.fg,
                fontSize: T.fsTitle,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              widget.st.currentGame.key.isEmpty
                  ? 'CPU 高频优化器'
                  : '${gameShortName(widget.st.currentGame.key)} · CPU 高频优化器',
              style: TextStyle(color: T.fgFaint, fontSize: T.fsTiny),
            ),
            const SizedBox(height: 14),

            // ---- 游戏切换：M3 里「多选一」就该用 SegmentedButton ----
            // 加游戏时：这里加一条 ButtonSegment，名字表在 models.dart。
            SegmentedButton<String>(
              segments: const [
                ButtonSegment(value: 'valorant', label: Text('无畏契约')),
                ButtonSegment(value: 'apex', label: Text('Apex')),
                ButtonSegment(value: 'wuwa', label: Text('鸣潮')),
              ],
              selected: {widget.st.gameKey},
              showSelectedIcon: false,
              style: SegmentedButton.styleFrom(
                visualDensity: VisualDensity.compact,
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 8),
                textStyle: const TextStyle(fontSize: T.fsSmall),
              ),
              onSelectionChanged: (s) {
                widget.st.selectGame(s.first);
                _go(3); // 顺手跳到游戏专项页
              },
            ),
          ],
        ),
      ),
    );
  }

  Widget _railFooter(ColorScheme cs) {
    return ListenableBuilder(
      listenable: widget.st,
      builder: (_, __) => Padding(
        padding: const EdgeInsets.fromLTRB(kRailPad, 6, kRailPad, 14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Divider(height: 20, color: T.line),
            const SizedBox(height: 4),

            // ---- 配色种子：Material You 的动态取色入口 ----
            Text(
              '配色种子',
              style: TextStyle(color: T.fgFaint, fontSize: T.fsTiny),
            ),
            const SizedBox(height: 6),

            // ★ 这里刻意不用 DropdownMenu。
            //   DropdownMenu 底下是 MenuAnchor + 一个内嵌 TextField，
            //   在侧栏这种窄而高的容器里会算出爆掉的尺寸（我怀疑上一版
            //   整条侧栏塌成一片黑，NavigationRail 和它各有一半责任）。
            //   DropdownButton 尺寸完全可预测，够用。
            Container(
              height: 40,
              padding: const EdgeInsets.symmetric(horizontal: 12),
              decoration: BoxDecoration(
                color: cs.surfaceContainerHigh,
                borderRadius: BorderRadius.circular(10),
                border: Border.all(color: T.line),
              ),
              child: DropdownButtonHideUnderline(
                child: DropdownButton<String>(
                  isExpanded: true,
                  value: widget.seedKey,
                  icon: Icon(Icons.expand_more, size: 18, color: T.fgDim),
                  borderRadius: BorderRadius.circular(12),
                  dropdownColor: T.cardTop,
                  style: TextStyle(color: T.fg, fontSize: T.fsSmall),
                  items: [
                    for (final s in kSeeds)
                      DropdownMenuItem<String>(
                        value: s.key,
                        child: Row(
                          children: [
                            _seedDot(s),
                            const SizedBox(width: 8),
                            Flexible(
                              child: Text(
                                s.label,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                  color: T.fg,
                                  fontSize: T.fsSmall,
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                  ],
                  onChanged: (k) {
                    if (k != null) widget.onSeed(k);
                  },
                ),
              ),
            ),
            const SizedBox(height: 8),

            // ---- 深色 / 浅色 ----
            TextButton.icon(
              onPressed: widget.onToggleBrightness,
              icon: Icon(
                widget.brightness == Brightness.dark
                    ? Icons.light_mode_outlined
                    : Icons.dark_mode_outlined,
                size: 18,
              ),
              label: Text(
                widget.brightness == Brightness.dark ? '切到浅色' : '切到深色',
              ),
            ),
            const SizedBox(height: 8),

            // ---- 引擎状态 ----
            Row(
              children: [
                Container(
                  width: 6,
                  height: 6,
                  decoration: BoxDecoration(
                    color: widget.st.engineReady ? T.ok : T.bad,
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 6),
                Text(
                  widget.st.engineReady ? '引擎已就绪' : '没找到引擎',
                  style: TextStyle(
                    color: widget.st.engineReady ? T.ok : T.bad,
                    fontSize: T.fsTiny,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 5),
            Text(
              widget.st.engineReady
                  ? widget.st.enginePath
                  : '需要 ValorantBoost.exe',
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                color: T.fgFaint,
                fontSize: 10,
                height: 1.4,
                fontFamily: kMonoFallback.first,
              ),
            ),
          ],
        ),
      ),
    );
  }

  /// 下拉里那一个小圆点，直接用该选项的颜色
  Widget _seedDot(SeedOption s) {
    final c = s.color ?? T.accent;
    return Container(
      width: 14,
      height: 14,
      decoration: BoxDecoration(
        color: c,
        shape: BoxShape.circle,
        border: Border.all(color: T.line),
      ),
    );
  }
}
