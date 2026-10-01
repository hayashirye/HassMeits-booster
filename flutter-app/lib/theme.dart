// 设计系统 —— Material You（Material 3）版。
//
// ★ 这一版跟 C# 版的设计判断反过来了：
//   C# 版是「手搓一套深色 + 上亮下暗渐变 + 顶部高光」；
//   Material You 的深度不靠渐变，靠的是**色调高程**（tonal elevation）——
//   同一个种子色按不同 tone 铺出一层层表面（surface → surfaceContainerLow →
//   surfaceContainer → surfaceContainerHigh），越靠前的层越亮，层级就出来了。
//   所以卡片是「一整块纯色」，不是渐变。
//
// ★ 颜色为什么是可变静态量而不是 const：
//   种子色是运行时可换的（系统强调色 / 品牌红 / 预设）。如果 T.fg 是 const，
//   换种子就得把每个页面里的 T.fg 改成 Theme.of(context).colorScheme.onSurface
//   —— 那是几百处改动。改成可变静态量 + T.apply(scheme)，页面一个字都不用动。
//   尺寸和时长仍然是 const（它们跟种子无关）。
import 'package:flutter/material.dart';

class T {
  T._();

  // ---------------------------------------------------------------- 表面
  // 随种子变。语义见文件头。
  static Color bg = const Color(0xFF0D0F13);
  static Color cardTop = const Color(0xFF1E222B);
  static Color cardBot = const Color(0xFF14171D);
  static Color sideBg = const Color(0xFF111318);
  static Color line = const Color(0xFF232833);

  // ---------------------------------------------------------------- 文字
  static Color fg = const Color(0xFFE8EBF0);
  static Color fgDim = const Color(0xFF98A0AE);
  static Color fgFaint = const Color(0xFF6B7484);

  // ---------------------------------------------------------------- 强调
  static Color accent = const Color(0xFFF65260);
  static Color accentDark = const Color(0xFFD42C3C);

  // ---------------------------------------------------------------- 判定色
  //
  // ★ M3 的 ColorScheme 里**没有** success / warning 这两个语义角色
  //   （只有 error）。所以这三个是半固定的：不跟着种子走，但亮度会按
  //   深色/浅色主题微调，免得在浅色背景上糊成一片。
  static Color ok = const Color(0xFF3FBF7F);
  static Color bad = const Color(0xFFE5484D);
  static Color warn = const Color(0xFFD9A441);
  static Color mute = const Color(0xFF6B7484);

  // ---------------------------------------------------------------- 圆角
  static const double cardRadius = 12;
  static const double btnRadius = 9;

  // ---------------------------------------------------------------- 动画时长（毫秒）
  //
  // 按钮的时长刻意压得很短。界面上的反馈一旦超过 150ms 就会显得迟钝 ——
  // 那不是「顺滑」，是「拖」。
  static const int dPress = 70;
  static const int dRelease = 90;
  static const int dBtnHover = 110;
  static const int dNavHover = 120;
  static const int dNavSelect = 180;
  static const int dModeHover = 130;
  static const int dModeSelect = 200;
  static const int dCheckPop = 300; // 勾选回弹
  static const int dCheckOff = 140;
  static const int dRowHover = 120;
  static const int dTableGrow = 460; // 表格逐行生长
  static const int dPageSlide = 200; // 切页滑动
  static const double pageSlidePx = 26; // 位移只有 26px，再大会变成「等它飘过来」

  // ---------------------------------------------------------------- 字号
  static const double fsBody = 13.5;
  static const double fsSmall = 12;
  static const double fsTiny = 11;
  static const double fsTitle = 17;
  static const double fsBig = 25;

  /// 按钮的三套渐变（常态 / 悬停 / 按下）。跟 accent 一起由 apply() 填。
  static List<Color> btnIdle = const [Color(0xFFF65260), Color(0xFFD42C3C)];
  static List<Color> btnHot = const [Color(0xFFFF606E), Color(0xFFE23444)];
  static List<Color> btnDown = const [Color(0xFFC42C36), Color(0xFFA6202A)];

  /// 当前是不是浅色主题（判定色要跟着调）
  static bool light = false;

  // ================================================================
  // 把一套 ColorScheme 铺进上面这些变量。
  //
  // ★ 调用时机：main() 里建主题时一次，以及每次换种子色时一次。
  //   调用完必须让根节点重建（setState / setState 在 App 上），否则
  //   已经建好的 widget 树还拿着旧颜色。
  // ================================================================
  static void apply(ColorScheme cs) {
    light = cs.brightness == Brightness.light;

    bg = cs.surface;
    sideBg = cs.surfaceContainerLow;
    cardBot = cs.surfaceContainer;
    cardTop = cs.surfaceContainerHigh;
    line = cs.outlineVariant;

    fg = cs.onSurface;
    fgDim = cs.onSurfaceVariant;
    // M3 的 outline 在深色下偏暗，正好当「最弱的说明文字」
    fgFaint = cs.outline;

    accent = cs.primary;
    // 渐变的下半段：主色压暗。直接压黑会脏，压 30% 刚好。
    accentDark = Color.lerp(cs.primary, Colors.black, light ? 0.18 : 0.32)!;

    // 判定色不跟种子走，但浅色背景下要压深一档才看得清。
    ok = light ? const Color(0xFF1B7F4D) : const Color(0xFF3FBF7F);
    bad = cs.error;
    warn = light ? const Color(0xFF9A6B00) : const Color(0xFFD9A441);
    mute = cs.outline;

    // 按钮渐变：以主色为基准插出来
    btnIdle = [accent, accentDark];
    btnHot = [
      Color.lerp(accent, Colors.white, 0.10)!,
      Color.lerp(accentDark, Colors.white, 0.06)!,
    ];
    btnDown = [
      Color.lerp(accent, Colors.black, 0.22)!,
      Color.lerp(accentDark, Colors.black, 0.22)!,
    ];
  }

  /// 判定 → 颜色
  static Color verdictColor(String v) {
    if (v == '符合') return ok;
    if (v == '不符合') return bad;
    if (v == '注意') return warn;
    return mute;
  }

  /// 占单核百分比 → 颜色（>=40 红，>=10 琥珀，其余灰）
  static Color pctColor(double pct) {
    if (pct >= 40) return bad;
    if (pct >= 10) return warn;
    return mute;
  }

  /// 卡片底色：上亮下暗的竖直渐变
  static LinearGradient cardGradient({Color? a, Color? b}) => LinearGradient(
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
        colors: [a ?? cardTop, b ?? cardBot],
      );

  /// 三套渐变之间插值 —— 悬停色是「插出来」的，不是「啪」一下跳过去的
  static List<Color> mixPair(List<Color> a, List<Color> b, double t) =>
      [Color.lerp(a[0], b[0], t)!, Color.lerp(a[1], b[1], t)!];

  /// 全局 InputDecoration（日志页那个文本框用）
  static InputDecoration inputDeco({String? hint}) => InputDecoration(
        hintText: hint,
        hintStyle: TextStyle(color: fgFaint, fontSize: fsSmall),
        filled: true,
        fillColor: bg,
        contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: BorderSide(color: line),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(8),
          borderSide: BorderSide(color: line),
        ),
      );
}

/// 等宽一点的数字排布用（表格里的百分比、GUID 等）
const List<String> kMonoFallback = ['Consolas', 'Cascadia Mono', 'monospace'];
