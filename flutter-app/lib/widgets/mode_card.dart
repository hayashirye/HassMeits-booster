// 档位选择卡（主页那四张）—— Material You 版。
//
// ★ M3 里「一张卡被选中」长什么样，是有明确答案的：底色换成
//   secondaryContainer、文字换成 onSecondaryContainer。整套 M3 的选中态
//   （筛选芯片、分段按钮、导航药丸）用的都是这一对颜色，所以一眼就能认出来。
//   之前用的是「品牌红压暗 + 红描边」，那是自定义风格，不是 M3。
//
// ★ 勾选标记仍然走 Curves.easeOutBack —— 会轻微过冲再收回来。这一点和 M3 不冲突：
//   M3 自己的「强调缓动」（emphasized easing）也是带一点过冲的。
//   取消选中走 140ms 的普通收缩，不用回弹（收回去的时候过冲会显得很怪）。
import 'package:flutter/material.dart';

import '../models.dart';
import '../theme.dart';

class ModeCardView extends StatefulWidget {
  final ModeProfile mode;
  final bool selected;
  final VoidCallback onTap;

  const ModeCardView({
    super.key,
    required this.mode,
    required this.selected,
    required this.onTap,
  });

  @override
  State<ModeCardView> createState() => _ModeCardViewState();
}

class _ModeCardViewState extends State<ModeCardView> {
  bool _hot = false;

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final sel = widget.selected;

    // M3 的三档表面：未选 → 未选悬停 → 选中
    final bg = sel
        ? cs.secondaryContainer
        : (_hot ? cs.surfaceContainerHighest : cs.surfaceContainer);
    final titleColor = sel ? cs.onSecondaryContainer : cs.onSurface;
    final descColor = sel
        ? cs.onSecondaryContainer.withValues(alpha: 0.78)
        : cs.onSurfaceVariant;

    return MouseRegion(
      cursor: SystemMouseCursors.click,
      onEnter: (_) => setState(() => _hot = true),
      onExit: (_) => setState(() => _hot = false),
      child: GestureDetector(
        onTap: widget.onTap,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: T.dModeSelect),
          curve: Curves.easeOut,
          padding: const EdgeInsets.fromLTRB(15, 15, 15, 15),
          decoration: BoxDecoration(
            color: bg,
            borderRadius: BorderRadius.circular(14),
            // M3 的选中态不靠描边，靠底色。这里只在选中时留一道同色系的
            // 细边，让边缘在浅色主题下不至于糊掉。
            border: Border.all(
              color: sel
                  ? cs.onSecondaryContainer.withValues(alpha: 0.22)
                  : Colors.transparent,
              width: 1,
            ),
          ),
          child: Stack(
            children: [
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    widget.mode.name,
                    style: TextStyle(
                      color: titleColor,
                      fontSize: T.fsBody + 1,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 7),
                  Text(
                    widget.mode.desc,
                    style: TextStyle(
                      color: descColor,
                      fontSize: T.fsTiny + 0.5,
                      height: 1.55,
                    ),
                  ),
                ],
              ),
              if (sel)
                Positioned(
                  right: 0,
                  top: 0,
                  child: TweenAnimationBuilder<double>(
                    tween: Tween(begin: 0, end: 1),
                    duration: const Duration(milliseconds: T.dCheckPop),
                    curve: Curves.easeOutBack,
                    builder: (_, s, __) => Transform.scale(
                      scale: s.clamp(0.0, 1.4),
                      child: Opacity(
                        opacity: s.clamp(0.0, 1.0),
                        child: Container(
                          width: 18,
                          height: 18,
                          decoration: BoxDecoration(
                            color: cs.primary,
                            shape: BoxShape.circle,
                          ),
                          child: Icon(
                            Icons.check,
                            size: 13,
                            color: cs.onPrimary,
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
