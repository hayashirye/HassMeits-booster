// 本机 CPU 拓扑检测 —— 直接问 Windows「哪些逻辑核属于同一档」。
//
// 为什么需要它：引擎（one\Core.cs:2107-2108）把掩码写成了常量
//
//     public const long ECORE_MASK = 0x3003FCL;   // 逻辑核 2-9、20-21
//     public const long PCORE_MASK = 0xFFC03L;    // 逻辑核 0-1、10-19
//
// 那是这台机器（Intel Core Ultra 7 155H，6 P核 + 8 E核 + 2 低功耗E核）实测
// 出来的值，Core.cs:2041 的注释自己就写着「别照抄到别的机器上」。在一台
// 8 核 16 线程的 Ryzen 上，掩码里的第 20、21 位根本不存在，实测
// SetProcessAffinityMask 会整条返回 FALSE / ERROR_INVALID_PARAMETER（err 87）
// —— 也就是说「压制后台程序」在那种机器上是个静默的空操作。
//
// 掩码改不了（C# 一行都不能动），但界面可以【检测出来并如实告诉用户】。
//
// 实现要点
// ────────
// 用 GetLogicalProcessorInformationEx(RelationProcessorCore) 拿每个物理核的
// EfficiencyClass。同档的核该值相同，异档的不同。
//
// ⚠️ 不要拿 EfficiencyClass 的数值大小去推断谁快谁慢。微软文档说数字越大越
//    快，但本机实测恰好相反：class 0 = 能效核（2-9、20-21），class 1 = 性能核
//    （0-1、10-19）。Core.cs:2046 也记着同一件事。所以这里【只分组、不命名】，
//    快慢交给调用方按实际计时或按已知型号判断。
//
// 刻意不用 package:ffi —— 那是独立包，本项目约束是不引第三方依赖。缓冲区
// 改用 kernel32 的 GetProcessHeap / HeapAlloc / HeapFree。

import 'dart:ffi';
import 'dart:io';
import 'dart:typed_data';

typedef _GetInfoNative = Int32 Function(
    Uint32 relationship, Pointer<Uint8> buffer, Pointer<Uint32> returnedLength);
typedef _GetInfoDart = int Function(
    int relationship, Pointer<Uint8> buffer, Pointer<Uint32> returnedLength);

typedef _HeapAllocNative = Pointer<Uint8> Function(
    Pointer<Void> heap, Uint32 flags, IntPtr bytes);
typedef _HeapAllocDart = Pointer<Uint8> Function(
    Pointer<Void> heap, int flags, int bytes);

typedef _HeapFreeNative = Int32 Function(
    Pointer<Void> heap, Uint32 flags, Pointer<Uint8> mem);
typedef _HeapFreeDart = int Function(
    Pointer<Void> heap, int flags, Pointer<Uint8> mem);

typedef _VoidPtrNative = Pointer<Void> Function();
typedef _VoidPtrDart = Pointer<Void> Function();

typedef _BoolOutNative = Int32 Function(Pointer<Uint8> out1);
typedef _BoolOutDart = int Function(Pointer<Uint8> out1);

const int _relationProcessorCore = 0;
const int _heapZeroMemory = 0x00000008;

/// 一档核：EfficiencyClass 相同的所有逻辑核。
class CoreTier {
  /// Windows 报的原始分组号。**不要拿数值大小当快慢**，见文件头注释。
  final int efficiencyClass;

  /// 属于这一档的逻辑核编号，已升序。
  final List<int> logicalCpus;

  const CoreTier(this.efficiencyClass, this.logicalCpus);

  /// 这一档的位掩码（最多 64 个逻辑核；再多的机器这里会截断，
  /// 但那种机器本来也不在支持范围内）。
  int get mask {
    var m = 0;
    for (final c in logicalCpus) {
      if (c >= 0 && c < 64) m |= 1 << c;
    }
    return m;
  }
}

/// 一次检测的完整结果。
class CpuTopology {
  /// 按 efficiencyClass 升序排列的各档核。
  final List<CoreTier> tiers;

  /// 物理核数（GetLogicalProcessorInformationEx 返回的条目数）。
  final int physicalCores;

  /// 逻辑核数（Dart 看到的）。
  final int logicalCpus;

  /// 电池相关：true 表示有电池（多半是笔记本）。取不到就是 null。
  final bool? hasBattery;

  /// 现在插着电吗。取不到就是 null。
  final bool? onAc;

  const CpuTopology({
    required this.tiers,
    required this.physicalCores,
    required this.logicalCpus,
    this.hasBattery,
    this.onAc,
  });

  /// 全是对称核（没有大小核之分）。
  bool get isUniform => tiers.length <= 1;

  /// 有大小核。
  bool get isHybrid => tiers.length > 1;

  /// 逻辑核数 —— 判断「引擎那个掩码装不装得下」就看它。
  int get maxLogicalIndex => logicalCpus - 1;

