# 游戏 CPU 高频优化器 · Flutter 版

这是**界面层用 Flutter 重写**的一版。它**不替换**原来的 C# 版 —— 两个是独立的程序，
原来的 `D:\test\valorant-cpu-boost\ValorantBoost.exe` 和计划任务 `ValorantBoostWatcher`
一行都没动。

## 它是怎么工作的

**Flutter 只做界面，干活还是原来那个 C# 引擎。**

```
Flutter UI  ──CLI──▶  ValorantBoost.exe  ──▶  Win32 电源 / 亲和性 API
（本目录）            （D:\test\valorant-cpu-boost\）
```

没有用 `dart:ffi` 重写引擎，是有意的：引擎那 2200 多行 C# 踩过一堆坑
（Vanguard 保护的进程上 `MainModule` 要 789 ms 而且只返回空串、本机能效核掩码是实测出来的
`0x3003FC`、计划任务的电池标志会让守护静默不启动、每 4 秒起一个 `powercfg.exe` 会把
守护开销顶到 390 ms/分钟）。用 FFI 重写等于把这些坑重踩一遍，而且换不来任何好处 ——
引擎本来就是无头的，CLI 契约很干净。

**所以：跑这个 Flutter 版之前，必须保证 `ValorantBoost.exe` 在。**

启动时它会按这个顺序找引擎（`lib\engine.dart:36` 的 `candidates()`）：

1. `%VALORANT_BOOST_HOME%\ValorantBoost.exe` —— 环境变量显式指定，绿色部署最省事
2. **和自己同一个文件夹** —— 最自然的「解压就能用」
3. **往上 1…6 级目录** —— 覆盖 `flutter-app\build\windows\x64\runner\Release\`
   这种深布局（Release 往上 5 级正好是仓库根），也覆盖「引擎和 flutter-app 平级」
4. `C:\ProgramData\ValorantBoost\ValorantBoost.exe` —— `install.exe` 装出来的位置
5. `%APPDATA%\ValorantCpuBoost\ValorantBoost.exe` —— 引擎自己的兜底目录
6. `%LOCALAPPDATA%\ValorantBoost\ValorantBoost.exe`
7. `D:\test\valorant-cpu-boost\ValorantBoost.exe` —— **我开发时用的老位置，刻意放最后**

★★ 第 7 条**不许往前挪**（`lib\engine.dart:32-35` 有注释专门盯着这件事）：它在我这台
机器上永远命中，于是这个 bug 从没暴露过；但把整个文件夹拷给别人，他的机器上没有
`D:\test` 这一层，界面就会显示「没找到引擎」，五个页面全空。放在最后，它只在别处
都找不到时才轮到，不会「抢答」出一个并不存在的路径。

全找不到就在左下角显示「没找到引擎」，其余界面仍能打开，只是数据都是空的。

## 先装工具链

本机实测：**没有** Flutter、**没有** Dart、**没有** Visual Studio 的 MSVC 工具链
（`cl.exe` / `vswhere.exe` / `devenv.exe` 全盘都搜不到，Windows SDK 也没装好）。
**装了 VS Code**（`C:\Users\hayashi\AppData\Local\Programs\Microsoft VS Code\`），
但那**不能替代** Visual Studio —— 见下面第 2 条。

要装两样：

### 1. Flutter SDK（约 1 GB 下载 / 3 GB 磁盘）

> **★ winget 里没有 Flutter。** 实测 `winget install --id Google.Flutter` 会报
> `No package found matching input criteria`（`Google.Flutter` / `Google.FlutterSDK` /
> `Flutter.Flutter` 三个 ID 都不存在）。winget 里只有 `Google.DartSDK`，那是**纯 Dart**，
> 不含 Flutter 框架，**装它跑不了 `flutter analyze`**（`package:flutter/material.dart` 会找不到）。
> 所以只能用下面两种官方方式之一。

**方式 A：git clone（推荐，git 本机已有 2.55.0）**

```powershell
git clone https://github.com/flutter/flutter.git -b stable --depth 1 D:\flutter
```

`--depth 1` 只取最新一次提交，比完整克隆小得多。放 `D:\flutter`（**不要放需要管理员权限的
`C:\` 根目录**，也**不要放带空格的路径**）。

**方式 B：下载 ZIP**

到 <https://docs.flutter.dev/get-started/install/windows> 下 stable 的 zip，
解压到 `D:\flutter`（解压后确认 `D:\flutter\bin\flutter.bat` 在）。

### 1b. ★★★ 设国内镜像 —— 整篇 README 里最省时间的一条

实测（本机，2026-10-01）：

| 下载源 | 速度 | 下完 Dart SDK（209.7 MB） |
|---|---|---|
| `storage.googleapis.com`（默认，走 BITS） | 1.8 MB/分 | 约 **105 分钟** |
| `storage.googleapis.com`（直连） | 5.6 MB/分 | 约 38 分钟 |
| **`storage.flutter-io.cn`（官方国内镜像）** | **196 MB/分** | **26 秒** |

**镜像比默认源快两个数量级。** 不设它，第一次 `flutter --version` 会卡在一个你以为
死掉了的下载上 —— 注意 `flutter doctor` 里那行 `[√] Network resources` 会骗你，
它只测连通性，不测速度。

```powershell
# 写进用户环境变量（做一次即可）
[Environment]::SetEnvironmentVariable('FLUTTER_STORAGE_BASE_URL', 'https://storage.flutter-io.cn', 'User')
[Environment]::SetEnvironmentVariable('PUB_HOSTED_URL',           'https://pub.flutter-io.cn',           'User')
# 当前这个终端里立刻生效
$env:FLUTTER_STORAGE_BASE_URL = 'https://storage.flutter-io.cn'
$env:PUB_HOSTED_URL           = 'https://pub.flutter-io.cn'
```

**★ 还有一个坑：`--depth 1` 克隆出来的仓库是 shallow 的、没有 tag，而 `flutter --version`
会去 `git fetch --tags` 补 —— 从这台机器连 github.com 拉 tags 是彻底卡死的。**
实测 `.git` 目录 25 秒增长 **0.00 MB**，最后报：

```
Command exited with code 128: fetch --tags
Standard error: error: RPC failed; curl 56 schannel: server closed abruptly (missing close_notify)
error: 45899 bytes of body are still expected
fetch-pack: unexpected disconnect while reading sideband packet
fatal: early EOF
```

**用 `--no-version-check` 跳过它**（版本号本身没问题 —— 第一次跑完
`D:\flutter\bin\cache\flutter.version.json` 就写好了，里面有 `frameworkVersion` 和
`engineRevision`）：

```powershell
flutter --no-version-check --version
flutter --no-version-check pub get
flutter --no-version-check analyze
flutter --no-version-check build windows --release
```

**然后把它加进 PATH，并触发 Dart SDK 下载：**

```powershell
# 加进当前用户的 PATH（只做一次，之后重开终端生效）
$old = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($old -notlike '*\flutter\bin*') {
  [Environment]::SetEnvironmentVariable('Path', "$old;D:\flutter\bin", 'User')
}

# 当前这个终端里先手动加上，好立刻能用
$env:Path += ';D:\flutter\bin'

