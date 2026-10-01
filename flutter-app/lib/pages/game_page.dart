// 游戏专项页 —— 主程序/显卡绑定、后台守护与压制、配置文件检查。
import 'package:flutter/material.dart';

import '../app_state.dart';
import '../engine.dart';
import '../models.dart';
import '../theme.dart';
import '../widgets/app_card.dart';
import '../widgets/data_grid.dart';
import '../widgets/press_button.dart';

class GamePage extends StatelessWidget {
  final AppState st;
  const GamePage(this.st, {super.key});

  // ★ 这里原来有一排自己画的无畏契约/Apex 页签，现在删掉了 ——
  //   侧栏顶部已经有一个 M3 的 SegmentedButton 在做同一件事，
  //   同一屏里出现两个「选游戏」的控件既冗余、又容易让人以为它们不同步。
  //   游戏的切换入口就只留侧栏那一个（C# 版也是这个位置）。

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: st,
      builder: (_, __) {
        final g = st.currentGame;
        return SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(18, 16, 18, 18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _exeCard(g),
              const SizedBox(height: 12),
              _daemonCard(g),
              const SizedBox(height: 12),
              _configCard(g),
            ],
          ),
        );
      },
    );
  }

  // ---------------------------------------------------- 主程序与显卡绑定

  Widget _exeCard(GameInfo g) {
    final exe = g.exe.isEmpty ? '还没有登记 —— 点右边的按钮' : g.exe;
    return AppCard(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const CardTitle('主程序与显卡绑定'),
                    const SizedBox(height: 10),
                    SelectableText(
                      exe,
                      style: TextStyle(
                        color: T.fgDim,
                        fontSize: T.fsSmall,
                        fontFamily: 'Consolas',
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 16),
              PressButton(
                label: g.exe.isEmpty ? '登记高性能' : '已登记（可重设）',
                primary: g.exe.isNotEmpty,
                onTap: st.busy ? null : st.doGpuPref,
                padding:
                    const EdgeInsets.symmetric(horizontal: 22, vertical: 13),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              Text(
                '显卡绑定：',
                style: TextStyle(color: T.fgDim, fontSize: T.fsSmall),
              ),
              Text(
                g.exe.isEmpty ? '未登记' : '高性能（独显）',
                style: TextStyle(
                  color: g.exe.isEmpty ? T.warn : T.ok,
                  fontSize: T.fsSmall,
                  fontWeight: FontWeight.w600,
                ),
              ),
              if (g.tier.isNotEmpty) ...[
                const SizedBox(width: 14),
                Text(
                  '建议档位：${g.tier}',
                  style: TextStyle(color: T.fgDim, fontSize: T.fsSmall),
                ),
              ],
            ],
          ),
          if (g.note.isNotEmpty) ...[
            const SizedBox(height: 8),
            CardHint(g.note),
          ],
        ],
      ),
    );
  }

  // -------------------------------------------------------- 后台守护

  Widget _daemonCard(GameInfo g) {
    final on = st.autoStart;
    // 被压到能效核的进程名（去重）
    // ★ 比对的是**引擎自己报的**能效核掩码，不是写死的 0x3003FC ——
    //   换台机器掩码就不是这个数了。
    final ecore = st.pinReport.ecoreMask;
    final names = <String>[];
    for (final p in st.pins) {
      if (p.pinnedAs(ecore) && !names.contains(p.name)) names.add(p.name);
    }
    return AppCard(
      padding: const EdgeInsets.fromLTRB(20, 16, 20, 18),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const CardTitle('后台守护'),
                    const SizedBox(height: 10),
                    Text(
                      on ? '已开启 · 开机静默启动' : '未开启',
                      style: TextStyle(
                        color: on ? T.ok : T.warn,
                        fontSize: T.fsSmall,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(height: 4),
                    const CardHint('游戏启动时自动优化，退出后自动还原 —— 全程不弹 UAC。'),
                  ],
                ),
              ),
              const SizedBox(width: 16),
              PressButton(
                label: on ? '关闭守护' : '开启守护',
                onTap: st.busy ? null : st.toggleAutoStart,
                padding:
                    const EdgeInsets.symmetric(horizontal: 22, vertical: 13),
              ),
            ],
          ),
          const SizedBox(height: 18),
          Divider(height: 1, color: T.line),
          const SizedBox(height: 16),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const CardTitle('后台程序压制'),
                    const SizedBox(height: 8),
                    Text(
                      _pinText(names),
                      style: TextStyle(
                        color: names.isEmpty ? T.fgDim : T.fg,
                        fontSize: T.fsSmall,
                      ),
                    ),
                    // ★ 本机掩码对不上时，别让「已压制 N 类」这句话骗人 ——
                    //   引擎在那种机器上每次都返回「设亲和性失败」，
                    //   界面看起来却一切正常。
                    if (st.pinAssessment != null &&
                        !st.pinAssessment!.usable) ...[
                      const SizedBox(height: 5),
                      Text(
                        st.pinAssessment!.headline,
                        style: TextStyle(
                          color: T.warn,
                          fontSize: T.fsTiny,
                          height: 1.5,
                        ),
                      ),
                    ],
                    if (names.isNotEmpty) ...[
                      const SizedBox(height: 4),
                      Text(
                        names.join('、'),
                        style: TextStyle(
                          color: T.fgFaint,
                          fontSize: T.fsTiny,
                          height: 1.6,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
              const SizedBox(width: 16),
              PressButton(
                label: st.pins.isEmpty ? '压制后台程序' : '放回全部核心',
                onTap: st.busy
                    ? null
                    : (st.pins.isEmpty ? st.doPin : st.doUnpin),
                padding:
                    const EdgeInsets.symmetric(horizontal: 22, vertical: 13),
              ),
            ],
          ),
        ],
      ),
    );
  }

  /// 压制那一行的措辞要跟着本机拓扑走。
  ///
  /// 原来的两句都写死了「能效核」—— 在一台全大核的 AMD 上，
  /// 「压到能效核」这句话本身就是错的（那里根本没有能效核）。
  String _pinText(List<String> names) {
    final a = st.pinAssessment;
    final bad = a != null && !a.usable;
    if (names.isEmpty) {
      if (bad) return '本机没有可用的能效核，「压制」不会生效。';
      return '现在没有进程被压到能效核。';
    }
    if (bad) return '已压制 ${names.length} 类（但本机掩码对不上，未必真生效）：';
    return '已压制 ${names.length} 类，只跑能效核（P 核腾给游戏，加速照常）：';
  }

  // -------------------------------------------------------- 配置文件检查

  Widget _configCard(GameInfo g) {
    final head = g.configFound
        ? g.configPath
        : (g.configPath.isEmpty ? '没有找到配置文件' : '${g.configPath}（不存在）');
    return AppCard(
      padding: const EdgeInsets.fromLTRB(2, 14, 2, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(18, 0, 18, 0),
            child: Row(
              children: [
                const CardTitle('配置文件检查'),
                const Spacer(),
                PressButton(
                  label: '打开配置文件',
                  onTap: g.configFound
                      ? () => Engine.i.openPath(g.configPath)
                      : null,
                  padding:
                      const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                ),
                const SizedBox(width: 10),
                PressButton(
                  label: '重新检查',
                  onTap: st.auditing ? null : () => st.reloadGame(st.gameKey),
                  padding:
                      const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                ),
              ],
            ),
          ),
          const SizedBox(height: 10),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18),
            child: Tooltip(
              message: head,
              child: Text(
                head,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  color: T.fgDim,
                  fontSize: T.fsSmall,
                  fontFamily: 'Consolas',
                ),
              ),
            ),
          ),
          const SizedBox(height: 10),
          SizedBox(
            // 表格自己滚，所以这里给一个固定高度
            height: (34 + g.checks.length * 28.0).clamp(120.0, 420.0),
            child: DataGrid(
              playToken: g.checks.length,
              headHeight: 32,
              rowHeight: 28,
              cols: const [
                GridCol('检查项', flex: 30),
                GridCol('实际值', flex: 34),
                GridCol('判定', flex: 22, badge: true),
                GridCol('说明', flex: 110),
              ],
              rows: [
                for (final c in g.checks)
                  GridRow(
                    [c.name, c.value, c.verdict, c.advice],
                    kind: c.kind,
                    tooltip:
                        '${c.name}　${c.value}　${c.verdict}\n${c.advice}',
                  ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(18, 10, 18, 2),
            child: Text(
              '不符合 ${g.badCount} 项，值得注意 ${g.warnCount} 项。',
              style: TextStyle(
                color: g.badCount > 0 ? T.bad : T.fgDim,
                fontSize: T.fsSmall,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

