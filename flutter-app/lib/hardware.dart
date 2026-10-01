// 本机硬件/环境检测 —— 每次打开程序都跑一遍。
//
// 为什么要这个文件：引擎 `one\Core.cs:2107-2108` 把能效核/性能核的亲和性掩码
// 写成了两个 const（`ECORE_MASK = 0x3003FC` / `PCORE_MASK = 0xFFC03`），
// 那是**作者那台 Intel Core Ultra 7 155H 的拓扑**，源码注释里自己也写着
// 「别照抄到别的机器上」。换一台机器就会出两类问题：
//
//   ① 装不下：掩码里有本机不存在的逻辑核 ⇒ SetProcessAffinityMask 返回
//      FALSE / err 87（ERROR_INVALID_PARAMETER），每次压制都失败，
//      功能变成静默的空操作，而界面看起来一切正常。
//   ② 语义错：掩码装得下但对不上本机拓扑（例如全大核的 Ryzen）⇒ 后台被钉到
//      随意的核心上，等于从游戏手里抢核，比不压还糟。
//
// 界面改不了引擎（`pin` 不收参数，`one\Ui.cs:2643-2647`），所以这里做的是
// **如实检测 + 如实告知**：拿 FFI 问出本机拓扑，拿 `pinstat` 问出引擎实际会用的
// 掩码，两者比对，把结论直接写在界面上。
//
// 读取来源与实测耗时（本机）：
//   GetLogicalProcessorInformationEx（FFI）      7 ms   ← lib/topology.dart
//   HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0   150 ms
//   HKLM\HARDWARE\DESCRIPTION\System\BIOS                 146 ms
//   GetSystemPowerStatus（FFI）                  ~0 ms
// 合计约 300 ms，而且全部不需要管理员权限。刻意不用 WMI —— 冷启动要 1.8 秒。

import 'dart:io';

import 'models.dart';
import 'topology.dart';

/// 本机身份 + 拓扑
class MachineInfo {
  /// 「Intel(R) Core(TM) Ultra 7 155H」
  final String cpuName;

  /// 「GenuineIntel」/「AuthenticAMD」/「HygonGenuine」……
  final String cpuVendor;

  /// 「Intel64 Family 6 Model 170 Stepping 4」
  final String cpuId;

  /// 注册表里那个 `~MHz`
  final int cpuMhz;

  /// 「HONOR」/「LENOVO」/「ASUSTeK COMPUTER INC.」……
  final String sysVendor;

  /// 「DRA-XX」
  final String sysModel;

  /// 「HONOR MagicBook」
  final String sysFamily;

  final String biosVendor;

  final CpuTopology topo;

  const MachineInfo({
    required this.cpuName,
    required this.cpuVendor,
    required this.cpuId,
    required this.cpuMhz,
    required this.sysVendor,
    required this.sysModel,
    required this.sysFamily,
    required this.biosVendor,
    required this.topo,
  });

  bool get isIntel => cpuVendor.toUpperCase().contains('INTEL');
  bool get isAmd =>
      cpuVendor.toUpperCase().contains('AMD') ||
      cpuVendor.toUpperCase().contains('AUTHENTICAMD');
  bool get isArm =>
      cpuVendor.toUpperCase().contains('ARM') ||
      cpuVendor.toUpperCase().contains('QUALCOMM');

  /// 「Intel · 16 核 22 线程」
  String get shortCpu {
    final v = isIntel
        ? 'Intel'
        : isAmd
            ? 'AMD'
            : isArm
                ? 'ARM'
                : (cpuVendor.isEmpty ? '未知厂商' : cpuVendor);
    final p = topo.physicalCores > 0 ? '${topo.physicalCores} 核' : '';
    final l = '${topo.logicalCpus} 线程';
    return [v, p, l].where((s) => s.isNotEmpty).join(' · ');
  }