flutter --version
```

**第一次跑 `flutter --version` 会花几分钟** —— 它要下载 Dart SDK（约 200 MB）和
Windows 引擎产物（约 500 MB），屏幕上会有一段进度条。这是正常的，不是卡住了。

然后是：

```powershell
flutter doctor
```

### 2. MSVC C++ 工具链（**这个绕不过去**）

Flutter 的 Windows 桌面程序分两半：**Dart 那半**由 Flutter SDK 自带的编译器搞定，
**不需要 Visual Studio**；但**外壳那半**（`windows\runner\*.cpp`，Flutter 自动生成的窗口宿主）
必须用 **MSVC 的 `cl.exe` + Windows SDK** 编译链接。

**VS Code 代替不了它** —— VS Code 是编辑器，不带编译器。它可以编辑 Dart、也可以用
Flutter 扩展去驱动 `flutter run`，但真正编 C++ 的那一步还是会报
`Unable to find suitable Visual Studio toolchain`。

两个装法，**推荐第一个**（更小，而且和 VS Code 是绝配）：

```powershell
# A. 只装生成工具（约 3–5 GB）—— 够 Flutter 用，不带 IDE
winget install --id Microsoft.VisualStudio.2022.BuildTools -e `
  --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"

# B. 装完整的 Visual Studio Community（约 7 GB）—— 想用 VS 本体写代码才需要
winget install --id Microsoft.VisualStudio.2022.Community -e `
  --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.NativeDesktop --includeRecommended"
```

两个都要**管理员权限**。装完重开终端再跑一次：

```powershell
flutter doctor
```

直到 `Visual Studio - develop Windows apps` 那行变成绿色的勾。

> 只想**先看看代码能不能过静态检查**（不装 MSVC 也行）：
> ```powershell
> cd D:\test\valorant-cpu-boost\flutter-app
> flutter pub get
> flutter analyze
> ```
> `flutter analyze` **不需要** MSVC —— 它只做静态分析。这一步能抓出所有语法/类型错误，
> 但不会产出 exe。

## 跑起来

### 第 0 步：生成 `windows\` 脚手架（**必须做，否则跑不起来**）

这个目录里只有 **Dart 那一半**（`lib\` + `pubspec.yaml`）。Flutter 的 Windows 外壳
（`windows\runner\*.cpp`，就是前面说的「必须用 MSVC 编的那半」）是 `flutter create`
生成的，仓库里不带。**少了它，`flutter run -d windows` 会直接报
`"windows" is not a supported platform for this project`。**

```powershell
cd D:\test\valorant-cpu-boost\flutter-app

# 保险起见先把 pubspec 备份一份 —— flutter create 一般不动它，但值得花这一秒
Copy-Item pubspec.yaml pubspec.yaml.bak

flutter create --platforms=windows --project-name valorant_boost .
```

跑完你会多出 `windows\`、`.metadata`、`.gitignore`、`test\` 这些东西。

> **★ 跑完立刻做一件事：删掉 `test\widget_test.dart`。**
> `flutter create` 生成的那个测试文件是给「新建的空项目」写的，它 `import` 的是
> `main.dart` 里那个默认的 `MyApp`。我们这个 `main.dart` 里没有 `MyApp`，
> 所以留着它 `flutter analyze` 会**多报一个假错误**（说 `MyApp` 未定义）——
> 那是脚手架的问题，不是代码的问题。
>
> ```powershell
> Remove-Item test\widget_test.dart -ErrorAction SilentlyContinue
> ```

### 第 1 步：拉依赖 + 跑

```powershell
flutter pub get
flutter run -d windows
```

现在 `pubspec.yaml` 的 `dependencies` 里**只有 Flutter 本身，零第三方包** ——
少一个包就少一个「拉不下来导致 `pub get` 整个失败」的风险。

### 第 2 步：先自测，再打包（**不用等 MSVC 就能跑**）

这套源码带两个自测脚本，它们**只需要 Dart VM**，不需要 MSVC、不需要 GUI ——
所以 C++ 工具链还没装好之前，就能确认「逻辑是对的」。

```powershell
# ① 解析契约：把真实抓下来的引擎输出喂给解析函数，看界面会渲染成什么
dart run tool\parse_test.dart

# ② 桥接契约：真的启动 ValorantBoost.exe，验证 --out 文件 / UTF-8 / 超时
dart run tool\bridge_test.dart
```

两个脚本都是**全绿才 exit 0**，有一条不通过就 exit 1。

> **为什么要写这两个**：`flutter analyze` 只能证明「能编译」，证明不了
> 「解析得对」。而 Flutter 版跟 C# 引擎之间唯一的接口就是
> `ValorantBoost.exe <命令> --out <文件>` 这条命令行 —— 桥上的坑
> （编码、超时、带空格的进程名）analyze 一个都看不见。
>
> 实测这两个脚本已经抓到 **3 个真 bug**：
> ① `bench` 的解析正则找的是 GUI 才有的「次/秒」，而 CLI 的 `bench`
> 根本没有这三个字（页面会永远空着）；
> ② 后台进程名里带空格的（`DeepSeek Harness`）会被切成两半，
> 而它恰好是占单核最高的一行；
> ③ 频率采样两行写法不一致（`估算频率 4,466` vs `估算 4,634`），
> 用同一个正则硬套会让前一个恒为 0。

`bridge_test.dart` 全程只跑**只读**命令，不会改电源方案、不会装计划任务。

跑 ① 之前需要先用真的引擎生成样本：

```powershell
$exe = 'D:\test\valorant-cpu-boost\ValorantBoost.exe'
$dir = 'D:\test\valorant-cpu-boost\_verify'
New-Item -ItemType Directory -Force $dir | Out-Null
& $exe check          --out "$dir\check.txt"          | Out-Null
& $exe bgcpu          --out "$dir\bgcpu.txt"          | Out-Null
& $exe pinstat        --out "$dir\pinstat.txt"        | Out-Null
& $exe audit valorant --out "$dir\audit_valorant.txt" | Out-Null
& $exe audit apex     --out "$dir\audit_apex.txt"     | Out-Null
& $exe bench          --out "$dir\bench.txt"          | Out-Null
```

（`bench` 跑 5 轮 × 6 秒、约 40 秒；`bgcpu` 采样 6 秒。）

## 打包成 exe

```powershell
flutter build windows --release
```

产物在 `build\windows\x64\runner\Release\`。

**注意：这不是单文件。** Flutter 的 Windows 产物是一个文件夹：

```
Release\
  valorant_boost.exe        ← 你的程序
  flutter_windows.dll       ← 15–20 MB
  data\
    icudtl.dat              ← 约 10 MB
    ...
