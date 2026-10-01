// 种子色管理 —— Material You 的「动态取色」在 Windows 上的等价物。
//
// ★ Android 上的 Material You 从壁纸取色（Monet），Windows 上没有这套东西。
//   官方的等价来源是**系统强调色**：设置 → 个性化和颜色 → 强调色。
//   它存在注册表里，用 reg query 就能读，不需要任何第三方包。
//
//   注册表里的 AccentColor 是 **0xAABBGGRR**（ABGR，不是 ARGB）—— 这是最容易
//   踩的坑：直接按 ARGB 解会把红蓝对调，取出来是青的。
import 'dart:io';

import 'package:flutter/material.dart';

import 'theme.dart';

/// 一个可选的种子色
class SeedOption {
  final String key; // 持久化用的标识
  final String label; // 下拉里显示的名字
  final Color? color; // null = 跟随系统
  const SeedOption(this.key, this.label, this.color);
}

/// 预设。第一项是「跟随系统」。
const List<SeedOption> kSeeds = [
  SeedOption('system', '跟随系统强调色', null),
  SeedOption('valorant', '无畏契约红', Color(0xFFF65260)),
  SeedOption('ocean', '海蓝', Color(0xFF4C8DF6)),
  SeedOption('teal', '青碧', Color(0xFF00A99D)),
  SeedOption('violet', '紫罗兰', Color(0xFF8B5CF6)),
  SeedOption('forest', '松绿', Color(0xFF22A06B)),
  SeedOption('amber', '琥珀', Color(0xFFE0A030)),
  SeedOption('slate', '石墨', Color(0xFF7C8698)),
];

/// 首次启动用的种子色 key（见 loadSeedKey 里的理由）。
const String kDefaultSeedKey = 'valorant';

/// 跟随系统时的兜底色（注册表读不到就用它）
const Color kFallbackSeed = Color(0xFFF65260);

// ================================================================
// 读 Windows 系统强调色
//
// 两个位置，按优先级来：
//   1) HKCU\Software\Microsoft\Windows\DWM\AccentColor      —— 当前生效的
//   2) HKCU\Software\...\Explorer\Accent\AccentColorMenu    —— 旧版/兜底
// 都读不到返回 null，调用方用 kFallbackSeed。
// ================================================================
Future<Color?> windowsAccent() async {
  const probes = [
    (r'HKCU\Software\Microsoft\Windows\DWM', 'AccentColor'),
    (r'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent',
        'AccentColorMenu'),
  ];
  for (final (path, name) in probes) {
    try {
      final r = await Process.run('reg', ['query', path, '/v', name]);
      final out = r.stdout.toString();
      // REG_DWORD 那一行形如： AccentColor    REG_DWORD    0x00f62c3c
      final m = RegExp(r'REG_DWORD\s+0x([0-9a-fA-F]{1,8})').firstMatch(out);
      if (m == null) continue;
      final v = int.parse(m.group(1)!, radix: 16);
      // ★ 0xAABBGGRR → 拆出 R/G/B（低字节是 R，不是 B）
      final r8 = v & 0xFF;
      final g8 = (v >> 8) & 0xFF;
      final b8 = (v >> 16) & 0xFF;
      if (r8 == 0 && g8 == 0 && b8 == 0) continue; // 全黑说明没设过
      return Color.fromARGB(255, r8, g8, b8);
    } catch (_) {
      // reg.exe 不在、键不存在、权限不足 —— 都走下一个探测点
    }
  }
  return null;
}

// ================================================================
// 持久化：%LOCALAPPDATA%\valorant_boost_flutter\seed.txt
// 只存一行，内容是 kSeeds 里的 key。刻意不用 shared_preferences ——
// 一个用不上的第三方依赖只要拉不下来就会让 pub get 整个失败。
// ================================================================
File _storeFile() {
  final base = Platform.environment['LOCALAPPDATA'] ??
      Directory.systemTemp.path;
  return File('$base\\valorant_boost_flutter\\seed.txt');
}

Future<String> loadSeedKey() async {
  // ★ 默认用品牌红，不用「跟随系统强调色」。
  //   理由：这软件的辨识度就是那抹红，而 Material You 的「味道」来自
  //   药丸选中态、色调面板、M3 字体这一整套，不来自颜色本身 ——
  //   所以默认红不会让它变得不像 Material You，但能让第一眼对得上品牌。
  //   想跟着系统走，下拉里第二项就是。
  try {
    final f = _storeFile();
    if (!f.existsSync()) return kDefaultSeedKey;
    final k = (await f.readAsString()).trim();
    return kSeeds.any((s) => s.key == k) ? k : kDefaultSeedKey;
  } catch (_) {
    return kDefaultSeedKey;
  }
}

Future<void> saveSeedKey(String key) async {
  try {
    final f = _storeFile();
    await f.parent.create(recursive: true);
    await f.writeAsString(key);
  } catch (_) {
    // 存不下就存不下，不影响本次使用
  }
}

/// 把 key 解析成实际颜色
Future<Color> resolveSeed(String key) async {
  final opt = kSeeds.firstWhere((s) => s.key == key, orElse: () => kSeeds.first);
  if (opt.color != null) return opt.color!;
  return (await windowsAccent()) ?? kFallbackSeed;
}