  /// 「HONOR DRA-XX」；厂商和型号都读不到时给空串
  String get shortMachine {
    final a = sysVendor.trim();
    final b = sysModel.trim();
    if (a.isEmpty && b.isEmpty) return '';
    if (a.isEmpty) return b;
    if (b.isEmpty) return a;
    // 「HONOR HONOR MagicBook」这种重复去掉
    if (b.toUpperCase().startsWith(a.toUpperCase())) return b;
    return '$a $b';
  }
}

/// 检测本机硬件。**不抛异常** —— 任何一项读不到就给空值，界面照常显示。
Future<MachineInfo> detectMachine() async {
  final cpu = await _regQuery(
      r'HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0');
  final bios = await _regQuery(r'HKLM\HARDWARE\DESCRIPTION\System\BIOS');

  final mhzRaw = cpu['~MHz'] ?? cpu['MHz'] ?? '';
  var mhz = 0;
  if (mhzRaw.startsWith('0x')) {
    mhz = int.tryParse(mhzRaw.substring(2), radix: 16) ?? 0;
  } else {
    mhz = int.tryParse(mhzRaw) ?? 0;
  }

  return MachineInfo(
    cpuName: cpu['ProcessorNameString'] ?? '',
    cpuVendor: cpu['VendorIdentifier'] ?? '',
    cpuId: cpu['Identifier'] ?? '',
    cpuMhz: mhz,
    sysVendor: bios['SystemManufacturer'] ?? '',
    sysModel: bios['SystemProductName'] ?? '',
    sysFamily: bios['SystemFamily'] ?? '',
    biosVendor: bios['BIOSVendor'] ?? '',
    topo: detectTopology(),
  );
}

/// 跑一次 `reg query`，把「名称    REG_SZ    值」解析成 Map。
/// 失败（键不存在 / reg.exe 不在）返回空 Map，不抛。
Future<Map<String, String>> _regQuery(String path) async {
  try {
    final r = await Process.run('reg', ['query', path]);
    if (r.exitCode != 0) return const {};
    final text = r.stdout is String ? r.stdout as String : '${r.stdout}';
    final out = <String, String>{};
    for (final line in text.split(RegExp(r'\r?\n'))) {
      // 「    ProcessorNameString    REG_SZ    Intel(R) Core(TM) Ultra 7 155H」
      final m = RegExp(r'^\s+(\S+)\s+REG_\w+\s+(.*)$').firstMatch(line);
      if (m == null) continue;
      out[m.group(1)!] = m.group(2)!.trim();
    }
    return out;
  } catch (_) {
    return const {};
  }
}

// ------------------------------------------------------------------ 结论

enum PinVerdict {
  /// 引擎的掩码和本机拓扑分毫不差
  exact,

  /// 掩码里有本机不存在的逻辑核 ⇒ 每次压制都失败（err 87）
  overflow,

  /// 本机是全对称核，没有能效核这种东西
  uniform,

  /// 掩码装得下，但落点不对（散在好几档里，或整档反了）
  mismatch,

  /// 引擎没报掩码（老版本），判不了
  unknown,
}

/// 「压制到能效核」在这台机器上到底能不能用
class PinAssessment {
  final PinVerdict verdict;

  /// 一句话结论（界面卡片标题下面那行）
  final String headline;

  /// 逐条明细
  final List<String> detail;

  /// 能不能指望这个功能
  final bool usable;

  /// 界面主色：0 正常 / 1 有问题 / 3 注意
  final int kind;

  const PinAssessment({
    required this.verdict,
    required this.headline,
    required this.detail,
    required this.usable,
    required this.kind,
  });
}