```

整个文件夹大概 25–40 MB。这一点和 C# 版（单个 120 KB 的 exe）差别很大 ——
如果你想让**开机自启动的守护**也换成这一版，得改计划任务指向这个文件夹里的 exe，
而且文件夹不能随便挪。

**建议：守护继续用 C# 版，Flutter 版只当日常操作界面。** 两者共用同一份
`boost-log.txt` 和备份文件，可以同时开着。

## 换台机器也能用：本机硬件检测

引擎（C# 版）把「能效核掩码」写成了两个常量 —— `one\Core.cs:2107-2108`：

```csharp
public const long ECORE_MASK = 0x3003FCL;   // 逻辑核 2-9、20-21
public const long PCORE_MASK = 0xFFC03L;    // 逻辑核 0-1、10-19
```

那是作者那台 **Intel Core Ultra 7 155H** 实测出来的拓扑，`one\Core.cs:2041`
的注释自己就写着「别照抄到别的机器上」。换台机器会出两类问题：

| 机器 | 会发生什么 |
|---|---|
| Ryzen 5 7600X（12 线程） | 掩码里第 12–21 位不存在 ⇒ `SetProcessAffinityMask` 返回 `err 87`，**每次压制都失败，但界面毫无提示** |
| Ryzen 9 7945HX（32 线程） | 掩码装得下，但全是大核 ⇒ 后台被钉到随意的核心上，**等于从游戏手里抢核** |
| 本机 Ultra 7 155H | 分毫不差 |

**实测结论：掩码里只要有「一位」超出本机逻辑核范围，`SetProcessAffinityMask`
就整条失败**（`ERROR_INVALID_PARAMETER` / 87），不是部分生效。测法是把
`0x003003FC`、`0x000FFC03`、`0x003FFFFF` 和两个「多一位」的掩码各试一遍。

所以 Flutter 版**每次打开程序都重新检测一遍**本机环境，把结论直接写在体检页顶上：

```
Intel(R) Core(TM) Ultra 7 155H                        [大小核 · 2 档]
Intel · 16 核 · 22 线程 · HONOR DRA-XX · 插着电        [压制：可用]
```

检测到不可用时，游戏专项页的「后台程序压制」下面会多出一行琥珀色说明，
不再只说「现在没有进程被压到能效核」这种在 AMD 上根本不成立的话。

### 检测什么、怎么测

| 项 | 来源 | 耗时 |
|---|---|---|
| 哪些逻辑核同档（大小核分组） | `GetLogicalProcessorInformationEx(RelationProcessorCore)`，走 `dart:ffi` | **7 ms** |
| CPU 名字 / 厂商 / 标识 | `HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0` | 150 ms |
| 整机厂商 / 型号 | `HKLM\HARDWARE\DESCRIPTION\System\BIOS` | 146 ms |
| 有没有电池 / 插没插电 | `GetSystemPowerStatus`，走 `dart:ffi` | ~0 ms |
| 引擎实际会用的掩码 | `ValorantBoost.exe pinstat` 输出的前两行 | 一次进程调用 |

合计约 **300 ms**，不需要管理员权限。**刻意不用 WMI** —— `Win32_ComputerSystem`
冷启动要 1.8 秒，而上面两条注册表加起来才 0.3 秒。

代码在 `lib\topology.dart`（FFI 那部分）和 `lib\hardware.dart`（身份 + 判定）。

### ⚠️ `EfficiencyClass` 的数值方向

**数值越大越快，与微软文档一致。** 本机实测：`class 1` = 性能核（逻辑核
0-1、10-19，12 个），`class 0` = 能效核（逻辑核 2-9、20-21，10 个）。

`one\Core.cs:2046` 原来那句注释写着「实测它把 P 核标成 1、E 核标成 0，跟微软
文档写的正好相反」—— **那是记错了**。名字叫 `EfficiencyClass` 容易让人以为
「越像能效核数字越大」，实际它的含义是「相对性能档位」。原注释已订正。

即便如此，`lib\topology.dart` 仍然**只分组、不命名**：谁快谁慢交给调用方判断。
这样即使将来某个平台方向真的反过来，判定逻辑也只在一处。

### 四种判定

| `PinVerdict` | 含义 | 界面显示 |
|---|---|---|
| `exact` | 两个掩码各对应本机一整档、互不重叠 | 绿色「压制：可用」 |
| `overflow` | 掩码里有本机不存在的逻辑核 | 红色「压制：掩码装不下」 |
| `uniform` | 全对称核，没有能效核这个概念 | 琥珀「压制：无大小核」 |
| `mismatch` | 装得下但落点不对 | 红色「压制：掩码对不上」 |

### 验证方法（不需要真的插一块 AMD）

`assessPin` 只依赖两个普通对象（本机拓扑 + 引擎掩码），所以可以造出 AMD 的
拓扑来验判定逻辑：

```powershell
D:\flutter\bin\cache\dart-sdk\bin\dart.exe tool\hw_probe.dart
```

它会先打印本机真实检测结果，再用合成拓扑跑一遍 Ryzen 5 7600X / Ryzen 7 7840HS /
Ryzen 9 7945HX / i5-12450H 四种机器，看分别落到哪条分支。

### ★ 现在引擎会自己算掩码了

原来 `one\Core.cs:2107-2108` 是两个写死的常量：

```csharp
public const long ECORE_MASK = 0x3003FCL;   // 逻辑核 2-9、20-21
public const long PCORE_MASK = 0xFFC03L;    // 逻辑核 0-1、10-19
```

这是**这台机器的拓扑**（注释里自己就写着「别照抄到别的机器上」）。换台机器就出事：

- **8 核 16 线程的 Ryzen**：`0x3003FC` 的第 20、21 位不存在 ⇒
  `SetProcessAffinityMask` 整条失败（`ERROR_INVALID_PARAMETER`，err 87）⇒
  压制变成**静默的空操作**，而界面看起来一切正常。
- **32 线程的全大核机器**（如 Ryzen 9 7945HX）：掩码装得下但语义错 ⇒
  把后台钉到随意的核心上，等于从游戏手里抢核。

**实测确认**：掩码里只要有一位超出本机逻辑核范围，整条调用就失败。

现在这两个常量变成了属性，值由 `CpuTopo` 在运行时按
`GetLogicalProcessorInformationEx` 的结果现算（`one\Core.cs:2138-2421`）：

```csharp
public static long ECORE_MASK { get { return CpuTopo.EMask; } }
```

判定规则：**`EfficiencyClass` 数值最大的那一档是 P 核，其余各档全部 OR 成 E 核**；
只有 1 档（全对称）、跨处理器组、或算出来为空/相等时判「不可用」，
此时两个掩码置 0，并且**绝不调用** `SetProcessAffinityMask`。

引擎在自己的构建脚本 `one\build-engine.ps1` 里（见下节）。pinstat 的输出格式
一个字都没改，只多了一行 `大小核判定 = ...` —— 界面读它来告知用户。

### 为什么界面当初「改不了引擎」

`pin` 子命令**不接收参数**（`one\Ui.cs:2643-2647`），掩码又是常量，
所以界面没法把算好的掩码传进去。现在改成引擎自己算，这条限制就不存在了。

界面侧仍然只做**检测并如实告知**：把引擎实报的掩码和本机拓扑摆在一起比，
吻合就显示绿色「压制：可用」，不吻合就说明原因。

## 重新编译引擎（`ValorantBoost.exe`）

### ★ 原来的构建命令从来没存成脚本

仓库里有两套互不相干的代码，别搞混：

| 源码 | 产物 | 构建脚本 |
|---|---|---|
| `src\ValorantCpuBoost.cs` + `src\MainForm.new.cs` | `ValorantCpuBoost.exe`（73728 字节） | `build.ps1` |
| **`one\Core.cs` + `one\Ui.cs`** | **`ValorantBoost.exe`（120832 字节）** | **原来没有** |

`ValorantBoost.exe` 是真正在跑的那个引擎（守护用的就是它），但全库搜 `csc`、
`/target:`、`one\Core.cs` 都找不到它的构建命令；`fix-encoding.ps1` 的 `$targets`
也只包含 `src\` 那三个文件。**这次补上了 `one\build-engine.ps1`。**

```powershell
powershell -ExecutionPolicy Bypass -File one\build-engine.ps1
powershell -ExecutionPolicy Bypass -File one\build-engine.ps1 -Out D:\somewhere\ValorantBoost.exe
powershell -ExecutionPolicy Bypass -File one\build-engine.ps1 -Compiler fx     # 强制用系统自带 csc
```

默认输出到 `<仓库>\engine-build\ValorantBoost.exe`，**拒绝写入出厂位置**
（免得手滑覆盖掉正在跑的引擎）。日志落在同目录的 `build-engine.log`。

输出目录刻意留在仓库**里面**：原来写死的是 `D:\test\vcb-engine-build\`，
结果每编译一次就在仓库旁边重新拉出一个目录，把「`D:\test` 下只有一个文件夹」破坏掉。

★★ 编译完**不会**自动部署。`ValorantBoost.exe` 一共有四份，要一起换：

| 位置 | 用途 |
|---|---|
| `<仓库>\engine-build\ValorantBoost.exe` | 构建产物，刚出炉的那个 |
| `<仓库>\ValorantBoost.exe` | 仓库里的成品（构建脚本拒绝直接写它，得手动拷） |
| `C:\ProgramData\ValorantBoost\ValorantBoost.exe` | **守护任务真正在跑的那份** |
| `%LOCALAPPDATA%\ValorantCpuBoost\ValorantBoost.exe` | 安装目录里的那份 |

★ 拷之前必须先停守护 —— 正在运行的 exe 是被锁住的，`Copy-Item` 会报占用：

```powershell
Stop-Process -Name ValorantBoost -Force            # 停守护，腾出文件锁
Copy-Item <新的> C:\ProgramData\ValorantBoost\ValorantBoost.exe -Force
schtasks /run /tn ValorantBoostWatcher             # 重新拉起来
```

编译参数：`/nologo /noconfig /target:winexe /platform:anycpu /optimize+ /codepage:65001`，
引用恰好这五个就够（一次过，零 CS0246）：

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\ 下的
System.dll / System.Core.dll / System.Drawing.dll / System.Windows.Forms.dll / System.Management.dll
```