// ================================================================
// 建 Material 3 主题
//
// ★ Material You 的观感有三个来源，都在这里定：
//   1. ColorScheme.fromSeed —— 从一个种子色推导出完整的色调板
//   2. useMaterial3: true  —— 圆角、药丸、色调高程全按 M3 来
//   3. 组件主题微调 —— 把 M3 默认值往「桌面端」调（默认是给手机定的：
//      控件偏大、间距偏松、字偏粗）
// ================================================================
ThemeData buildM3Theme(Color seed, Brightness brightness) {
  // ★ dynamicSchemeVariant: fidelity
  //   默认的 M3 配色算法（tonalSpot）会大幅降饱和 —— 拿 #F65260 这种
  //   鲜红做种子，推出来的 primary 是灰扑扑的砖红、按钮变成浅粉，
  //   品牌辨识度就没了。fidelity 变体把种子色本身的色度尽量保住，
  //   于是「还是那个红」，但明度、容器色、状态层依然全套按 M3 走。
  final cs = ColorScheme.fromSeed(
    seedColor: seed,
    brightness: brightness,
    dynamicSchemeVariant: DynamicSchemeVariant.fidelity,
  );
  final dark = brightness == Brightness.dark;

  return ThemeData(
    useMaterial3: true,
    colorScheme: cs,
    brightness: brightness,
    scaffoldBackgroundColor: cs.surface,
    // 字体：微软雅黑。Windows 上一定有，缺了会退回系统默认。
    fontFamily: 'Microsoft YaHei',

    // ---- 卡片：M3 的招牌是「一整块纯色 + 大圆角」，不是渐变和阴影 ----
    cardTheme: CardThemeData(
      color: cs.surfaceContainer,
      surfaceTintColor: cs.surfaceTint,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
    ),

    // ---- 按钮：M3 的 FilledButton 是全圆角药丸 ----
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 16),
        textStyle: const TextStyle(
          fontSize: T.fsBody,
          fontWeight: FontWeight.w600,
        ),
        shape: const StadiumBorder(),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
        side: BorderSide(color: cs.outlineVariant),
        textStyle: const TextStyle(
          fontSize: T.fsBody,
          fontWeight: FontWeight.w600,
        ),
        shape: const StadiumBorder(),
      ),
    ),
    textButtonTheme: TextButtonThemeData(
      style: TextButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
        textStyle: const TextStyle(fontSize: T.fsSmall),
      ),
    ),
    // 桌面端用不着 48px 的最小点击区，压到 36
    iconButtonTheme: IconButtonThemeData(
      style: IconButton.styleFrom(minimumSize: const Size(36, 36)),
    ),

    // ---- 导航栏：选中项是一颗药丸（M3 NavigationRail 的 indicator）----
    navigationRailTheme: NavigationRailThemeData(
      backgroundColor: cs.surfaceContainerLow,
      indicatorColor: cs.secondaryContainer,
      indicatorShape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(20),
      ),
      labelType: NavigationRailLabelType.none,
      useIndicator: true,
    ),

    // ---- 分段按钮：游戏切换用（M3 里就是给「二选一」用的）----
    segmentedButtonTheme: SegmentedButtonThemeData(
      style: SegmentedButton.styleFrom(
        selectedBackgroundColor: cs.secondaryContainer,
        selectedForegroundColor: cs.onSecondaryContainer,
        backgroundColor: Colors.transparent,
        foregroundColor: cs.onSurfaceVariant,
        side: BorderSide(color: cs.outlineVariant),
        textStyle: const TextStyle(fontSize: T.fsSmall),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      ),
    ),

    // ---- 药丸标签 ----
    chipTheme: ChipThemeData(
      backgroundColor: cs.surfaceContainerHighest,
      side: BorderSide(color: cs.outlineVariant),
      labelStyle: TextStyle(fontSize: T.fsTiny, color: cs.onSurfaceVariant),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      shape: const StadiumBorder(),
    ),

    dividerTheme: DividerThemeData(
      color: cs.outlineVariant,
      thickness: 1,
      space: 1,
    ),
    progressIndicatorTheme: ProgressIndicatorThemeData(color: cs.primary),

    tooltipTheme: TooltipThemeData(
      decoration: BoxDecoration(
        color: cs.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(6),
        border: Border.all(color: cs.outlineVariant),
      ),
      textStyle: TextStyle(color: cs.onSurface, fontSize: T.fsTiny),
    ),

    scrollbarTheme: ScrollbarThemeData(
      thickness: WidgetStateProperty.all(6),
      radius: const Radius.circular(3),
      thumbColor: WidgetStateProperty.all(
        cs.onSurfaceVariant.withValues(alpha: dark ? 0.35 : 0.45),
      ),
    ),

    snackBarTheme: SnackBarThemeData(
      backgroundColor: cs.surfaceContainerHighest,
      contentTextStyle: TextStyle(color: cs.onSurface, fontSize: T.fsSmall),
      behavior: SnackBarBehavior.floating,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
    ),
  );
}
