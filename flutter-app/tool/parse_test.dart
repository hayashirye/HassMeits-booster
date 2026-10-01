// 把「真实的引擎输出」喂给 engine.dart 里的解析函数，看界面会渲染成什么。
//
// ★ 为什么要有这个：本机没有 MSVC，跑不了 `flutter build windows`，
//   但 Dart VM 是现成的。CLI 契约（命令名、--out 行为、UTF-8 编码、文本格式）
//   不需要 GUI 就能验证 —— 这才是 Flutter 版最容易出错的地方，
//   因为 analyze 只能证明「能编译」，证明不了「解析得对」。
//
// 跑法（在 flutter-app 目录下）：
//     dart run tool/parse_test.dart
//
// 前置：先用真的 ValorantBoost.exe 生成样本，见下面的 _dir。
//
// 这是命令行测试脚本，不是应用代码 —— print 就是它的输出方式，
// 所以整文件关掉 avoid_print 这条 lint（43 条 info 里有 42 条是它）。
// ignore_for_file: avoid_print
import 'dart:convert';
import 'dart:io';

import 'package:valorant_boost/engine.dart';
import 'package:valorant_boost/models.dart';

const String _dir = r'D:\test\valorant-cpu-boost\_verify';

String _read(String name) {
  final f = File('$_dir\\$name.txt');
  if (!f.existsSync()) return '';
  return f.readAsStringSync(encoding: utf8);
}

void _h(String t) {
  print('');
  print('─' * 74);
  print('  $t');
  print('─' * 74);
}

String _hex(int? m) =>
    m == null ? '（没有）' : '0x${m.toRadixString(16).toUpperCase()}';

int _fail = 0;
void _chk(bool ok, String what, [String detail = '']) {
  if (ok) {
    print('  ✓ $what');
  } else {
    _fail++;
    print('  ✗ $what${detail.isEmpty ? '' : '  → $detail'}');
  }
}