### ★★ 怎么确认重编出来的和出厂的是同一个东西

跑 `pinstat`，对比输出。**未改动源码时应当逐字节相同**：

```
2331 字节，SHA-256 = 14051A619281066CA2F06140ABE0ED45F57BCB454B5015EB3EFF838C9F0D0A56
```

### ★★ `csc` 的两个坑

**① `/out` 必须写在源文件之前。** 系统自带 csc 反序会报：

```
fatal error CS2022: 选项"/out"和"/target"不能出现在源文件之前
```

Roslyn 不挑这个顺序 —— 所以这个 bug **只在没装 Visual Studio 的机器上才暴露**。
`build-engine.ps1` 已按正确顺序写，并实现了「首选编译器失败就换下一个」。

**② 尺寸能指纹出用的是哪个编译器。**

| 编译器 | 未改动源码编出来 | 改了掩码之后 |
|---|---|---|
| 系统自带 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（4.8.9221.0） | **120832** ← 与出厂尺寸一致 | 125440 |
| Roslyn（VS 自带，5.1000.26.38003） | 119296 | 123904 |

出厂的是 120832，**所以历史构建用的是系统自带 csc，不是 Roslyn**。

### ★ 中文源码不要写 BOM 也不要写中文注释

`one\Core.cs` 和 `one\Ui.cs` 都**没有 BOM**却含中文，靠 `/codepage:65001` 才能编对。
`build-engine.ps1` 本身是纯 ASCII 的（0 个字节 >127）—— 理由和
`make-portable.ps1` 一样：PowerShell 5.1 会用系统 ANSI 代码页（中文机器上是 GBK）
解码没有 BOM 的 `.ps1`，任何非 ASCII 字节都会被读成乱码。

**另外，`windows\runner\main.cpp` 里不要写中文注释** —— MSVC 按代码页 936 读无 BOM
文件会报 `warning C4819`，而这个项目 warnings-as-errors，直接编译失败
（`error C2220`）。窗口标题用 `\u6E38\u620F...` 转义写。

### ★ .NET 的字符串不一定在偶数偏移

排查「中文是不是丢了」的时候，**要同时查奇数和偶数对齐**。.NET 的 `#US` 堆里
字符串起始偏移可以是奇数：

```powershell
# 双字节对齐搜索（UTF-16LE）
$pat = [Text.Encoding]::Unicode.GetBytes('设亲和性失败')
```

只查一种对齐会漏掉一半字面量。这次基线阶段就因此误判过一次「中文丢了」。

## 仓库长什么样

清理过一轮：**原来 799 MB，现在 51 MB。源码全留（还能重新编译），成品归到 `release\`
一个文件夹里。**

```
D:\test\valorant-cpu-boost\              51.3 MB
├── src\                    旧版引擎源码（ValorantCpuBoost.exe）
├── one\                    现役引擎源码：Core.cs / Ui.cs / build-engine.ps1
├── installer\              安装器 + 卸载器源码：Installer.cs / Uninstaller.cs
│                           / Vcb.cs / Launcher.cs / *.manifest
├── backup\                 引擎的电源方案备份（★ 运行期数据，别删）
├── engine-backup-87E5932E\ 回滚用的引擎备份
├── boost-log.txt           引擎日志
├── boost-backup.txt        「还原初始设置」的还原点
├── auto-backup.txt
├── ValorantBoost.exe       现役引擎成品
├── flutter-app\            Flutter 界面：lib\ tool\ windows\ pubspec.*
│   └── README.md           这份文档
└── release\                ★ 交付物都在这里
    ├── install.exe                 11,924,992  约 11.4 MB
    ├── uninstall.exe                   13,312  可单独发出去
    ├── portable.zip                11,881,417  约 11.3 MB
    ├── portable\                     27.36 MB  17 个文件
    └── README.md                     这份文档的副本
