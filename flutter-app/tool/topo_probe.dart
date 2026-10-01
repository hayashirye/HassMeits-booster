// 验证脚本：跑【真正上线的那份代码】lib/topology.dart，
// 把检测结果和引擎写死的两个掩码并排打出来，核对是否吻合。
//
// 跑法：
//   D:\flutter\bin\cache\dart-sdk\bin\dart.exe tool\topo_probe.dart
//
// 这里刻意用相对路径 import lib/ 下的文件：这个脚本是拿 `dart run` 直接跑的
// 独立工具，不是 app 的一部分，而我们要验的正是那个文件本身。
// ignore_for_file: avoid_relative_lib_imports

import 'dart:io';

import '../lib/topology.dart';

List<int> bits(int m) =>
    [for (var i = 0; i < 64; i++) if ((m & (1 << i)) != 0) i];

void main() {
  final sw = Stopwatch()..start();
  final t = detectTopology();
  sw.stop();

  stdout.writeln('检测耗时：${sw.elapsedMilliseconds} ms');
  stdout.writeln('');
  stdout.writeln('逻辑核数（Dart）= ${t.logicalCpus}    '
      '物理核数（API 记录数）= ${t.physicalCores}');
  stdout.writeln('有电池 = ${t.hasBattery}    插着电 = ${t.onAc}');
  stdout.writeln('isUniform = ${t.isUniform}    isHybrid = ${t.isHybrid}');
  stdout.writeln('');

  stdout.writeln('各档核（EfficiencyClass 的数值不代表快慢，只用于分组）：');
  for (final tier in t.tiers) {
    stdout.writeln('  class=${tier.efficiencyClass}  '
        '${tier.logicalCpus.length} 个逻辑核  '
        '0x${tier.mask.toRadixString(16).toUpperCase().padLeft(8, '0')}  '
        '->  ${tier.logicalCpus}');
  }
  stdout.writeln('');

  // 引擎 one/Core.cs:2107-2108 写死的两个常量
  const eMask = 0x3003FC;
  const pMask = 0xFFC03;

  stdout.writeln('引擎写死的掩码：');
  stdout.writeln('  ECORE_MASK 0x${eMask.toRadixString(16).toUpperCase()} '
      '-> 逻辑核 ${bits(eMask)}');
  stdout.writeln('  PCORE_MASK 0x${pMask.toRadixString(16).toUpperCase()} '
      '-> 逻辑核 ${bits(pMask)}');
  stdout.writeln('');

  // ① 装得下吗
  final overE = t.maskOverflows(eMask);
  stdout.writeln('① 掩码装得下吗（本机逻辑核 0..${t.maxLogicalIndex}）');
  if (overE) {
    final bad = bits(eMask).where((b) => b > t.maxLogicalIndex).toList();
    stdout.writeln('   ★ 装不下！ECORE_MASK 里第 $bad 位不存在。');
    stdout.writeln('     SetProcessAffinityMask 会返回 FALSE / err 87，');
    stdout.writeln('     每次压制都失败 —— 功能是个静默的空操作。');
  } else {
    stdout.writeln('   ✓ 装得下。');
  }
  stdout.writeln('');

  // ② 对得上吗
  stdout.writeln('② 掩码和本机拓扑对得上吗');
  if (t.isUniform) {
    stdout.writeln('   ★ 本机是全对称核，没有大小核之分。');
    stdout.writeln('     ECORE_MASK 在这里没有物理意义 —— 压了只是随机抢走几个核。');
  } else {
    final hitE = t.tierMatching(eMask);
    final hitP = t.tierMatching(pMask);
    if (hitE != null && hitP != null) {
      stdout.writeln('   ✓ 完全吻合：');
      stdout.writeln('     ECORE_MASK == class ${hitE.efficiencyClass}'
          '（${hitE.logicalCpus.length} 个）');
      stdout.writeln('     PCORE_MASK == class ${hitP.efficiencyClass}'
          '（${hitP.logicalCpus.length} 个）');
    } else {
      stdout.writeln('   ★ 对不上：');
      stdout.writeln('     ECORE_MASK 落在档 ${t.tiersTouchedBy(eMask)}');
      stdout.writeln('     PCORE_MASK 落在档 ${t.tiersTouchedBy(pMask)}');
      stdout.writeln('     本机各档 = ${t.tiers.map((x) => x.efficiencyClass).toList()}');
      stdout.writeln('     ⇒ 压制会把后台钉到错误的核心上。');
    }
  }
  stdout.writeln('');

  // ③ 本机正确的掩码是什么
  stdout.writeln('③ 按本机拓扑应当使用的掩码');
  if (t.isUniform) {
    stdout.writeln('   全对称，没有「能效核」可用；想要少抢游戏的核心，');
    stdout.writeln('   只能人为指定最后几个逻辑核，例如 0x'
        '${(((1 << (t.logicalCpus ~/ 2)) - 1) << (t.logicalCpus - t.logicalCpus ~/ 2)).toRadixString(16).toUpperCase()}');
  } else {
    for (final tier in t.tiers) {
      stdout.writeln('   class=${tier.efficiencyClass} -> '
          '0x${tier.mask.toRadixString(16).toUpperCase()} '
          '(${tier.logicalCpus.length} 个: ${tier.logicalCpus})');
    }
  }
}
