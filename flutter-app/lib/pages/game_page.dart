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
              _configCard(context, g),
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

  Widget _configCard(BuildContext context, GameInfo g) {
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
                // ★ 这个按钮只在引擎真的报出了「着色器预缓存」这一行时才出现 ——
                //   按数据判断，不是按游戏 key 写死。引擎那边对不认识配置形状的
                //   游戏会拒绝执行（见 Core.cs 的 Pso.Clean），所以没这一行就没按钮，
                //   用户不会点到一个只会报错的按钮。
                if (g.checks.any((c) => c.name == '着色器预缓存')) ...[
                  const SizedBox(width: 10),
                  PressButton(
                    label: '清理旧缓存',
                    onTap: st.auditing ? null : () => _psoClean(context, g),
                    padding:
                        const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                  ),
                ],
                // ★ 锐化这一项同样是「按数据出现」，而且按钮的文案跟着当前状态变：
                //   没写就是「写入锐化 0.4」，已经写了就变成「还原 Engine.ini」。
                //   这样永远只有一个按钮、不会让人猜哪个是当前状态。
                if (_sharpenOn(g) != null) ...[
                  const SizedBox(width: 10),
                  PressButton(
                    label: _sharpenOn(g)! ? '还原 Engine.ini' : '写入锐化 0.4',
                    onTap: st.auditing
                        ? null
                        : () => _sharpen(context, g, _sharpenOn(g)!),
                    padding:
                        const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
                  ),
                ],
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

  // ---------------------------------------------------- Engine.ini 锐化
  //
  // 锐化那一项当前是什么状态：null = 这个游戏没有这一项（按钮不出现），
  // false = 还没写过，true = 已经写进去了。故意从检查结果里读，不再另外问引擎一次。
  bool? _sharpenOn(GameInfo g) {
    for (final c in g.checks) {
      if (c.name == '锐化') return c.value != '没设置';
    }
    return null;
  }

  // 写入要确认，还原不要 —— 风险是不对称的：写入是往用户的游戏配置里加东西，
  // 还原只是把备份整份盖回去（而备份就是【原始】那一版）。给还原也弹个确认框，
  // 只会让人以为「还原」也是危险操作。
  //
  // ★ 为什么值只有 0.4 这一项：查得到的鸣潮 cvar 表里，有社区共识取值的就这一个。
  //   r.Streaming.PoolSize 没有鸣潮推荐值，sg.KuroRenderQuality /
  //   sg.KuroLocalRenderQuality 的 0-3 语义也查不到 —— 猜个数值写进去，等于拿
  //   用户的画面做实验。所以引擎只实现了这一项，界面上如实标「未经验证」。
  Future<void> _sharpen(BuildContext context, GameInfo g, bool on) async {
    if (!on) {
      final ok = await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: const Text('写入锐化设置'),
          content: const SingleChildScrollView(
            child: Text(
              '会往 Engine.ini 的 [SystemSettings] 段写一行：\n\n'
              '    r.Tonemapper.Sharpen=0.4\n\n'
              '作用：抵消一部分 TAA 造成的画面发糊。\n'
              '代价：开太高会有白边，所以用的是社区在用的 0.4。\n\n'
              '★ 这一项没有官方文档背书，取值来自社区，标为「未经验证」。\n'
              '★ 改之前会整份备份成 Engine.ini.vcb.bak，随时能一键还原。\n'
              '★ 游戏正在运行时引擎会拒绝写入 —— 那种情况下退出游戏时改动可能被吞掉。',
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(false),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(ctx).pop(true),
              child: const Text('写入'),
            ),
          ],
        ),
      );
      if (ok != true || !context.mounted) return;
    }

    final res = on
        ? await Engine.i.engIniRevert(g.key)
        : await Engine.i.engIniApply(g.key);
    if (!context.mounted) return;
    ScaffoldMessenger.maybeOf(context)?.showSnackBar(
      SnackBar(content: Text(res.text.trim().replaceAll('\n', '　'))),
    );
    // 重跑一遍 audit —— 「锐化」那一行和这个按钮的文案都要跟着变
    await st.reloadGame(st.gameKey);
  }

  // ---------------------------------------------------- 旧着色器缓存清理

  // 分两步，永远是「先预览、后确认」：
  //   1) psodry   —— 引擎只列清单，一个文件都不动
  //   2) psoclean —— 用户点了确认才真删
  // 删掉的是游戏自己生成的、属于【旧版本】的着色器预缓存（鸣潮那边攒了几百 MB，
  // 换显卡驱动之后旧的那批还会继续失效堆积）。引擎下次进游戏会重新编译、重新攒
  // 回来，所以代价只是「接下来一段时间进新场景略慢」。不碰存档、不碰游戏本体。
  //
  // ★ 这里刻意不按游戏 key 写死 —— 按钮出不出现由引擎有没有报出「着色器预缓存」
  //   那一行决定。引擎对配置目录形状不认识的游戏会拒绝执行（Core.cs 的 Pso.Clean），
  //   所以不会出现「点了只会报错」的按钮。
  Future<void> _psoClean(BuildContext context, GameInfo g) async {
    final dry = await Engine.i.psoDry(g.key);
    if (!context.mounted) return;

    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('清理旧版着色器缓存'),
        content: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: SingleChildScrollView(
            child: SelectableText(
              '${dry.text.trim()}\n\n'
              '确认后才会真的删除。之后再进游戏时，引擎会重新编译着色器 ——\n'
              '第一次进新场景会比平时慢一点，攒回来之后恢复正常。\n'
              '不会碰存档，也不会碰游戏本体的任何文件。',
              style: const TextStyle(fontFamily: 'Consolas', fontSize: 13),
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(ctx).pop(true),
            child: const Text('确认清理'),
          ),
        ],
      ),
    );
    if (confirm != true || !context.mounted) return;

    final res = await Engine.i.psoClean(g.key);
    if (!context.mounted) return;
    ScaffoldMessenger.maybeOf(context)?.showSnackBar(
      SnackBar(content: Text(res.text.trim().replaceAll('\n', '　'))),
    );
    // 重跑一遍 audit，把「着色器预缓存」那一行的数字刷新成清理后的样子
    await st.reloadGame(st.gameKey);
  }
}