```

**删掉的**：

| 目录 / 文件 | 大小 | 是什么 |
|---|---|---|
| `electron-app\` | 380.4 MB | 换 Flutter 之前的 Electron 版，**整个废弃**（`node_modules` 就占 380.17 MB） |
| `zzz_probe\` | 334.3 MB | 当年查 Flutter 刷屏报错建的模板对照实验 |
| `flutter-app\windows\flutter\ephemeral\` | 261.6 MB | Flutter 引擎缓存 |
| `flutter-app\build\` | 49.4 MB | 构建产物 |
| `flutter-app\.dart_tool\` | 33.2 MB | Dart 构建缓存 |
| `portable\payload.zip` | 11.3 MB | 和 `portable.zip` 字节完全相同 |
| `one\*.png` / `*.txt` / `*.xml` | 9.8 MB | 测试残留（最大单个 `one\tray-full.png` 5.18 MB） |
| `flutter-app\*.png` | 1.7 MB | 开发期截图（**README 里没有任何 .png 引用**） |
| 根目录 22 个零散文件 | — | 一次性的验证脚本、`_verify\` 等 |

**★ 代价**：删掉 `build\` 和 `windows\flutter\ephemeral\` 之后，再想重建绿色版必须先跑
一次完整的 `flutter build windows --release`（约 30 秒，外加重新生成约 260 MB 引擎缓存）。
想省这一步，就别删 `ephemeral\`。

**★ 改过的路径**：`flutter-app\tool\make-portable.ps1:37` 的 `$Out`、
`make-installer.ps1` 的 `$Portable` / `$Out` 现在默认指向 `release\` —— 仓库顶层不会再
冒出 `portable\` 和 `dist\`。`make-installer.ps1` 的中间产物（`payload.zip`、
`run-ui.exe`）挪到了 `%TEMP%`：原来写在输出目录里，而 `payload.zip` 和 `portable.zip`
字节完全相同（都是 11,881,417），等于在交付目录里放两份 11.3 MB。
`make-installer.ps1` 调 `make-portable.ps1` 时补了 `-Zip`，一条命令同时刷新
`release\portable\` 和 `release\portable.zip`。

**★ 排查这条清理时踩的坑**：清理脚本里**不能定义名为 `Kill` 的函数** ——
PowerShell 的命令解析顺序是 **别名 > 函数 > cmdlet**，而 `kill` 是 `Stop-Process`
的内置别名，所以自定义的 `Kill` 永远被盖掉，报
`Stop-Process : 无法绑定参数"InputObject"`。改名 `Remove-Junk` 即可。

## 能不能拷到别人的电脑上用

**直接拷 `Release\` 文件夹不行**，会缺两样东西。用一条命令补齐：

```powershell
powershell -ExecutionPolicy Bypass -File tool\make-portable.ps1 -Zip
```

产物在 `<仓库根>\release\portable\`（约 27 MB，17 个文件），加 `-Zip` 再压一个
`<仓库根>\release\portable.zip`。**两个都写进 `release\`，仓库顶层不会再冒出
`portable\` 和 `dist\`** —— 见下面「仓库长什么样」。
拷过去、解压、双击 `valorant_boost.exe` 就能用 —— **不需要装 Flutter、不需要装
VC++ 运行库、不需要管理员权限**（只有点「开启守护」那一下会弹一次 UAC）。

### 缺的第一样：MSVC 运行库

`valorant_boost.exe` 的导入表里有：

```
VCRUNTIME140.dll   VCRUNTIME140_1.dll   MSVCP140.dll
```

Flutter 的 CMake 用 `/MD`（动态 CRT）链接，所以这三份 DLL 得由系统提供。
装过 Visual Studio 或者「Visual C++ 2015-2022 可再发行组件包 (x64)」的机器有，
**全新装的 Windows 没有** —— 那种机器上 exe 会在任何 Dart 代码跑起来之前就退出，
连个错误框都不弹，最难查。

`make-portable.ps1` 把 `C:\Windows\System32` 里的这三个文件拷到 exe 旁边，
这叫 [app-local deployment](https://learn.microsoft.com/cpp/windows/redistributing-visual-cpp-files)，
微软支持的正式做法，比让对方去装再发行包省事。

### 缺的第二样：引擎和它的日志路径

Flutter 这一版**只是个界面**，每一页的内容都是跑 `ValorantBoost.exe` 拿回来的。
而 `lib\engine.dart` 原来第一行候选路径写的是：

```dart
r'D:\test\valorant-cpu-boost\ValorantBoost.exe',   // ← 只有我这台机器有
```

因为它在开发机上永远命中，这个坑一直没暴露；**换台机器就是「找不到引擎」，
五个页面全空**。现在改成了按顺序找：

| 顺序 | 位置 | 覆盖的场景 |
|---|---|---|
| 1 | `%VALORANT_BOOST_HOME%\ValorantBoost.exe` | 环境变量显式指定 |
| 2 | **和界面自己同一个文件夹** | 绿色部署（`make-portable.ps1` 就靠这条） |
| 3 | 往上 1–6 级目录 | 开发时的 `build\windows\x64\runner\Release\`（往上 5 级正好是仓库根） |
| 4 | `C:\ProgramData\ValorantBoost\` | 正式 `install` 装出来的位置 |
| 5 | `%APPDATA%\ValorantCpuBoost\` · `%LOCALAPPDATA%\ValorantBoost\` | 引擎自己的兜底目录 |
| 6 | `D:\test\valorant-cpu-boost\` | 我开发机的老位置，**放最后**，不会在别人机器上「抢答」 |

日志和备份同理，**不能再写死**。引擎那边的规则是（`one\Core.cs:549-563`）：
目录 = exe 所在目录，那个目录不可写就退到 `%APPDATA%\ValorantCpuBoost`；
而守护任务是 `install` 的时候用 `watch --dir "<当时的目录>"` 把目录**烤进计划任务**的
（`one\Ui.cs:1095`）—— 所以守护写的日志在哪，取决于**对方在哪装的**，猜不出来。

现在的做法是不猜：把所有候选目录列出来，**谁的 `boost-log.txt` 最新就用谁的**；
一个都没有（全新机器）就用引擎所在目录，这样守护一写就找得到。

### 对方机器上需要什么

| 需要 | 为什么 |
|---|---|
| Windows 10 1903+ / Windows 11 | 引擎是 .NET Framework 4.x 程序（exe 里有 `mscoree.dll`），1903 起系统自带 4.8 |
| 上面那个 `release\portable\` 文件夹 | 其他什么都不用装 |
| 一次 UAC 同意 | 改电源方案要管理员；引擎自己 `psi.Verb = "runas"` 提权，所以 exe 本身不用带管理员清单 |

**如果对方也想装守护**：在游戏专项页点「开启守护」，它会把当时的文件夹路径写进计划任务。
所以**先把这个文件夹放到最终位置，再点开启守护** —— 之后再挪文件夹，守护就找不到日志了。

## 一键安装（`install.exe`）

`release\portable\` 那套要手动拷贝、手动点开启守护、每次启动还弹一次 UAC。打包成单个
`install.exe` 就是为了免掉这些。生成它：

```powershell
powershell -ExecutionPolicy Bypass -File tool\make-installer.ps1
# 产物：<仓库根>\release\install.exe   约 11.4 MB
```

它把整个 `release\portable\` 压进 `payload.zip` 内嵌进 exe（`/resource:payload.zip,Payload`），
所以**对方只要这一个文件**，双击即可。

### 「装一次 UAC，之后永不弹」是怎么做到的

| 步骤 | 做法 |
|---|---|
| `install.exe` 自己 | 带 `requireAdministrator` 清单 ⇒ **这一次弹 UAC** |
| 界面以后怎么起 | 注册计划任务 `ValorantBoostUI`，`RunLevel=HighestAvailable`、`LogonType=InteractiveToken` |
| 快捷方式指向哪 | 不是界面 exe，而是 `run-ui.exe`（**不带清单，asInvoker**），它只做一件事：`schtasks /run /tn ValorantBoostUI` |

**为什么必须是「界面一启动就是管理员」，而不是「每条命令各自提权」**：界面拿到了管理员
令牌，`lib\engine.dart` 里那些 `Process.run` 直接继承，**一行都不用改**。反过来做就得写
一套请求/响应协议。（C# 版能用 `psi.Verb = "runas"` 是因为它走 `ShellExecute`；Dart 的
`Process.run` 走 `CreateProcess`，套不上。）另外 `run-ui.exe` 必须单独存在：快捷方式直接
调 `schtasks.exe` 会闪一下黑窗，直接调 `install.exe` 又会因为清单每次弹 UAC。

### ★★★ 这个设计会撞上 Windows Defender —— 必须知道

**症状**：装完几分钟内，引擎 exe 被丢进隔离区、`ValorantBoostWatcher` 计划任务被删，
守护彻底失效。事件查看器/Defender 里能看到：

```
ThreatName = Behavior:Win32/Persistence.A!ml    严重度 5
  file:C:\...\ValorantBoost.exe
  taskscheduler:C:\Windows\System32\Tasks\ValorantBoostWatcher
  regkey:HKLM\...\Schedule\TaskCache\Tree\ValorantBoostWatcher
```

**根因**：我们这个手法本身 —— **把未签名的 exe 落到一个用户可写目录，再用它注册一个
「登录时触发、最高权限」的计划任务** —— 在行为检测里就是教科书式的「计划任务持久化」。
Defender 判得没错，只是我们不是恶意的。实测时间线（2026-10-01）：

```
16:46:51  解压出的引擎 exe 首次被判
16:48:23  重建守护任务的动作让 ProgramData 的引擎 exe 被隔离、任务被删
16:49:17  连用户原来就有的 ValorantBoost.lnk 快捷方式也被收走
```

**修法**：安装器的 `[4/8]` 步会显式给**安装目录这一个路径**加 Defender 排除项
（`Add-MpPreference -ExclusionPath`），并在输出里说明为什么；卸载时 `[4/7]` 步用
`Remove-MpPreference` 原样收回。

**如果不想让安装器动 Defender**：把那一步删掉，代价是守护会随时被清掉 —— 那就得改成
每次启动界面都弹一次 UAC（把 `installer.manifest` 换成 `asInvoker`，快捷方式直接指向
界面 exe），放弃「零 UAC」这个目标。

### 卸载

有三个入口，都指向同一套逻辑：

| 入口 | 适合谁 |
|---|---|
| **「设置 → 应用」→ 游戏 CPU 高频优化器 → 卸载** | 最正常的那条路 |
| 开始菜单「卸载 游戏 CPU 高频优化器」 | 同上 |
| 安装目录里的 `uninstall.exe`（可加 `--quiet` 静默） | `install.exe` 丢了也能卸；也给脚本调 |

`installer\Uninstaller.cs` 编出来的 `uninstall.exe` 单独存在，安装时被解到安装目录，
同时也以资源名 `Uninstaller` 内嵌进 `install.exe`（所以 `install.exe` 自己也不怕被删）。
它注册在 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ValorantCpuBoost`：
`DisplayName` / `DisplayVersion` / `Publisher` / `InstallLocation` / `UninstallString` /
`QuietUninstallString` / `DisplayIcon`（指向界面 exe，这样列表里有图标）/
`NoModify=1` / `NoRepair=1` / `EstimatedSize`（KB）。

