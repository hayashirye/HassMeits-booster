// 卡片 —— Material You 版。
//
// ★ 这里跟 C# 版的设计判断是相反的，值得记一笔：
//   C# 版用「表面更亮 + 顶边高光 + 上亮下暗渐变」来表达「浮起来」，
//   因为深色背景上画阴影等于没画。
//   Material You 用的是**色调高程**（tonal elevation）：同一个种子色按不同
//   tone 铺出一层层表面（surface → surfaceContainer → surfaceContainerHigh），
//   靠「层与层之间的亮度差」表达深度。所以 M3 的卡片是**一整块纯色、没有
//   渐变、没有高光、没有阴影**。
//   两者都对，但混着用就会两边都不像 —— 所以这里把渐变和高光去掉了。
import 'package:flutter/material.dart';

import '../theme.dart';

class AppCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;
  final Color? top; // 兼容旧接口：给了就用它当纯色底（不再是渐变的上半段）
  final Color? bottom; // 兼容旧接口：仅在没给 top 时用来兜底
  final Color? border;

  const AppCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(18),
    this.radius = T.cardRadius,
    this.top,
    this.bottom,
    this.border,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        // ★ 纯色，不是渐变。层级靠 surfaceContainer* 之间的亮度差。
        color: top ?? bottom ?? T.cardBot,
        borderRadius: BorderRadius.circular(radius),
        // M3 的卡片默认没有描边 —— 靠色调差就够了。
        // 只有调用方明确给了颜色（判定卡片那种）才描边。
        border: border == null ? null : Border.all(color: border!, width: 1),
      ),
      child: Padding(padding: padding, child: child),
    );
  }
}

/// 小标题（卡片里的第一行，弱化色 + 字重 600）
class CardTitle extends StatelessWidget {
  final String text;
  final Widget? trailing;
  const CardTitle(this.text, {super.key, this.trailing});

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Text(
          text,
          style: TextStyle(
            color: T.fg,
            fontSize: T.fsBody,
            fontWeight: FontWeight.w600,
          ),
        ),
        if (trailing != null) ...[const Spacer(), trailing!],
      ],
    );
  }
}

/// 说明文字（卡片里的第二行，弱化色）
class CardHint extends StatelessWidget {
  final String text;
  final Color? color;
  const CardHint(this.text, {super.key, this.color});

  @override
  Widget build(BuildContext context) {
    return Text(
      text,
      style: TextStyle(color: color ?? T.fgDim, fontSize: T.fsSmall, height: 1.5),
    );
  }
}

/// 胶囊徽章 —— 判定列用的那个。
///
/// M3 里对应的是 assist/tonal chip：低透明度的色调底 + 同色文字，
/// 全圆角（Stadium）。判定色不跟种子走，所以这里仍然直接用传入的颜色。
class Pill extends StatelessWidget {
  final String text;
  final Color color;
  final bool filled;
  const Pill(this.text, this.color, {super.key, this.filled = false});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: filled ? 1.0 : 0.16),
        borderRadius: BorderRadius.circular(100), // 全圆角 = Stadium
        border: Border.all(color: color.withValues(alpha: 0.5), width: 1),
      ),
      child: Text(
        text,
        style: TextStyle(
          color: filled ? Colors.white : color,
          fontSize: T.fsTiny,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