  /// 掩码里有没有超出本机范围的位。有的话 SetProcessAffinityMask 必然失败。
  bool maskOverflows(int mask) {
    for (var i = logicalCpus; i < 64; i++) {
      if ((mask & (1 << i)) != 0) return true;
    }
    return false;
  }

  /// 某个掩码是否恰好等于【某一档】的全部逻辑核。
  /// 返回那一档，没有就返回 null。
  CoreTier? tierMatching(int mask) {
    for (final t in tiers) {
      if (t.mask == mask) return t;
    }
    return null;
  }

  /// 这个掩码落在哪几档上（用于「钉错核」的判断）。
  /// 返回命中的档号列表。
  List<int> tiersTouchedBy(int mask) {
    final out = <int>[];
    for (final t in tiers) {
      for (final c in t.logicalCpus) {
        if (c < 64 && (mask & (1 << c)) != 0) {
          out.add(t.efficiencyClass);
          break;
        }
      }
    }
    return out;
  }
}

// ────────────────────────────────────────────────────────────────────────
//  FFI 绑定（懒加载：非 Windows 或缺少函数时全部为 null）
// ────────────────────────────────────────────────────────────────────────

DynamicLibrary? _k32;
int Function(int, Pointer<Uint8>, Pointer<Uint32>)? _getInfo;
Pointer<Void> Function()? _getHeap;
Pointer<Uint8> Function(Pointer<Void>, int, int)? _heapAlloc;
int Function(Pointer<Void>, int, Pointer<Uint8>)? _heapFree;
int Function(Pointer<Uint8>)? _getPowerStatus;
bool _ffiReady = false;

void _initFfi() {
  if (_ffiReady) return;
  _ffiReady = true;
  if (!Platform.isWindows) return;
  try {
    _k32 = DynamicLibrary.open('kernel32.dll');
    _getInfo = _k32!
        .lookupFunction<_GetInfoNative, _GetInfoDart>(
            'GetLogicalProcessorInformationEx');
    _getHeap =
        _k32!.lookupFunction<_VoidPtrNative, _VoidPtrDart>('GetProcessHeap');
    _heapAlloc =
        _k32!.lookupFunction<_HeapAllocNative, _HeapAllocDart>('HeapAlloc');
    _heapFree =
        _k32!.lookupFunction<_HeapFreeNative, _HeapFreeDart>('HeapFree');
    _getPowerStatus = _k32!
        .lookupFunction<_BoolOutNative, _BoolOutDart>('GetSystemPowerStatus');
  } catch (_) {
    // 拿不到就算了，调用方会看到 null / 空结果
    _getInfo = null;
    _getHeap = null;
    _heapAlloc = null;
    _heapFree = null;
    _getPowerStatus = null;
  }
}

int _u32(Uint8List b, int off) =>
    b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24);