**共享代码在 `installer\Vcb.cs`** —— 安装器和卸载器都要「关进程、删任务、收排除项、
删快捷方式、删目录」这一整套，抄两份迟早会不一致。`Installer.cs` 里保留同签名的私有
薄包装，所以它那 600 多行调用点一个字都没改。

卸载七步：关界面+引擎 → 删 `ValorantBoostUI` → 调引擎 `uninstall` 删
`ValorantBoostWatcher` → **收回 Defender 排除项** → 删注册表条目 → 删快捷方式 →
删安装目录（自己删自己所在目录会失败，所以写一个临时 `.cmd` 延迟删）。
对着一个没装过的机器跑也不会炸：每一步都是「尽力而为 + 失败只提示不中断」。

#### ★★★ 延迟删除的「盲删窗口」—— 卸载后立刻重装，新装的那份会凭空消失

「删掉自己住的目录」只能靠一个延迟批处理，而它第一版是这样的：

```bat
@echo off
ping 127.0.0.1 -n 3 >nul
rd /s /q "<安装目录>"
del /f /q "<自己>"
```

`ping -n 3` 只等约 2 秒，而 `rd /s /q` 之前**不做任何确认**。所以卸载之后如果在这两
三秒窗口里又装了一次，批处理醒来就把**刚装好的**那份整个吃掉。这是实测撞到的：
18:10 重装，18:12:45 目录消失 —— 连守护任务、界面任务、注册表条目、Defender
排除项一起没了，`%LOCALAPPDATA%` 下只剩 `valorant_boost_flutter`（界面配色，
卸载时**刻意保留**的那个，反而成了「确实跑过卸载器」的旁证）。
Prefetch 时间线是 `UNINSTALL.EXE-6F2265D3.pf` 18:05:04 → 重装 → 目录 mtime
18:12:45 → `UNINSTALL.EXE-7A332903.pf` 18:12:47。

修法是**哨兵文件**：卸载第 7 步先往安装目录写 `.vcb-uninstalling`，批处理醒来先看它
还在不在：

```bat
@echo off
ping 127.0.0.1 -n 4 >nul
if not exist "<安装目录>\.vcb-uninstalling" goto :vcb_done
rd /s /q "<安装目录>"
:vcb_done
del /f /q "<自己>"
```

**为什么这样就够**：重装时 `RemoveStaleFiles()` 拿 payload 的条目名做白名单，
`.vcb-uninstalling` 既不在白名单、也不在保留名单（保留名单只有三个：
`boost-log.txt` / `boost-backup.txt` / `.wtest`）⇒ 会被当成上一个版本的遗留文件清掉
⇒ 批处理看到哨兵不在就跳过。常量在 `installer\Vcb.cs` 的 `SentinelName`。

实测两种情形都对：

| 情形 | 结果 |
|---|---|
| 卸载后不重装 | 7 秒后目录已删 ✓、哨兵无残留 ✓ |
| **卸载后不给窗口期、立刻重装** | t+3s / t+8s / t+13s 目录都还在 ✓，守护任务、界面任务、注册表条目齐全 ✓ |

`%LOCALAPPDATA%\valorant_boost_flutter`（界面配色设置）**刻意不删** —— 重装之后
配色还在，比「卸干净」更符合预期。

### ★ 装完之后做的事：卸载入口独立了，顺带省掉 11.4 MB

第一版 `install.exe` 在收尾时会**把自己复制一份进安装目录**，理由是「卸载入口才找得到」
（当时的卸载入口就是 `install.exe --uninstall`）。出了独立的 `uninstall.exe` 之后，
这个理由就不成立了，但那份副本还留着 —— `install.exe` 带着内嵌的 `payload.zip`，
**11.4 MB**，放在一个 27 MB 的安装目录里正好是 **42% 的纯冗余**（实测目录 38.7 MB）。
现在不再复制了；要重装/修复，用当初下载的那个 `install.exe` 就行：

```powershell
install.exe              # 覆盖重装，顺便修复被安全软件吃掉的文件
install.exe --uninstall  # 卸载（老入口保留，老快捷方式和用户脚本还在用它）
```

**★ 覆盖安装从来不删文件，这是另一个更容易咬人的坑。** `ExtractZip` 只负责写和覆盖，
所以「上个版本有、这个版本不再有」的文件会一直赖在目录里 —— 上面那 11.4 MB 就是这样
活过一个又一个版本的。现在 `[3/8]` 解压之后会多走一遍 `RemoveStaleFiles()`：
拿 payload 里的条目名做白名单，`InstallRoot` 下不在白名单里的文件全删，并报出删了几个。
**保留名单只有三个**：`boost-log.txt`、`boost-backup.txt`、`.wtest` ——
前两个是引擎运行期自己写的，`boost-backup.txt` 还是「还原初始设置」的还原点，
删掉会让用户丢掉回退能力。实测：造一个假的 `boost-backup.txt` + 一个 `leftover.dll`
覆盖安装，前者内容原样保留、后者被清掉。

### 绿色版怎么清理（`release\portable\cleanup.cmd`）

