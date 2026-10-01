// 主页 —— 优化状态 + 四个档位 + 一键优化 / 还原 + 结果卡。
import 'package:flutter/material.dart';

import '../app_state.dart';
import '../models.dart';
import '../theme.dart';
import '../widgets/app_card.dart';
import '../widgets/mode_card.dart';
import '../widgets/press_button.dart';

class HomePage extends StatelessWidget {
  final AppState st;
  const HomePage(this.st, {super.key});

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: st,
      builder: (_, __) => SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(18, 16, 18, 18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _status(),
            const SizedBox(height: 14),
            _modes(),
            const SizedBox(height: 14),
            _actions(context),
            const SizedBox(height: 14),
            _result(),
          ],
        ),
      ),
    );
  }

  // ------------------------------------------------------------ 优化状态

  Widget _status() {
    final ok = st.optimized;
    final scheme = st.check.schemeName.isEmpty ? '—' : st.check.schemeName;
    return AppCard(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Text(
                ok ? '优化状态 · 已生效' : '优化状态 · 检测到设置不符合游戏目标',
                style: TextStyle(
                  color: ok ? T.ok : T.warn,
                  fontSize: T.fsTiny,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const Spacer(),
              if (st.checking)
                SizedBox(
                  width: 13,
                  height: 13,
                  child: CircularProgressIndicator(
                      strokeWidth: 1.6, color: T.accent),
                ),
            ],
          ),
          const SizedBox(height: 8),
          // 状态大字：已优化绿、未优化琥珀
          AnimatedDefaultTextStyle(
            duration: const Duration(milliseconds: T.dModeSelect),
            style: TextStyle(
              color: ok ? T.ok : T.warn,
              fontSize: T.fsBig,
              fontWeight: FontWeight.w700,
              height: 1.15,
            ),
            child: Text(ok ? '已优化' : '未优化'),
          ),
          const SizedBox(height: 8),
          Text(
            '当前电源方案：$scheme　|　当前选中：${ModeProfile.byKey(st.modeKey).name} 模式',
            style: TextStyle(color: T.fgDim, fontSize: T.fsSmall),
          ),
          const SizedBox(height: 3),
          Text(
            st.check.schemeGuid.isEmpty ? '—' : st.check.schemeGuid,
            style: TextStyle(
              color: T.fgFaint,
              fontSize: T.fsTiny,
              fontFamily: 'Consolas',
            ),
          ),
        ],
      ),
    );
  }

  // -------------------------------------------------------------- 档位卡

  Widget _modes() {
    return AppCard(
      padding: const EdgeInsets.all(14),
      // ★ 必须套 IntrinsicHeight。Row 的 crossAxisAlignment.stretch 是「纵向拉满」，
      //   而这里外面是 SingleChildScrollView，纵向约束是 infinity —— 直接 stretch
      //   会把四张卡撑成无限高：卡片消失、勾选标记飘到左上角、后面的按钮全被推到
      //   y=infinity，同时每帧生成一个非有限的变换矩阵（TransformLayer 报错刷屏）。
      //   IntrinsicHeight 先量出内容高度，stretch 才有确定的高度可用。
      child: IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            for (var i = 0; i < ModeProfile.all.length; i++) ...[
              if (i > 0) const SizedBox(width: 10),
              Expanded(
                child: ModeCardView(
                  mode: ModeProfile.all[i],
                  selected: st.modeKey == ModeProfile.all[i].key,
                  onTap: () => st.selectMode(ModeProfile.all[i].key),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  // ---------------------------------------------------------------- 按钮

  Widget _actions(BuildContext context) {
    return Row(
      children: [
        Expanded(
          flex: 2,
          child: PressButton(
            label: st.busy ? '正在处理…' : '一键优化',
            primary: true,
            onTap: st.busy ? null : st.applyMode,
            padding: const EdgeInsets.symmetric(vertical: 14),
          ),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: PressButton(
            label: '还原初始设置',
            onTap: st.busy ? null : st.restoreAll,
            padding: const EdgeInsets.symmetric(vertical: 14),
          ),
        ),
      ],
    );
  }

  // -------------------------------------------------------------- 结果卡

  Widget _result() {
    return AppCard(
      padding: const EdgeInsets.fromLTRB(18, 14, 18, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          CardHint(st.lastTitle),
          const SizedBox(height: 10),
          if (st.lastOutput.trim().isEmpty)
            Text(
              '先在下面选一个模式，然后点「一键优化」。\n结果会显示在这里。',
              style: TextStyle(
                  color: T.fgFaint, fontSize: T.fsSmall, height: 1.6),
            )
          else
            Container(
              width: double.infinity,
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: const Color(0xFF0A0C10),
                borderRadius: BorderRadius.circular(8),
                border: Border.all(color: T.line),
              ),
              child: SelectableText(
                st.lastOutput.trim(),
                style: TextStyle(
                  color: T.fgDim,
                  fontSize: T.fsTiny,
                  height: 1.65,
                  fontFamily: 'Consolas',
                ),
              ),
            ),
        ],
      ),
    );
  }
}
