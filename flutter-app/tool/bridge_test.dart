// 端到端桥接测试：**真的**启动 ValorantBoost.exe，走 `Engine.run()` 那条路，
// 再用同一批解析函数吃掉结果。
//
// 跟 `parse_test.dart` 的分工：
//   parse_test  —— 把「已经抓下来的文本」喂给解析函数，只管格式对不对；
//   本文件      —— 验证传输层：进程能不能起来、`--out` 文件有没有生成、
//                  UTF-8 解码对不对、退出码是什么、耗时离超时还有多少余量。
//
// ★ 为什么这个测试值得写：Flutter 版跟 C# 版之间唯一的接口就是这个桥，
//   而桥上的坑（编码、超时、文件句柄、进程名）在 analyze 阶段一个都看不见。
//   本机没有 MSVC 也能跑 —— 只要有 Dart VM。
//
// 跑法（在 flutter-app 目录下）：
//     dart run tool/bridge_test.dart
//
// 全程只跑**只读**命令（check / bgcpu / pinstat / audit / autocheck），
// 不会碰电源方案，也不会装计划任务。
//
// 命令行测试脚本，print 就是它的输出方式。
// ignore_for_file: avoid_print
import 'dart:convert';
import 'dart:io';

import 'package:valorant_boost/engine.dart';

int _fail = 0;
void _chk(bool ok, String what, [String detail = '']) {
  if (ok) {
    print('  ✓ $what');
  } else {
    _fail++;
    print('  ✗ $what${detail.isEmpty ? '' : '  → $detail'}');
  }
}

void _h(String t) {
  print('');
  print('─' * 74);
  print('  $t');
  print('─' * 74);
}

String _hex(int? m) =>
    m == null ? '（没有）' : '0x${m.toRadixString(16).toUpperCase()}';