`release\portable\` 那套不用安装，但在界面里点过「开启守护」之后同样会留下两样东西：计划任务
`ValorantBoostWatcher` 和一条 Defender 排除项。收回去的办法是文件夹里的 `cleanup.cmd`：

```powershell
# 双击也行 —— 它会自己请求提权
D:\test\valorant-cpu-boost\release\portable\cleanup.cmd
```

它做五件事：关掉在跑的界面和守护 → 删遗留的 `ValorantBoostUI` → 让引擎自己
`uninstall`（**不重写「守护任务叫什么」这知识**）→ 收回本目录的 Defender 排除项 →
列出刻意不动的东西。**文件夹本身不动**，想删直接删。

**★ 两道闸**：

- **管理员检查**：直接跑 `.ps1` 而没提权会被拦住并说明原因，不会装作跑过了。
- **「装过正式版」检查**：绿色版和安装版**共用同一套任务名**，所以在这里清理会把装好的
  那份一起拆掉 —— 这是实测踩到的（跑完清理，安装版的开机自启和提权项都没了）。
  检测到 `%LOCALAPPDATA%\ValorantCpuBoost\valorant_boost.exe` 存在就停下（退出码 2），
  告诉你用 `uninstall.exe`。确实要强拆加 `-Force`。

**★ 两个文件要相反的编码，这是这一步唯一容易错的地方**：

| 文件 | 编码 | 为什么 |
|---|---|---|
| `cleanup.cmd` | **纯 ASCII，无 BOM** | cmd.exe 按控制台代码页（这里是 936）读 `.cmd`，中文会乱码；而且带 BOM 会让第一行 `@echo off` 直接失效 |
| `cleanup.ps1` | **UTF-8 带 BOM** | PowerShell 5.1 把没有 BOM 的 `.ps1` 按 GBK 解码，中文全变乱码 |

`tool\make-portable.ps1` 的 `[6/6]` 步会把这两个文件拷进 `release\portable\` 并
**顺手校验编码**（`.cmd` 必须 0 个非 ASCII 字节且无 BOM，`.ps1` 必须有 BOM），不对就黄字警告。

**★ 踩到的坑：`edit` 之类的编辑器会悄悄吃掉 BOM。** 改完 `cleanup.ps1` 一定要补回来，
否则下次跑就是满屏乱码（而且语法检查也会报一堆莫名其妙的错 —— 因为解析器同样按 GBK 读）：

```powershell
$p = 'tool\cleanup.ps1'
$t = [System.IO.File]::ReadAllText($p, (New-Object System.Text.UTF8Encoding($false)))
[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($true)))
```

### ★ `-Zip` 那个从没被跑过的 bug

`make-portable.ps1` 结尾原来是：

```powershell
if ($Zip) { $zip = "$Out.zip"; ... }
```

**PowerShell 的变量名不区分大小写，所以 `$zip` 就是那个 `[switch]$Zip` 参数本身**，
往它上面赋字符串会报：

```
Cannot convert value "System.String" to type "System.Management.Automation.SwitchParameter".
```

改成 `$zipPath` 即可。这个 bug 一直在，只是以前从没带 `-Zip` 跑过。

### 踩过的两个坑

- **`out` 参数不能传临时表达式**：`Run("schtasks.exe", ..., out new string[0])` 报
  `error CS1510: ref 或 out 参数必须是可赋值的变量`。先 `string o2;` 再传。
- **csc 的中文报错是乱码**：加 `/utf8output` 就好。

## 界面里有什么

| 页面 | 内容 |
|---|---|
| **主页** | 优化状态（已优化 / 未优化）、四个档位卡、一键优化 / 还原初始设置、结果 |
| **体检** | 电源方案逐项核对表（交流 / 电池 / 判定）、后台抢 CPU 的程序表（带比例条） |
| **性能测试** | 固定时间里数 CPU 算多少次开方，不依赖系统计数器 |
| **游戏专项** | 无畏契约 / Apex 切换、主程序与显卡绑定、后台守护开关、后台程序压制、配置文件检查 |
| **日志** | `boost-log.txt` 的尾部 |

## 托盘图标（引擎建的，但点开的是 Flutter 界面）

托盘图标**不是 Flutter 建的**，是引擎（常驻守护 `ValorantBoost.exe watch`）建的，
在 `one\Ui.cs` 的 `WatchApp` 里。这么分是有意的：

| | 谁来做 | 为什么 |
|---|---|---|
| 托盘图标 + 4 秒轮询 | **引擎**（守护） | 必须常驻、必须管理员权限（改电源方案），而且要在界面**根本没打开**时也盯着游戏 |
| 主窗口 | **Flutter** | 就是你正在用的这套界面 |

### ★★★ 修掉的那个 bug：托盘点出来的是老界面

原来的 `ShowMain()`（`one\Ui.cs`）是这么写的：

```csharp
ProcessStartInfo psi = new ProcessStartInfo(AutoStart.ExePath());
```

而 `AutoStart.ExePath()`（`one\Core.cs:892`）返回的是
`Assembly.GetExecutingAssembly().Location` —— **正在跑的那个 assembly，也就是
`ValorantBoost.exe` 它自己**。不带参数进去，`Main` 会走到
`Application.Run(new MainForm())`，于是**点托盘弹出来的是老的 C# WinForms 窗口**，
Flutter 那套只有从桌面快捷方式进去才看得到。两套界面同时活着，根子就在这一行。

### 现在怎么做的（`one\Ui.cs` 的 `UiLaunch.Show()`）

按顺序试三条路：

1. **界面已经在跑** → `Process.GetProcessesByName("valorant_boost")` 拿到
   `MainWindowHandle`，最小化就 `ShowWindow(SW_RESTORE)`，然后 `SetForegroundWindow`。
   没有这一步的话，窗口被最小化、或压到别的窗口后面之后**就再也点不出来了** ——
   `ValorantBoostUI` 任务设了 `IgnoreNew`，重复触发是空操作。
2. **没在跑** → `schtasks /run /tn "ValorantBoostUI"`，和桌面快捷方式**同一条路**：
   任务 `RunLevel=HighestAvailable`，所以界面是管理员身份、**不弹 UAC**。
3. **兜底** → 绿色版没注册过计划任务，界面 exe 就躺在引擎旁边或上一级，直接起。

★ 找窗口用的是**进程名**，不是 `FindWindow` 按标题找。标题「游戏 CPU 高频优化器」
里的中文要经过源码编码才落进 exe（`build-engine.ps1` 用 `/codepage:65001` 读
**无 BOM** 的 UTF-8），一旦编码错位 `FindWindow` 会**无声地**永远找不到窗口；
进程名是纯 ASCII，没有这个风险。

★ `UiTaskName = "ValorantBoostUI"` 这个常量在引擎里是**第二份**（`one\Ui.cs` 的
`UiLaunch`），第一份在 `installer\Vcb.cs:57`。引擎不引用安装器的源码，
所以**改一处必须改两处**。

### 命令行也能复现托盘那一击

```powershell
ValorantBoost.exe show
ValorantBoost.exe show --out C:\somewhere\result.txt
```

排障时不用真去点托盘。实测两种情形：

| 情形 | 引擎返回的话 | 耗时 |
|---|---|---|
| 界面没在跑 | `界面没在跑，已通过计划任务 ValorantBoostUI 拉起（管理员身份，不弹 UAC）。` | 183 ms |
| 界面在跑且已最小化 | `界面已经在跑（pid 32228），已从最小化还原并叫到前面。` | 124 ms |

两种情况都验证过：5 秒后 Flutter 窗口（标题「游戏 CPU 高频优化器」）起来了，
`GetForegroundWindow()` 确实是它，而**老的 WinForms 窗口没有出现**。

### ★ 右键菜单为什么还是 Windows 原生的

托盘右键菜单是 **Explorer 画的**，不是程序自己画的 —— 任何程序都改不了它的外观。
想「全套 Material You」只有一条路：弃用原生菜单，改成在鼠标位置弹一个自己做的小窗。
那要额外加 `window_manager` 包，还得在两个进程之间架一套 IPC（否则每次右键都要
新起一个 Flutter 进程，慢约 1 秒），所以**这次没做**。

## 动画

和 C# 版一样，时长是照着手感调的（毫秒）：

| 动作 | 时长 |
|---|---|
| 按钮按下 / 抬起 | 70 / 90 |
| 按钮悬停进出 | 110 |
| 导航悬停 / 选中 | 120 / 180 |
| 档位卡悬停 / 选中 | 130 / 200 |
| 勾选回弹 / 取消 | 300 / 140 |
| 表格行悬停 | 120 |
| 表格逐行生长 | 460 |
| 切页滑动 | 200（位移 26 px） |

**按钮反馈刻意压得短。** 界面上的反馈一旦超过 150 ms 就会显得迟钝 —— 那是「拖」，
不是「顺滑」。

## 已知的不同

- Flutter 的 `Text` 不用手动补齐空格对齐，所以 C# 版那套 `TextPad` / `TextRenderer.MeasureText`
  按像素量的对齐逻辑在这里没有对应物 —— 表格用 `Expanded` 分列，天然对齐。
- C# 版表格是自绘的（含吸顶表头和自绘滚动条）；这里用 `ListView` + `Scrollbar`，
  行为一致但滚动条样式跟随 Flutter 主题。
- 这一版**没有** `watch`（守护）功能 —— 那是引擎的事，界面只负责开关计划任务。

## 排错：两个「不报错但界面就是不对」的坑

这一版首次跑起来时踩到两个问题，**`flutter analyze` 和编译器都发现不了**，症状都是「界面看着像死了一样」。记在这里，因为改布局时很容易再犯。

### ① stderr 刷几百条 `TransformLayer is constructed with an invalid matrix`

```
[ERROR:flutter/flow/layers/transform_layer.cc(15)] TransformLayer is constructed with an invalid matrix.
```

**症状**：窗口能开、不崩，但某一页从某张卡往下**整片空白**，同时 stderr 每帧刷一条。6 秒能刷 550 行。

**根因**：`home_page.dart` 的档位卡那一行是

```dart
Row(
  crossAxisAlignment: CrossAxisAlignment.stretch,   // ← 元凶
  children: [Expanded(child: ModeCardView(...)) × 4],
)
```

而它外面套着 `SingleChildScrollView` → `Column`，**纵向约束是 infinity**。`stretch` 的语义是「把孩子的交叉轴尺寸设成传入约束的最大值」—— 最大值是 infinity，于是四张卡被撑成无限高：卡片自己画不出来，后面的按钮被推到 y=∞，每帧生成一个非有限（NaN/Inf）的变换矩阵。Flutter 的 C++ 层检查 `!matrix.isFinite()`，判定后把矩阵重置成单位阵，所以**不崩，只是刷屏 + 布局塌掉**。

**修法**：外面包一层 `IntrinsicHeight`，先把内容高度量出来，`stretch` 才有确定值可用。

```dart
child: IntrinsicHeight(
  child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [...]),
),
```

**排查手法（可复用）**：`flutter create` 一个空白模板应用对比 —— 模板只刷 1 行，就能立刻确认「不是驱动/Impeller 的问题，是我自己的代码」；再往 `_pages()` 里逐个注释页面做二分。

> ⚠️ 顺带记住：`IndexedStack` 会 **layout 全部子页但不 paint 未选中的**。所以「当前页没问题」不等于其他四页没问题，切页才暴露。

### ② 「游戏专项」页永远是空的

**症状**：那一页显示「还没有登记 / 显卡绑定：未登记 / 没有找到配置文件 / 还没有数据」，但引擎明明是好的（体检页数据齐全）。切一下游戏标签、或点一次「重新检查」，数据立刻出现。

**根因**：`app_state.dart` 的 `games` 是懒加载的，只有 `ensureGame(key)` 会填充它，而它只被 `selectGame()` 和 `reloadGame()` 调用 —— `init()` 里漏了：

```dart
// 修前
await Future.wait([refreshCheck(), refreshAutoStart(), refreshLog()]);
```

**修法**：

```dart
await Future.wait<void>([
  refreshCheck(),
  refreshAutoStart(),
  refreshLog(),
  ensureGame(gameKey),      // ← 补上
]);
```

（`Future<GameInfo>` 能赋给 `Future<void>`，因为 `void` 是顶类型，所以这个混合列表可以用 `Future.wait<void>` 收下。）

**教训**：凡是「点一下才会出来」的数据，都要问一句**启动时谁负责拉它**。

### ③ 整条侧栏塌成一片黑（`NavigationRail` + `DropdownMenu`）

**症状**：换 Material You 之后，1264×681 的窗口里只剩左上角一个下拉框，其余全黑。stderr 刷 47 行 `TransformLayer is constructed with an invalid matrix`（和第 ① 条同一个报错，但根因完全不同）。

**根因**：`NavigationRail(extended: true, leading: ..., trailing: ..., groupAlignment: -0.82)`，而 `trailing` 里塞了一个 `DropdownMenu`。`DropdownMenu` 底下是 `MenuAnchor` + 内嵌 `TextField`，在「窄而高」的侧栏容器里算出了爆掉的尺寸，把 rail 的 `Expanded` 挤成零。

**修法**：两样都换掉。

- 侧栏**不用** `NavigationRail`，自己用 `Column` 拼（`lib/widgets/nav_item.dart` + `lib/main.dart` 的 `_sidebar`）。M3 的观感其实不来自「用了哪个组件」，而来自三条：①选中态用 `secondaryContainer` 全圆角药丸 ②选中时图标描边→实心 ③所有元素共用同一条左基准线。自己写反而更准。
- 下拉**不用** `DropdownMenu`，改用 `DropdownButton`（`isExpanded: true`，外面套一个固定 `height: 40` 的 Container）。尺寸完全可预测。

**教训**：`TransformLayer ... invalid matrix` 只是**症状**，不是病因 —— 任何「算出非有限约束」的布局都会报这一条。看到它就去找**哪个容器的高度或宽度是 infinity**，别去查矩阵代码。

## Material You（配色与侧栏）

### 种子色

侧栏底部有一个「配色种子」下拉，八项：跟随系统强调色 / 无畏契约红 / 海蓝 / 青碧 / 紫罗兰 / 松绿 / 琥珀 / 石墨。选中后立刻换肤，并写进 `%LOCALAPPDATA%\valorant_boost_flutter\seed.txt`。

- **首次启动默认「无畏契约红」**（`kDefaultSeedKey`）。理由：这软件的辨识度就是那抹红，而 Material You 的「味道」来自药丸选中态、色调面板、M3 字体这一整套，不来自颜色本身 —— 默认红不会让它变得不像 Material You，但能让第一眼对得上品牌。想跟着系统走，下拉里第一项就是。
- 「跟随系统强调色」是读注册表：`HKCU\Software\Microsoft\Windows\DWM` 的 `AccentColor`，兜底 `HKCU\Software\...\Explorer\Accent` 的 `AccentColorMenu`，都读不到就用 `kFallbackSeed`。
  > ⚠️ **注册表里存的是 `0xAABBGGRR`，不是 ARGB。** 按 ARGB 解会把红蓝对调，取出来是青的。拆法：`r = v & 0xFF; g = (v >> 8) & 0xFF; b = (v >> 16) & 0xFF;`
- `dynamicSchemeVariant: DynamicSchemeVariant.fidelity` —— 默认的 M3 算法（`tonalSpot`）会大幅降饱和，拿 `#F65260` 这种鲜红做种子会推出灰扑扑的砖红、按钮变成浅粉，品牌辨识度就没了。`fidelity` 变体尽量保住种子色本身的色度，明度/容器色/状态层依然全套按 M3 走。

