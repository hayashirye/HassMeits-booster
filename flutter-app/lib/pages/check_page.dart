// 体检页 —— 电源方案逐项核对 + 后台抢 CPU 的程序。
import 'package:flutter/material.dart';

import '../app_state.dart';
import '../hardware.dart';
import '../theme.dart';
import '../widgets/app_card.dart';
import '../widgets/press_button.dart';
import '../widgets/data_grid.dart';

class CheckPage extends StatelessWidget {
  final AppState st;
  const CheckPage(this.st, {super.key});

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: st,
      builder: (_, __) {
        final c = st.check;
        return Padding(
          padding: const EdgeInsets.fromLTRB(18, 16, 18, 18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              // ---------------------------------------------------- 顶部
              Row(
                children: [
                  const CardTitle('电源方案核对'),
                  const SizedBox(width: 10),
                  if (st.checking)
                    SizedBox(
                      width: 13,
                      height: 13,
                      child: CircularProgressIndicator(
                          strokeWidth: 1.6, color: T.accent),
                    ),
                  const Spacer(),
                  PressButton(
                    label: '重新检测',
                    onTap: st.checking ? null : st.refreshCheck,
                    padding: const EdgeInsets.symmetric(
                        horizontal: 18, vertical: 11),
                  ),
                ],
              ),
              const SizedBox(height: 10),

              // ------------------------------------------- 本机环境
              _machineCard(),

              const SizedBox(height: 12),

              // ------------------------------------------- 电源方案表
              Expanded(
                flex: 5,
                child: AppCard(
                  padding: const EdgeInsets.fromLTRB(2, 4, 2, 4),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              '当前电源方案：${c.schemeName.isEmpty ? '—' : c.schemeName}',
                              style: TextStyle(
                                  color: T.fg, fontSize: T.fsSmall),
                            ),
                            const SizedBox(height: 2),
                            Text(
                              '方案 GUID：${c.schemeGuid.isEmpty ? '—' : c.schemeGuid}',
                              style: TextStyle(
                                color: T.fgFaint,
                                fontSize: T.fsTiny,
                                fontFamily: 'Consolas',
                              ),
                            ),
                          ],
                        ),
                      ),
                      Expanded(
                        child: DataGrid(
                          playToken: c.power.length,
                          headHeight: 28,
                          rowHeight: 25,
                          cols: const [
                            GridCol('设置项', flex: 50),
                            GridCol('交流 AC', flex: 10, right: true),
                            GridCol('电池 DC', flex: 10, right: true),
                            GridCol('判定', flex: 17, badge: true),
                          ],
                          rows: [
                            for (final p in c.power)
                              GridRow(
                                [p.name, p.ac, p.dc, p.verdict],
                                kind: p.kind,
                                tooltip:
                                    '${p.name}　交流 ${p.ac}　电池 ${p.dc}　${p.verdict}',
                              ),
                          ],
                        ),
                      ),
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 8, 16, 6),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              '符合 ${c.okCount} 项，不符合 ${c.badCount} 项，本机无此项 ${c.naCount} 项。',
                              style: TextStyle(
                                  color: T.fgDim, fontSize: T.fsSmall),
                            ),
                            if (c.footer.isNotEmpty) ...[
                              const SizedBox(height: 3),
                              Text(
                                c.footer,
                                style: TextStyle(
                                  color: c.badCount > 0 ? T.bad : T.ok,
                                  fontSize: T.fsSmall,
                                ),
                              ),
                            ],
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 12),

              // ----------------------------------------- 后台 CPU 表
              Expanded(
                flex: 4,
                child: AppCard(
                  padding: const EdgeInsets.fromLTRB(2, 4, 2, 4),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 10, 16, 6),
                        child: Text(
                          // ★ 「22 个逻辑核」原来是写死的 —— 那是这台机器的数。
                          //   换台 12 线程的 Ryzen 就会显示错的核数。
                          '后台抢占 CPU 的程序（6 秒采样，本机 '
                          '${st.machine?.topo.logicalCpus ?? '?'} 个逻辑核）',
                          style: TextStyle(color: T.fgDim, fontSize: T.fsSmall),
                        ),
                      ),
                      Expanded(
                        child: DataGrid(
                          playToken: st.bg.length,
                          headHeight: 30,
                          rowHeight: 26,
                          cols: const [
                            GridCol('占单核', flex: 18, bar: true),
                            GridCol('进程', flex: 26),
                            GridCol('说明', flex: 70),
                          ],
                          rows: [
                            for (final r in st.bg)
                              GridRow(
                                [
                                  '${r.pct.toStringAsFixed(1)}%',
                                  r.name,
                                  r.note,
                                ],
                                kind: r.pct >= 40
                                    ? 1
                                    : (r.pct >= 10 ? 3 : 2),
                                bar: r.pct,
                                tooltip:
                                    '${r.name}　${r.pct.toStringAsFixed(1)}% 单核\n${r.note}',
                              ),
                          ],
                        ),
                      ),
                      if (st.bgFooter.isNotEmpty)
                        Padding(
                          padding: EdgeInsets.fromLTRB(
                              16, 6, 16, st.bgJudge.isEmpty ? 8 : 2),
                          child: Text(
                            st.bgFooter,
                            style: TextStyle(
                                color: T.fgDim, fontSize: T.fsSmall),
                          ),
                        ),
                      // ★ 判读行。C# 版这两行是跟着合计数一起显示的，其中带 ★ 的
                      //   那句往往才是整页最可操作的一条（实测到 dwm 吃 CPU，
                      //   就等于告诉你游戏跑的是无边框窗口）。这里让它单独占一段、
                      //   并且用琥珀色从灰色的合计行里跳出来。
                      if (st.bgJudge.isNotEmpty)
                        Padding(
                          padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: <Widget>[
                              for (final line in st.bgJudge.split('\n'))
                                Padding(
                                  padding: const EdgeInsets.only(top: 4),
                                  child: Text(
                                    line,
                                    style: TextStyle(
                                      color: line.startsWith('★')
                                          ? T.warn
                                          : T.fgDim,
                                      fontSize: T.fsSmall,
                                      height: 1.45,
                                    ),
                                  ),
                                ),
                            ],
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }

  /// 本机环境 —— 每次打开程序都重新检测（实现见 `lib/hardware.dart`）。
  ///
  /// 这块不是装饰。引擎把能效核掩码写成了常量（`one\Core.cs:2107-2108`），
  /// 那是作者那台 Ultra 7 155H 的拓扑；换台机器可能
  /// ① 整条 `SetProcessAffinityMask` 失败（err 87）而界面毫无提示，或
  /// ② 装得下但钉错核，反而和游戏抢核。所以这里如实报出来。
  Widget _machineCard() {
    final m = st.machine;
    if (m == null) {
      return AppCard(
        padding: const EdgeInsets.fromLTRB(16, 13, 16, 13),
        child: Row(
          children: [
            if (st.probing)
              SizedBox(
                width: 13,
                height: 13,
                child: CircularProgressIndicator(
                    strokeWidth: 1.6, color: T.accent),
              ),
            const SizedBox(width: 10),
            Text('正在检测本机硬件…',
                style: TextStyle(color: T.fgDim, fontSize: T.fsSmall)),
          ],
        ),
      );
    }

    final t = m.topo;
    final a = st.pinAssessment;

    final String kindText;
    final Color kindColor;
    if (t.isUniform) {
      kindText = '全对称核';
      kindColor = T.fgDim;
    } else if (t.isHybrid) {
      kindText = '大小核 · ${t.tiers.length} 档';
      kindColor = T.ok;
    } else {
      kindText = '${t.tiers.length} 档';
      kindColor = T.fgDim;
    }

    return AppCard(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 13),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  m.cpuName.isEmpty ? '未知处理器' : m.cpuName,
                  style: TextStyle(
                    color: T.fg,
                    fontSize: T.fsBody,
                    fontWeight: FontWeight.w600,
                  ),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              const SizedBox(width: 10),
              _chip(kindText, kindColor),
            ],
          ),
          const SizedBox(height: 5),
          Row(
            children: [
              Expanded(
                child: Text(
                  [
                    m.shortCpu,
                    if (m.shortMachine.isNotEmpty) m.shortMachine,
                    if (t.hasBattery == true) (t.onAc == true ? '插着电' : '用电池'),
                  ].join('　·　'),
                  style: TextStyle(color: T.fgDim, fontSize: T.fsSmall),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              if (a != null) ...[
                const SizedBox(width: 10),
                _chip(
                  _verdictText(a.verdict),
                  a.usable ? T.ok : (a.kind == 3 ? T.warn : T.bad),
                ),
              ],
            ],
          ),
          if (a != null && !a.usable) ...[
            const SizedBox(height: 7),
            Text(
              a.headline,
              style:
                  TextStyle(color: T.warn, fontSize: T.fsSmall, height: 1.45),
            ),
          ],
        ],
      ),
    );
  }

  static String _verdictText(PinVerdict v) {
    switch (v) {
      case PinVerdict.exact:
        return '压制：可用';
      case PinVerdict.overflow:
        return '压制：掩码装不下';
      case PinVerdict.uniform:
        return '压制：无大小核';
      case PinVerdict.mismatch:
        return '压制：掩码对不上';
      case PinVerdict.unknown:
        return '压制：判不了';
    }
  }

  Widget _chip(String text, Color color) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.14),
          borderRadius: BorderRadius.circular(100),
          border: Border.all(color: color.withValues(alpha: 0.45)),
        ),
        child: Text(
          text,
          style: TextStyle(
            color: color,
            fontSize: T.fsTiny,
            fontWeight: FontWeight.w600,
          ),
        ),
      );
}
