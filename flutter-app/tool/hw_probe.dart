// 验证 lib/hardware.dart：本机真检测 + 用合成拓扑模拟别人机器的判定结果。
//
// 跑法（在 flutter-app 目录下）：
//   D:\flutter\bin\cache\dart-sdk\bin\dart.exe tool\hw_probe.dart
//
// 为什么要有「合成拓扑」这一段：我手上只有一台 Intel Core Ultra 7 155H，
// 没法真去插一块 Ryzen。但 assessPin 的判定只依赖两个输入 —— 本机拓扑 +
// 引擎报的掩码 —— 两者都是普通对象，所以可以造出 AMD 的拓扑来验判定逻辑，
// 证明「换台机器会走到哪条分支」。
//
// 这里刻意用相对路径 import lib/ 下的文件：这个脚本是拿 `dart run` 直接跑的
// 独立工具，不是 app 的一部分，而我们要验的正是那几个文件本身。
// ignore_for_file: avoid_relative_lib_imports

import 'dart:io';

import '../lib/hardware.dart';
import '../lib/models.dart';
import '../lib/topology.dart';

void line([String s = '']) => stdout.writeln(s);

void main() async {
  line('══════════ 一、本机真实检测 ══════════');
  final sw = Stopwatch()..start();
  final m = await detectMachine();
  sw.stop();
  line('耗时 ${sw.elapsedMilliseconds} ms');
  line('');
  line('CPU        ${m.cpuName}');
  line('厂商       ${m.cpuVendor}   (isIntel=${m.isIntel} isAmd=${m.isAmd})');
  line('标识       ${m.cpuId}');
  line('标称频率   ${m.cpuMhz} MHz');
  line('整机       ${m.shortMachine}');
  line('系列       ${m.sysFamily}');
  line('BIOS       ${m.biosVendor}');
  line('拓扑       ${m.shortCpu}');
  line('           isUniform=${m.topo.isUniform} isHybrid=${m.topo.isHybrid}');
  line('供电       有电池=${m.topo.hasBattery}  插着电=${m.topo.onAc}');
  line('');

  line('══════════ 二、本机 + 引擎真掩码 ══════════');
  _show(assessPin(
    m,
    const PinReport(ecoreMask: 0x3003FC, pcoreMask: 0xFFC03, ecoreDesc: '逻辑核 2-9、20-21', pcoreDesc: '逻辑核 0-1、10-19'),
  ));

  line('══════════ 三、合成拓扑：模拟别人的机器 ══════════');
  const engineE = 0x3003FC;
  const engineP = 0xFFC03;

  // ① Ryzen 5 7600X —— 6 核 12 线程，全大核，system mask 0xFFF
  _sim(
    'AMD Ryzen 5 7600X（6 核 12 线程，全大核）',
    _uniform(12, 6),
    engineE,
    engineP,
  );

  // ② Ryzen 7 7840HS —— 8 核 16 线程，全大核，system mask 0xFFFF
  _sim(
    'AMD Ryzen 7 7840HS（8 核 16 线程，全大核）',
    _uniform(16, 8),
    engineE,
    engineP,
  );

  // ③ Ryzen 9 7945HX —— 16 核 32 线程，掩码装得下但语义错
  _sim(
    'AMD Ryzen 9 7945HX（16 核 32 线程，全大核）',
    _uniform(32, 16),
    engineE,
    engineP,
  );

  // ④ Core i5-12450H —— 4 P核(8线程) + 4 E核(4线程) = 12 线程，另一种大小核布局
  _sim(
    'Intel Core i5-12450H（4P+4E，12 线程，另一种大小核布局）',
    _hybrid(
      [[0, 1, 2, 3, 4, 5, 6, 7], [8, 9, 10, 11]],
      8,
    ),
    engineE,
    engineP,
  );

  // ⑤ Core Ultra 7 155H 的拓扑，但引擎换一套掩码（验「对不上」那条分支）
  _sim(
    '本机拓扑 + 一套错掩码（验 mismatch 分支）',
    m.topo,
    0x00000F,
    0x3FFFF0,
  );
}

void _sim(String title, CpuTopology t, int e, int p) {
  line('── $title');
  final fake = MachineInfo(
    cpuName: title,
    cpuVendor: title.startsWith('AMD') ? 'AuthenticAMD' : 'GenuineIntel',
    cpuId: '',
    cpuMhz: 0,
    sysVendor: '某厂',
    sysModel: '某型',
    sysFamily: '',
    biosVendor: '',
    topo: t,
  );
  _show(assessPin(
    fake,
    PinReport(ecoreMask: e, pcoreMask: p, ecoreDesc: '引擎写死', pcoreDesc: '引擎写死'),
  ));
}

void _show(PinAssessment a) {
  line('判定   ${a.verdict.name}   usable=${a.usable}  kind=${a.kind}');
  line('结论   ${a.headline}');
  for (final d in a.detail) {
    line('  · $d');
  }
  line('');
}

CpuTopology _uniform(int logical, int physical) => CpuTopology(
      tiers: [CoreTier(0, [for (var i = 0; i < logical; i++) i])],
      physicalCores: physical,
      logicalCpus: logical,
      hasBattery: true,
      onAc: true,
    );

CpuTopology _hybrid(List<List<int>> classes, int physical) => CpuTopology(
      tiers: [
        for (var i = 0; i < classes.length; i++) CoreTier(i, classes[i]),
      ],
      physicalCores: physical,
      logicalCpus: classes.fold<int>(0, (a, b) => a + b.length),
      hasBattery: true,
      onAc: true,
    );