### 换肤是怎么做到「五个页面一个字都不用改」的

`theme.dart` 里的 `T` 原本是**编译期常量**。要换肤就得让它可变，但页面里到处是 `const X(color: T.fg)`，一改就要满库摘 `const`。

做法：**只让颜色可变，尺寸和时长仍是 `const`**（先统计过，只有 36 行同时出现 `const` 和 `T.`）。于是

```dart
// theme.dart
static Color fg = ...;                    // ← 可变
static const double fsBody = 13;          // ← 仍然 const

static void apply(ColorScheme cs) { ... } // 把 scheme 铺进去
```

调用点在 `main.dart` 根节点的 `build()` 里，`buildM3Theme()` 之后立刻 `T.apply(theme.colorScheme)` —— 根节点天生最先 build，子节点读到的一定是新值。

> ⚠️ 摘 `const` 的脚本要能区分**表达式**和**声明**：`const AppCard({` 是构造函数声明，被误摘会导致编译不过。声明紧跟着 `({`，表达式不会。

### 侧栏对齐

整条侧栏只有一个左基准线常量 `kRailPad = 16`（`lib/widgets/nav_item.dart`）。标题、游戏切换、导航项、页脚全部从这同一个 16px 起。

导航项是唯一例外：选中药丸要往外多探 8px（M3 的指示器本来就比文字宽），所以 `NavItem` 外面用 `horizontal: 8`、药丸里再垫 8 —— 图标左边缘照样落在 16px 上。

改一个数，整条侧栏一起动。这就是「左边没对齐」的全部解法。

