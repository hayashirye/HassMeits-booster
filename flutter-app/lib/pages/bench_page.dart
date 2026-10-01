// 性能测试页 —— 固定时间里的开方次数，不依赖任何系统计数器。
//
// ★★ 这一页对应的是 CLI 的 `bench`（C# 里的 `CmdBench`，`Ui.cs:2887`），
//    是 **5 轮 × 6 秒的持续负载**测试，看的是「掉不掉频」——
//    跟 C# GUI 上那个瞬时吞吐按钮（`Bench.Full()`）不是同一个测试。
//    详见 `models.dart` 里 `BenchResult` 的注释。
import 'package:flutter/material.dart';

import '../app_state.dart';
import '../models.dart';
import '../theme.dart';
import '../widgets/app_card.dart';
import '../widgets/press_button.dart';

/// 判定配色约定，与表格的药丸一致：0 绿 / 1 红 / 2 灰 / 3 琥珀。
Color _k(int k) {
  switch (k) {
    case 0:
      return T.ok;
    case 1:
      return T.bad;
    case 3:
      return T.warn;
    default:
      return T.mute;
  }
}

class BenchPage extends StatelessWidget {
  final AppState st;
  const BenchPage(this.st, {super.key});

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: st,
      builder: (_, __) {
        final b = st.bench;
        return SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(18, 16, 18, 18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _headline(b),
              const SizedBox(height: 14),

              // ------------------------------------------- 按钮
              // 5 轮 × 6 秒，实测约 39 秒 —— 原先写的「约 8 秒」是照 GUI
              // 那个瞬时测试抄的，会让人以为卡死了。
              PressButton(
                label: st.busy ? '正在测试…（约 40 秒）' : '开始性能测试（约 40 秒）',
                primary: true,
                onTap: st.busy ? null : st.runBench,
                padding: const EdgeInsets.symmetric(vertical: 15),
              ),
              const SizedBox(height: 14),

              if (b != null && b.rounds.isNotEmpty) ...[
                _rounds(b),
                const SizedBox(height: 14),
              ],
              if (b != null && b.verdict.isNotEmpty) ...[
                _verdict(b),
                const SizedBox(height: 14),
              ],
              _notes(),
            ],
          ),
        );
      },
    );
  }

  // ------------------------------------------------------------ 头条数字
  Widget _headline(BenchResult? b) {
    return AppCard(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            b == null ? '还没测过' : '中位数',
            style: TextStyle(
              color: b == null ? T.fgDim : _k(b.kind),
              fontSize: T.fsTiny,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 8),
          if (b == null)
            Text(
              '点下面的按钮，实测这颗 CPU 到底能出多少力。',
              style: TextStyle(color: T.fgDim, fontSize: T.fsSmall, height: 1.5),
            )
          else ...[
            Row(
              crossAxisAlignment: CrossAxisAlignment.baseline,
              textBaseline: TextBaseline.alphabetic,
              children: [
                Text(
                  groupNum(b.median),
                  style: TextStyle(
                    color: _k(b.kind),
                    fontSize: T.fsBig,
                    fontWeight: FontWeight.w700,
                    height: 1.1,
                  ),
                ),
                const SizedBox(width: 8),
                Text('次 / 秒',
                    style: TextStyle(color: T.fgDim, fontSize: T.fsSmall)),
              ],
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 6,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                if (b.rounds.isNotEmpty)
                  Pill('首→末衰减 ${b.dropPct.toStringAsFixed(1)}%', _k(b.kind)),
                if (b.totalSecs > 0)
                  Pill('${b.rounds.length} 轮 × ${b.roundSecs} 秒',
                      T.mute),
                if (b.cpuName.isNotEmpty) Pill(b.cpuName, T.mute),
              ],
            ),
            if (b.when.isNotEmpty) ...[
              const SizedBox(height: 8),
              Text(
                '测于 ${b.when}',
                style: TextStyle(
                    color: T.fgFaint, fontSize: T.fsTiny, fontFamily: 'Consolas'),
              ),
            ],
          ],
        ],
      ),
    );
  }

  // ------------------------------------------------------------ 每轮曲线
  Widget _rounds(BenchResult b) {
    final peak = b.rounds.reduce((a, c) => a > c ? a : c);
    return AppCard(
      padding: const EdgeInsets.fromLTRB(18, 14, 18, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const CardTitle('每一轮'),
          const SizedBox(height: 4),
          const CardHint('同一个负载连着跑，看数字是不是一路往下掉 —— 掉就是在降频。'),
          const SizedBox(height: 12),
          for (var i = 0; i < b.rounds.length; i++)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(
                children: [
                  SizedBox(
                    width: 52,
                    child: Text(
                      '第 ${i + 1} 轮',
                      style: TextStyle(color: T.fgDim, fontSize: T.fsTiny),
                    ),
                  ),
                  Expanded(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(4),
                      child: Stack(
                        children: [
                          Container(height: 8, color: const Color(0xFF232833)),
                          FractionallySizedBox(
                            widthFactor:
                                peak <= 0 ? 0 : (b.rounds[i] / peak).clamp(0.0, 1.0),
                            child: Container(
                              height: 8,
                              decoration: BoxDecoration(
                                gradient: LinearGradient(colors: [
                                  _k(b.kind).withValues(alpha: 0.55),
                                  _k(b.kind),
                                ]),
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(width: 10),
                  SizedBox(
                    width: 118,
                    child: Text(
                      groupNum(b.rounds[i]),
                      textAlign: TextAlign.right,
                      style: TextStyle(
                        color: T.fg,
                        fontSize: T.fsTiny,
                        fontFamily: 'Consolas',
                      ),
                    ),
                  ),
                ],
              ),
            ),
          if (b.startMhz > 0 || b.endMhz > 0) ...[
            Divider(height: 20, color: T.line),
            Row(
              children: [
                Icon(Icons.speed, size: 14, color: T.fgFaint),
                const SizedBox(width: 6),
                Expanded(
                  child: Text(
                    '估算频率 ${b.startMhz.toStringAsFixed(0)} → '
                    '${b.endMhz.toStringAsFixed(0)} MHz'
                    '　·　停泊 ${b.parkedStart}/${b.logical} → '
                    '${b.parkedEnd}/${b.logical}',
                    style: TextStyle(
                        color: T.fgDim, fontSize: T.fsTiny, height: 1.5),
                  ),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }

  // ------------------------------------------------------------ 判读
  Widget _verdict(BenchResult b) {
    return AppCard(
      padding: const EdgeInsets.fromLTRB(18, 14, 18, 16),
      child: Container(
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: _k(b.kind).withValues(alpha: 0.08),
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: _k(b.kind).withValues(alpha: 0.35)),
        ),
        child: Text(
          b.verdict,
          style: TextStyle(color: _k(b.kind), fontSize: T.fsSmall, height: 1.7),
        ),
      ),
    );
  }

  // ------------------------------------------------------------ 说明
  Widget _notes() {
    return AppCard(
      padding: const EdgeInsets.fromLTRB(18, 14, 18, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '这个测试不看任何系统计数器 —— 它在固定时间里数 CPU 一共算了多少次开方，'
            '所以计数器损坏、读数被钉死都不影响它。同一台机器上数字越大，表示出力越足。',
            style: TextStyle(color: T.fgDim, fontSize: T.fsSmall, height: 1.7),
          ),
          const SizedBox(height: 12),
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: const Color(0xFF1A1608),
              borderRadius: BorderRadius.circular(8),
              border: Border.all(color: T.warn.withValues(alpha: 0.35)),
            ),
            child: Text(
              '注意：单次数字会有 ±20% 的天然抖动，所以这里跑 5 轮取中位数，'
              '并且用「首→末衰减」而不是绝对值得出降频结论。'
              '想比较两个设置谁更好，请在后台程序完全一样的状态下各测一次 —— '
              '后台多开一个客户端就足以盖过设置本身的影响。',
              style: TextStyle(color: T.warn, fontSize: T.fsSmall, height: 1.7),
            ),
          ),
        ],
      ),
    );
  }
}
