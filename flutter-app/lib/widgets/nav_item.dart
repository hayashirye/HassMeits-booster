// 左侧导航项 —— Material You 版。
//
// ★ 为什么不用 Flutter 自带的 NavigationRail：
//   试过了（extended: true + leading + trailing + groupAlignment），
//   结果整个侧栏塌成一片黑，只剩 trailing 里的下拉框可见，同时 stderr 刷
//   47 条 TransformLayer is constructed with an invalid matrix。
//   M3 的观感其实不来自「用了哪个组件」，而来自这几件事：
//     1. 选中态用 secondaryContainer 填一颗全圆角药丸
//     2. 选中时图标从「描边」变「实心」
//     3. 所有元素共用同一条左基准线
//   这三条自己写反而更准，而且对齐完全可控。
//
// ★ 对齐：整条侧栏只用一个常量 kRailPad = 16。
//   标题、游戏切换、导航项、页脚全部从同一个 16px 起，不再出现
//   「标题 18px、导航 26px」那种各组各缩进一档、看着就是没对齐的问题。
import 'package:flutter/material.dart';

import '../theme.dart';

/// 侧栏统一的左右留白。改这一个数，整条侧栏一起动。
const double kRailPad = 16;

class NavItem extends StatefulWidget {
  final IconData icon;
  final IconData selectedIcon;
  final String label;
  final bool selected;
  final VoidCallback onTap;

  const NavItem({
    super.key,
    required this.icon,
    required this.selectedIcon,
    required this.label,
    required this.selected,
    required this.onTap,
  });

  @override
  State<NavItem> createState() => _NavItemState();
}

class _NavItemState extends State<NavItem> {
  bool _hot = false;

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final sel = widget.selected;

    // M3 状态层：悬停 8% 的 onSurface 叠在背景上；选中直接填 secondaryContainer
    final bg = sel
        ? cs.secondaryContainer
        : (_hot ? cs.onSurface.withValues(alpha: 0.08) : Colors.transparent);
    final fgCol = sel ? cs.onSecondaryContainer : cs.onSurfaceVariant;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      child: MouseRegion(
        cursor: SystemMouseCursors.click,
        onEnter: (_) => setState(() => _hot = true),
        onExit: (_) => setState(() => _hot = false),
        child: GestureDetector(
          onTap: widget.onTap,
          child: AnimatedContainer(
            duration: const Duration(milliseconds: T.dNavSelect),
            curve: Curves.easeOut,
            height: 44,
            decoration: BoxDecoration(
              color: bg,
              // 全圆角 = M3 的 Stadium 药丸
              borderRadius: BorderRadius.circular(100),
            ),
            child: Row(
              children: [
                // ★ 药丸内再缩进 8px，加上外面 8px 的 Padding，
                //   图标左边缘正好落在 16px —— 和标题同一条基准线。
                const SizedBox(width: 8),
                Icon(
                  // M3 的小细节：选中时图标描边 → 实心
                  sel ? widget.selectedIcon : widget.icon,
                  size: 20,
                  color: fgCol,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: AnimatedDefaultTextStyle(
                    duration: const Duration(milliseconds: T.dNavSelect),
                    style: TextStyle(
                      color: fgCol,
                      fontSize: T.fsBody,
                      // M3 用 600 表达「选中」，不再叠字色
                      fontWeight: sel ? FontWeight.w600 : FontWeight.w400,
                    ),
                    child: Text(widget.label),
                  ),
                ),
                const SizedBox(width: 12),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