void main() {
  // ---------------------------------------------------------------- check
  _h('check  →  parseCheck');
  final ct = _read('check');
  final c = Engine.parseCheck(ct);
  print('  方案      : ${c.schemeName}');
  print('  GUID      : ${c.schemeGuid}');
  print('  计数      : 符合 ${c.okCount} / 不符合 ${c.badCount} / 无此项 ${c.naCount}');
  print('  表格 ${c.power.length} 行:');
  for (final r in c.power) {
    print('    ${r.name.padRight(18)} AC=${r.ac.padRight(6)} '
        'DC=${r.dc.padRight(6)} ${r.verdict}');
  }
  print('  footer    : ${c.footer}');

  _chk(c.schemeName == '平衡', '方案名解析正确', c.schemeName);
  _chk(c.schemeGuid.startsWith('381b4222'), 'GUID 解析正确', c.schemeGuid);
  _chk(c.okCount == 7 && c.badCount == 1 && c.naCount == 2, '三个计数解析正确',
      '${c.okCount}/${c.badCount}/${c.naCount}');
  _chk(c.power.length == 10, '表格 10 行（含 2 行本机无此项）',
      '${c.power.length}');
  // ★ 这一条是关键：名字里带空格的那行不能被切碎
  final epp = c.power.where((r) => r.name.contains('EPP')).toList();
  _chk(epp.length == 1, '「能效偏好 EPP」整名保留', epp.map((e) => e.name).join());
  if (epp.isNotEmpty) {
    _chk(epp.first.ac == '33' && epp.first.dc == '50' && epp.first.verdict == '不符合',
        'EPP 行的 AC/DC/判定正确',
        '${epp.first.ac}/${epp.first.dc}/${epp.first.verdict}');
  }
  final naRow = c.power.where((r) => r.verdict == '本机无此项').toList();
  _chk(naRow.length == 2, '两行「本机无此项」', '${naRow.length}');
  _chk(c.footer.contains('1 项不符合'), 'footer 抓到不符合提示', c.footer);

  // ---------------------------------------------------------------- bgcpu
  _h('bgcpu  →  parseBg / bgFooter / bgJudge');
  final bt = _read('bgcpu');
  final bg = Engine.parseBg(bt);
  print('  表格 ${bg.length} 行:');
  for (final r in bg) {
    print('    ${r.pct.toStringAsFixed(1).padLeft(5)}%  '
        '${r.name.padRight(22)} ${r.note}');
  }
  final ftr = Engine.bgFooter(bt);
  print('  footer    : $ftr');
  print('  judge     :');
  for (final l in Engine.bgJudge(bt).split('\n')) {
    if (l.isNotEmpty) print('    $l');
  }

  // ★★ 这是本次修复的核心断言。
  //    C# 的进程名里有空格（"DeepSeek Harness"），旧的 \S+ 正则会把这一行
  //    切成 name="DeepSeek" + note="Harness …"。
  final ds = bg.where((r) => r.name == 'DeepSeek Harness').toList();
  _chk(ds.length == 1, '「DeepSeek Harness」整名保留（含空格）',
      bg.map((r) => r.name).join(' / '));
  if (ds.isNotEmpty) {
    _chk(ds.first.note.startsWith('AI 助手'),
        '该行的说明没有被名字吃掉', ds.first.note);
  }
  _chk(bg.every((r) => !r.name.contains('  ')), '没有任何名字带连续空格');
  _chk(bg.every((r) => r.pct > 0), '每行都有正的百分比');
  _chk(ftr.contains('合计') && ftr.contains('单核'), 'footer 抓到合计数', ftr);
  final judge = Engine.bgJudge(bt);
  _chk(judge.contains('判读：'), 'judge 抓到「判读：」行');
  _chk(judge.contains('★'), 'judge 抓到 ★ 专项提醒');

  // ---------------------------------------------------------------- pinstat
  _h('pinstat  →  parsePin');
  final pt = _read('pinstat');
  final rep = Engine.parsePin(pt);
  final pins = rep.rows;
  print('  掩码: 能效核 ${_hex(rep.ecoreMask)} "${rep.ecoreDesc}"　'
      '性能核 ${_hex(rep.pcoreMask)} "${rep.pcoreDesc}"');
  print('  表格 ${pins.length} 行，前 5 行:');
  for (final p in pins.take(5)) {
    print('    ${p.name.padRight(20)} pid=${p.pid}  mask=${p.mask}  '
        'pinned=${p.pinnedAs(rep.ecoreMask)}');
  }
  _chk(pins.isNotEmpty, '解析出进程行', '${pins.length}');
  _chk(pins.every((p) => p.pid > 0), '每行都有 pid');
  _chk(pins.every((p) => p.mask.toUpperCase().startsWith('0X')), '每行都有掩码');
  _chk(!pins.any((p) => p.name.startsWith('—')), '没有把分隔线当成数据行');
  _chk(!pins.any((p) => p.name.contains('掩码')), '没有把掩码说明行当成数据行');
  // ★ 这两行以前是被丢掉的（parsePin 里 continue 掉），界面只能写死 0x3003FC。
  _chk(rep.hasMasks, '抓到引擎实报的两个掩码',
      'ecore=${rep.ecoreMask} pcore=${rep.pcoreMask}');
  _chk(rep.ecoreDesc.isNotEmpty, '抓到能效核掩码的说明', rep.ecoreDesc);
  _chk(rep.ecoreMask != null && rep.pcoreMask != null &&
          (rep.ecoreMask! & rep.pcoreMask!) == 0,
      '能效核掩码与性能核掩码不重叠');

  // ---------------------------------------------------------------- audit
  for (final key in <String>['valorant', 'apex']) {
    _h('audit $key  →  parseAudit');
    final g = Engine.parseAudit(_read('audit_$key'), key);
    print('  名称      : ${g.name}');
    print('  主程序    : ${g.exe}');
    print('  进程名    : ${g.procs}');
    print('  推荐档位  : ${g.tier}');
    print('  说明      : ${g.note}');
    print('  配置      : ${g.configPath}');
    print('  configFound = ${g.configFound}');
    print('  检查 ${g.checks.length} 项:');
    for (final ck in g.checks) {
      print('    [${ck.verdict}] ${ck.name.padRight(16)} = ${ck.value}');
      if (ck.advice.isNotEmpty) print('        ${ck.advice}');
    }
    print('  不符合 ${g.badCount} / 注意 ${g.warnCount}');

    _chk(g.exe.isNotEmpty && g.exe.contains('\\'), '主程序路径解析正确', g.exe);
    _chk(g.tier == '竞技', '推荐档位解析正确', g.tier);
    _chk(g.configPath.isNotEmpty, '配置路径解析正确', g.configPath);
    _chk(g.configFound, 'configFound 为真');
    _chk(g.checks.length >= 5, '检查项数量合理', '${g.checks.length}');
    _chk(g.checks.every((ck) => ck.name.isNotEmpty), '每项都有名字');
    _chk(g.checks.every((ck) => ck.value.isNotEmpty), '每项都有实际值');
    _chk(
        g.checks.every((ck) =>
            <String>['符合', '不符合', '注意', '参考'].contains(ck.verdict)),
        '每项的判定都在已知集合里',
        g.checks.map((e) => e.verdict).toSet().join(' / '));
    // 文件名里带 \ 的路径不能被 | 分割影响（说明里出现过 1920x1080）
    final res = g.checks.where((ck) => ck.name.contains('渲染分辨率')).toList();
    if (res.isNotEmpty) {
      _chk(res.first.value.startsWith('1920x'),
          '渲染分辨率的值完整', res.first.value);
    }
  }

  // ---------------------------------------------------------------- bench
  _h('bench  →  parseBench');
  final benchText = _read('bench');
  final br = Engine.parseBench(benchText);
  if (br == null) {
    _chk(false, 'parseBench 返回 null —— 格式对不上，页面会永远空着',
        r'见 _verify\bench.txt');
  } else {
    print('  CPU        : ${br.cpuName}');
    print('  逻辑处理器 : ${br.logical}');
    print('  每轮       : ${br.roundSecs} 秒，共 ${br.rounds.length} 轮');
    print('  每轮数字   : ${br.rounds.map(groupNum).join('   ')}');
    print('  中位数     : ${groupNum(br.median)}');
    print('  首轮       : ${groupNum(br.first)}');
    print('  末轮       : ${groupNum(br.last)}');
    print('  首→末衰减  : ${br.dropPct.toStringAsFixed(1)}%');
    print('  频率       : ${br.startMhz.toStringAsFixed(0)} → '
        '${br.endMhz.toStringAsFixed(0)} MHz');
    print('  停泊       : ${br.parkedStart} → ${br.parkedEnd}');
    print('  判读       : ${br.verdict}');
    print('  kind=${br.kind}  throttled=${br.throttled}  '
        'slightDrop=${br.slightDrop}  totalSecs=${br.totalSecs}');

    // ★★ 本次修复的核心：engine.dart 原来的正则找的是「次/秒」，
    //    而 CLI 的 `bench`（CmdBench）输出里根本没这三个字 —— 永远 null。
    _chk(br.median > 0, '中位数解析出来了（旧正则会在这里返回 null）',
        groupNum(br.median));
    _chk(br.rounds.length >= 3, '每轮数字都收到了', '${br.rounds.length} 轮');
    _chk(br.cpuName.isNotEmpty, 'CPU 名解析正确', br.cpuName);
    _chk(br.logical > 0, '逻辑处理器数解析正确', '${br.logical}');
    _chk(br.roundSecs > 0, '每轮秒数解析正确', '${br.roundSecs}');
    _chk(br.first > 0 && br.last > 0, '首轮 / 末轮都解析出来了');
    _chk(br.dropPct >= 0 && br.dropPct < 100, '衰减百分比落在合理区间',
        '${br.dropPct}%');
    _chk(br.verdict.startsWith('判读：'), '抓到引擎给的判读', br.verdict);
    _chk(br.startMhz > 0 && br.endMhz > 0, '两次频率采样都抓到了',
        '${br.startMhz.toStringAsFixed(0)} → ${br.endMhz.toStringAsFixed(0)}');
    _chk(br.totalSecs > 0, '总耗时算得出来', '${br.totalSecs}s');

    // ★ 量级守卫：每轮行里既有吞吐量也有「100.0%」这个百分比，
    //   正则要是抓错列，会拿到 100 这种数字而不是 170 亿。
    //   这条断言专门用来拦这种「格式对不上但没报错」的静默错误。
    _chk(br.rounds.every((v) => v > br.median * 0.5),
        '每轮数字都是吞吐量级（不是 100.0% 那种百分比）',
        br.rounds.map(groupNum).join(' '));
    _chk((br.first - br.median).abs() / br.median < 0.5 &&
            (br.last - br.median).abs() / br.median < 0.5,
        '首/末与中位数同量级',
        '${groupNum(br.first)} / ${groupNum(br.median)} / ${groupNum(br.last)}');
    // 首→末衰减要跟每轮数字自洽（容 0.2% 的四舍五入）
    if (br.rounds.length >= 2) {
      final computed = (br.first - br.last) / br.first * 100;
      _chk((computed - br.dropPct).abs() < 0.5,
          '衰减与首末数字自洽（引擎报 ${br.dropPct}%，算得 '
          '${computed.toStringAsFixed(1)}%）');
    }
  }

  // ---------------------------------------------------------------- 收尾
  _h(_fail == 0 ? '全部通过' : '有 $_fail 条不通过');
  exit(_fail == 0 ? 0 : 1);
}