/// 把「本机拓扑」和「引擎实报的掩码」摆在一起比。
///
/// [report] 来自 `ValorantBoost.exe pinstat`（`one\Core.cs:2428-2429` 那两行）。
PinAssessment assessPin(MachineInfo m, PinReport report) {
  final t = m.topo;
  final d = <String>[];

  d.add('本机 ${t.logicalCpus} 个逻辑核、${t.physicalCores} 个物理核；'
      '${t.isUniform ? "全对称（没有大小核之分）" : "分 ${t.tiers.length} 档"}。');

  if (!report.hasMasks) {
    return PinAssessment(
      verdict: PinVerdict.unknown,
      headline: '读不到引擎的掩码，无法判断「压制」在本机是否有效。',
      detail: d,
      usable: true,
      kind: 3,
    );
  }

  final e = report.ecoreMask!;
  final p = report.pcoreMask!;
  d.add('引擎会用的能效核掩码 = 0x${e.toRadixString(16).toUpperCase()}'
      '${report.ecoreDesc.isEmpty ? "" : "（${report.ecoreDesc}）"}');
  d.add('引擎会用的性能核掩码 = 0x${p.toRadixString(16).toUpperCase()}'
      '${report.pcoreDesc.isEmpty ? "" : "（${report.pcoreDesc}）"}');

  // ① 装得下吗
  if (t.maskOverflows(e) || t.maskOverflows(p)) {
    final bad = <int>{
      ..._bitsOver(e, t.maxLogicalIndex),
      ..._bitsOver(p, t.maxLogicalIndex),
    }.toList()
      ..sort();
    d.add('★ 掩码里有本机不存在的逻辑核：$bad。'
        'SetProcessAffinityMask 会整条失败（err 87），'
        '所以「压制」现在是个静默的空操作。');
    d.add('⇒ 掩码是引擎自己报的，而它算出来装不下，说明引擎还是旧版本'
        '（掩码写死在代码里）。重装一次应用，新引擎会照本机拓扑自己算。');
    return PinAssessment(
      verdict: PinVerdict.overflow,
      headline: '本机只有 ${t.logicalCpus} 个逻辑核，引擎的掩码装不下 —— '
          '「压制后台程序」不会生效（也不会报错）。重装一次应用即可。',
      detail: d,
      usable: false,
      kind: 1,
    );
  }

  // ② 本机有没有「能效核」这个概念
  if (t.isUniform) {
    d.add('★ 本机所有核心一模一样，没有「能效核」可用。'
        '引擎的掩码虽然装得下，但压过去只是随机抢走几个核。');
    return PinAssessment(
      verdict: PinVerdict.uniform,
      headline: '本机是全对称多核（${t.logicalCpus} 线程），没有大小核之分 —— '
          '「压到能效核」在这里没有意义，只会把后台挪个地方。',
      detail: d,
      usable: false,
      kind: 3,
    );
  }

  // ③ 落点对不对
  final hitE = t.tierMatching(e);
  final hitP = t.tierMatching(p);
  if (hitE != null && hitP != null && hitE.efficiencyClass != hitP.efficiencyClass) {
    d.add('✓ 两个掩码各对应本机的一整档，互不重叠 —— '
        '压制会把后台放进 ${hitE.logicalCpus.length} 个核里，'
        '把 ${hitP.logicalCpus.length} 个核留给游戏。');
    return PinAssessment(
      verdict: PinVerdict.exact,
      headline: '引擎的掩码和本机拓扑完全吻合，压制功能正常。'
          '（能效核 ${hitE.logicalCpus.length} 个 · 性能核 ${hitP.logicalCpus.length} 个）',
      detail: d,
      usable: true,
      kind: 0,
    );
  }

  final te = t.tiersTouchedBy(e);
  final tp = t.tiersTouchedBy(p);
  d.add('★ 掩码落点不对：能效核掩码落在档 $te，性能核掩码落在档 $tp，'
      '而本机的档是 ${t.tiers.map((x) => x.efficiencyClass).toList()}。');
  d.add('⇒ 后台会被钉到不该去的核心上，可能反而和游戏抢核。');
  return PinAssessment(
    verdict: PinVerdict.mismatch,
    headline: '引擎的掩码和本机拓扑对不上，压制会把后台钉到错误的核心上。'
        '这通常说明引擎是旧版本，重装一次应用即可。',
    detail: d,
    usable: false,
    kind: 1,
  );
}

List<int> _bitsOver(int mask, int maxIndex) =>
    [for (var i = 0; i < 64; i++) if ((mask & (1 << i)) != 0 && i > maxIndex) i];