/// 读一次探测的核心逻辑。失败返回 null。
/// 成功时返回 (各档核, 物理核数)。
(List<CoreTier>, int)? _readTiers() {
  _initFfi();
  final getInfo = _getInfo;
  final getHeap = _getHeap;
  final heapAlloc = _heapAlloc;
  final heapFree = _heapFree;
  if (getInfo == null ||
      getHeap == null ||
      heapAlloc == null ||
      heapFree == null) {
    return null;
  }

  final heap = getHeap();
  final need = heapAlloc(heap, _heapZeroMemory, 4);
  if (need == nullptr) return null;

  Pointer<Uint8> buf = nullptr;
  Pointer<Uint8> ret = nullptr;
  try {
    // 第一次调用：缓冲传 NULL，只问需要多少字节。
    getInfo(_relationProcessorCore, nullptr, need.cast<Uint32>());
    final size = need.cast<Uint32>().value;
    if (size <= 0) return null;

    buf = heapAlloc(heap, _heapZeroMemory, size);
    if (buf == nullptr) return null;

    ret = heapAlloc(heap, _heapZeroMemory, 4);
    if (ret == nullptr) return null;

    // ★ ReturnedLength 是【输入输出】参数：输入时必须说明缓冲区有多大。
    //   HeapAlloc 带 HEAP_ZERO_MEMORY，不显式写就是 0，函数会以为缓冲区是空的
    //   而直接失败。（这个坑实测踩过：err 一直是 0，看起来像「没有错误」。）
    ret.cast<Uint32>().value = size;

    if (getInfo(_relationProcessorCore, buf, ret.cast<Uint32>()) == 0) {
      return null;
    }
    final used = ret.cast<Uint32>().value;
    final b = buf.asTypedList(size);

    // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
    //   DWORD Relationship                        @0
    //   DWORD Size                                @4
    //   union { PROCESSOR_RELATIONSHIP Processor; ... }  @8
    // PROCESSOR_RELATIONSHIP（相对 union 起点）
    //   BYTE  Flags                               @0
    //   BYTE  EfficiencyClass                     @1
    //   BYTE  Reserved[20]                        @2
    //   WORD  GroupCount                          @22
    //   GROUP_AFFINITY GroupMask[]                @24
    // GROUP_AFFINITY = KAFFINITY Mask(8) + WORD Group + WORD Reserved[3] = 16 字节
    // ⇒ 绝对偏移：EfficiencyClass = 9，GroupCount = 30，GroupMask = 32
    const int offEff = 9;
    const int offGroupCount = 30;
    const int offGroupMask = 32;
    const int groupAffinitySize = 16;

    final byClass = <int, List<int>>{};
    var records = 0;
    var off = 0;
    while (off + 8 <= used) {
      final rel = _u32(b, off);
      final entrySize = _u32(b, off + 4);
      if (entrySize <= 0 || off + entrySize > used) break;

      if (rel == _relationProcessorCore) {
        records++;
        final eff = b[off + offEff];
        final groupCount =
            b[off + offGroupCount] | (b[off + offGroupCount + 1] << 8);
        final cpus = <int>[];
        for (var g = 0; g < groupCount; g++) {
          final base = off + offGroupMask + g * groupAffinitySize;
          if (base + groupAffinitySize > used) break;
          var mask = 0;
          for (var k = 0; k < 8; k++) {
            mask |= b[base + k] << (8 * k);
          }
          final group = b[base + 8] | (b[base + 9] << 8);
          for (var i = 0; i < 64; i++) {
            if ((mask & (1 << i)) != 0) cpus.add(group * 64 + i);
          }
        }
        byClass.putIfAbsent(eff, () => <int>[]).addAll(cpus);
      }
      off += entrySize;
    }

    final out = <CoreTier>[];
    for (final e in byClass.entries) {
      final l = e.value..sort();
      out.add(CoreTier(e.key, List<int>.unmodifiable(l)));
    }
    out.sort((a, c) => a.efficiencyClass.compareTo(c.efficiencyClass));
    // records 就是物理核数：GetLogicalProcessorInformationEx 每个物理核返回一条记录
    // （记录里带着它那几个逻辑核的掩码）。所以物理核数不能拿逻辑核数去数。
    return (out, records);
  } catch (_) {
    return null;
  } finally {
    if (ret != nullptr) heapFree(heap, 0, ret);
    if (buf != nullptr) heapFree(heap, 0, buf);
    heapFree(heap, 0, need);
  }
}

/// 读电池状态。返回 (hasBattery, onAc)，取不到就是 (null, null)。
(bool?, bool?) _readPower() {
  _initFfi();
  final f = _getPowerStatus;
  if (f == null || _heapAlloc == null || _heapFree == null || _getHeap == null) {
    return (null, null);
  }
  final heap = _getHeap!();
  final p = _heapAlloc!(heap, _heapZeroMemory, 16);
  if (p == nullptr) return (null, null);
  try {
    if (f(p) == 0) return (null, null);
    // SYSTEM_POWER_STATUS
    //   BYTE ACLineStatus      @0    0=离线 1=在线 255=未知
    //   BYTE BatteryFlag       @1    128=没有电池
    //   BYTE BatteryLifePercent@2
    //   BYTE SystemStatusFlag  @3
    //   DWORD BatteryLifeTime  @4
    //   DWORD BatteryFullLifeTime @8
    final ac = p.asTypedList(16)[0];
    final flag = p.asTypedList(16)[1];
    final hasBat = (flag & 128) == 0;
    return (hasBat, ac == 1 ? true : (ac == 0 ? false : null));
  } catch (_) {
    return (null, null);
  } finally {
    _heapFree!(heap, 0, p);
  }
}

/// 检测本机 CPU 拓扑。这是入口。
///
/// 全程不抛异常：任何一步失败都退化成「只有一个全对称档」，
/// 界面据此显示「检测失败」，而不是崩掉。
CpuTopology detectTopology() {
  final logical = Platform.numberOfProcessors;
  List<CoreTier>? tiers;
  var physical = 0;
  try {
    final r = _readTiers();
    if (r != null) {
      tiers = r.$1;
      physical = r.$2;
    }
  } catch (_) {
    tiers = null;
  }

  bool? hasBat;
  bool? onAc;
  try {
    final p = _readPower();
    hasBat = p.$1;
    onAc = p.$2;
  } catch (_) {
    hasBat = null;
    onAc = null;
  }

  if (tiers == null || tiers.isEmpty) {
    // 退化：当作全对称的一档，掩码 = 全部逻辑核
    return CpuTopology(
      tiers: [CoreTier(0, [for (var i = 0; i < logical; i++) i])],
      physicalCores: 0,
      logicalCpus: logical,
      hasBattery: hasBat,
      onAc: onAc,
    );
  }

  return CpuTopology(
    tiers: tiers,
    physicalCores: physical,
    logicalCpus: logical,
    hasBattery: hasBat,
    onAc: onAc,
  );
}