Future<void> main() async {
  // ---------------------------------------------------------- 环境探针
  _h('传输层探针 —— 先排除环境问题，别把沙箱限制误判成代码 bug');
  var pipeOk = false;
  try {
    final r = await Process.run('cmd', ['/c', 'echo probe-ok'],
        stdoutEncoding: null, stderrEncoding: null);
    final s = utf8.decode(r.stdout as List<int>, allowMalformed: true).trim();
    pipeOk = s.contains('probe-ok');
    _chk(pipeOk, 'Process.run 能抓到子进程 stdout', s);
  } catch (e) {
    _chk(false, 'Process.run 抛异常（环境限制，不是代码问题）', '$e');
  }

  // ---------------------------------------------------------- 定位引擎
  _h('定位引擎 exe');
  final ok = Engine.i.locate();
  print('  候选: ${Engine.i.exe ?? '(都没找到)'}');
  print('  lastError: ${Engine.i.lastError}');
  _chk(ok, 'locate() 找到了引擎 exe', Engine.i.exe ?? Engine.i.lastError);
  if (!ok) {
    print('\n  ⇒ 引擎不在预期位置，后面的断言没有意义。');
    exit(2);
  }
  _chk(File(Engine.i.exe!).existsSync(), 'exe 文件确实存在');

  // ---------------------------------------------------------- check
  _h('check —— 走 Engine.check()（跑 exe + --out + 解析）');
  final sw = Stopwatch()..start();
  final c = await Engine.i.check();
  sw.stop();
  print('  耗时 ${sw.elapsedMilliseconds} ms　方案=${c.schemeName}　'
      '符合 ${c.okCount} / 不符合 ${c.badCount} / 无此项 ${c.naCount}');
  _chk(c.schemeName.isNotEmpty, 'Engine.check() 拿到了结果（传输通了）',
      c.schemeName.isEmpty ? '空 —— 见 lastError: ${Engine.i.lastError}' : '');
  _chk(c.power.isNotEmpty, '电源表解析出 ${c.power.length} 行');
  _chk(c.power.every((r) => r.name.isNotEmpty), '每行都有名字');
  _chk(Engine.i.lastError.isEmpty, 'lastError 是空的', Engine.i.lastError);

  // ---------------------------------------------------------- bgcpu
  _h('bgcpu —— 采样 6 秒，验证长命令不超时');
  sw.reset();
  sw.start();
  final bgText = await Engine.i.run(['bgcpu'],
      timeout: const Duration(seconds: 40));
  sw.stop();
  final bg = Engine.parseBg(bgText.out);
  print('  耗时 ${sw.elapsedMilliseconds} ms　表格 ${bg.length} 行');
  for (final r in bg.take(4)) {
    print('    ${r.pct.toStringAsFixed(1).padLeft(5)}%  '
        '${r.name.padRight(20)} ${r.note}');
  }
  _chk(bg.isNotEmpty, 'bgcpu 有输出且能解析', '${bg.length} 行');
  _chk(bgText.ms < 40000, '耗时在超时之内', '${bgText.ms} ms');
  _chk(bg.every((r) => !r.name.contains('  ')), '没有进程名被切成两半');
  if (bg.isNotEmpty) {
    _chk(bg.first.pct > 0, '第一行有正的占比', '${bg.first.pct}');
  }

  // ---------------------------------------------------------- pinstat
  _h('pinstat —— 验证大批量文本（30+ 行）能完整过桥');
  final pinText = await Engine.i.run(['pinstat'],
      timeout: const Duration(seconds: 90));
  final rep = Engine.parsePin(pinText.out);
  final pins = rep.rows;
  print('  字节 ${pinText.out.length}　表格 ${pins.length} 行');
  print('  引擎报的掩码：能效核 ${_hex(rep.ecoreMask)}　'
      '性能核 ${_hex(rep.pcoreMask)}');
  _chk(pins.isNotEmpty, 'pinstat 解析出进程行', '${pins.length}');
  _chk(pins.every((p) => p.mask.toUpperCase().startsWith('0X')), '每行掩码完整');
  _chk(rep.hasMasks, '解析出引擎实报的两个掩码',
      'ecore=${rep.ecoreMask} pcore=${rep.pcoreMask}');

  // ---------------------------------------------------------- audit
  for (final key in <String>['valorant', 'apex']) {
    _h('audit $key');
    final t = await Engine.i.run(['audit', key]);
    final g = Engine.parseAudit(t.out, key);
    print('  ${g.name}　exe=${g.exe}');
    print('  检查 ${g.checks.length} 项，不符合 ${g.badCount} / 注意 ${g.warnCount}');
    for (final ck in g.checks.take(3)) {
      print('    [${ck.verdict}] ${ck.name} = ${ck.value}');
    }
    _chk(g.name.isNotEmpty, '$key 名称解析正确', g.name);
    _chk(g.exe.contains('\\'), '$key 主程序路径解析正确', g.exe);
    _chk(g.configPath.isNotEmpty, '$key 配置路径解析正确', g.configPath);
    _chk(g.checks.isNotEmpty, '$key 检查项解析正确', '${g.checks.length}');
    // ★ 中文必须原样过来 —— 这条专门盯 UTF-8 解码
    _chk(g.checks.every((e) => !e.advice.contains('\uFFFD')),
        '$key 的建议文本没有替换字符（UTF-8 解码正确）');
    _chk(g.tier == '竞技', '$key 推荐档位', g.tier);
  }

  // ---------------------------------------------------------- autocheck
  _h('autocheck —— 守护状态');
  final ac = await Engine.i.run(['autocheck'],
      timeout: const Duration(seconds: 60));
  print('  退出码 ${ac.code}　字节 ${ac.out.length}');
  print('  首行: ${ac.out.split('\n').first.trim()}');
  _chk(ac.out.isNotEmpty, 'autocheck 有输出');

  // ---------------------------------------------------------- schtasks
  _h('autoStartInstalled() —— 走 schtasks，不走引擎');
  final inst = await Engine.i.autoStartInstalled();
  print('  autoStart = $inst');
  _chk(inst, '计划任务 ValorantBoostWatcher 存在（本机确实装了）');

  // ---------------------------------------------------------- 日志
  _h('readLog() —— 直接读 boost-log.txt');
  final lines = await Engine.i.readLog(maxLines: 5);
  print('  拿到 ${lines.length} 行（最新的在前）:');
  for (final l in lines) {
    print('    $l');
  }
  _chk(lines.isNotEmpty, '读到了日志');
  _chk(lines.every((l) => !l.contains('\uFFFD')), '日志没有替换字符');

  // ---------------------------------------------------------- 收尾
  _h(_fail == 0 ? '全部通过' : '有 $_fail 条不通过');
  if (!pipeOk) {
    print('  注：stdout 管道不可用，但引擎走的是 --out 文件，不影响。');
  }
  exit(_fail == 0 ? 0 : 1);
}
