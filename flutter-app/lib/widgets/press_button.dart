// 按钮 —— Material You 版。
//
// ★ 之前这里是自绘的「渐变 + 描边 + 按下下沉 1px」。Material You 的做法反过来：
//   按钮的层级不用渐变和阴影表达，而是用**语义色**——
//     · 主按钮   → FilledButton，填 primary
//     · 次按钮   → FilledButton.tonal，填 secondaryContainer
//   这两档一亮一暗，主次关系一眼就分得出来，而且换种子色时自动跟着变。
//
// ★ 保留了原来的对外接口（label / primary / onTap / icon / padding），
//   所以五个页面里一行都不用改。
import 'package:flutter/material.dart';

import '../theme.dart';

class PressButton extends StatelessWidget {
  final String label;
  final bool primary;
  final VoidCallback? onTap;
  final IconData? icon;
  final EdgeInsetsGeometry padding;

  const PressButton({
    super.key,
    required this.label,
    this.primary = false,
    this.onTap,
    this.icon,
    this.padding = const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
  });

  @override
  Widget build(BuildContext context) {
    final child = Row(
      mainAxisSize: MainAxisSize.min,
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        if (icon != null) ...[
          Icon(icon, size: 16),
          const SizedBox(width: 8),
        ],
        Text(label),
      ],
    );

    // ★ minimumSize 归零 + tapTargetSize 收缩：默认值是给手指定的 48px，
    //   在桌面上会让按钮高得离谱、把卡片撑开。桌面端要按鼠标来定尺寸。
    final style = ButtonStyle(
      padding: WidgetStatePropertyAll(padding),
      minimumSize: const WidgetStatePropertyAll(Size(0, 0)),
      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
      visualDensity: VisualDensity.compact,
      textStyle: const WidgetStatePropertyAll(
        TextStyle(fontSize: T.fsBody, fontWeight: FontWeight.w600),
      ),
    );

    if (primary) {
      return FilledButton(onPressed: onTap, style: style, child: child);
    }
    return FilledButton.tonal(onPressed: onTap, style: style, child: child);
  }
}
