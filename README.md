# 游戏 CPU 高频优化器

把 CPU 从「按需降频」改成**尽量保持高频**，并针对 **Intel Core Ultra 7 155H（Meteor Lake 混合架构）**
做了专门适配：EPP 能效偏好、P 核优先调度、大小核停泊策略、游戏进程优先级 / EcoQoS 节流。

**所有改动全部先备份，一键即可完整还原。**

| | |
| --- | --- |
| 当前版本 | **v1.0** |
| 下载 | [Releases](https://github.com/hayashirye/HassMeits-booster/releases) |
| 界面 | Flutter（Material You） |
| 引擎 | C# / .NET Framework 4.x，无需安装运行时 |
| 支持游戏 | 无畏契约（VALORANT）、Apex Legends |

---

## 目录

1. [这是什么](#1-这是什么)
2. [安装](#2-安装)
3. [界面](#3-界面)
4. [命令行](#4-命令行)
5. [它到底改了什么](#5-它到底改了什么)
6. [针对 Core Ultra 7 155H](#6-针对-core-ultra-7-155h)
7. [三个必须知道的前提](#7-三个必须知道的前提)
8. [项目结构](#8-项目结构)
9. [从源码构建](#9-从源码构建)
10. [卸载 / 完整还原](#10-卸载--完整还原)
11. [排错](#11-排错)
12. [编码约定](#12-编码约定)

---

## 1. 这是什么

一个 Windows 上的 CPU 调优小工具，目标只有一个：**消除「系统主动降频」这一层损耗**，
让游戏跑在尽可能高的频率上。

它**不**注入游戏进程、**不** hook、**不**改游戏文件 —— 只动三样东西：

- **电源方案**（15 项，走 `powercfg` 导出 → 字节层面改值 → 导回）
- **系统注册表**里的游戏相关开关（Game Mode / Game DVR / 全屏优化 / PowerThrottling / GPU 首选项）
- **运行中的游戏进程**的优先级与 EcoQoS 节流（仅当前这次运行）

### 架构

```
install.exe  ──┬─►  valorant_boost.exe   Flutter 界面（Material You）
               │        │
               │        └─ 通过管道调用引擎的命令行
               │
               └─►  ValorantBoost.exe    C# 引擎（无界面）
                        ├─ apply / restore / check / bench …
                        ├─ watch   常驻守护：托盘图标 + 自动增强
                        └─ 自己注册 ValorantBoostWatcher 计划任务
```

界面和引擎是**两个独立进程**，通过引擎的命令行接口通信。这意味着：

- 关掉界面，守护和托盘照常工作；
- 引擎可以单独在命令行里用（脚本 / 批处理 / 计划任务）；
- 界面崩了不会影响已经应用的优化。

---

## 2. 安装

从 [Releases](https://github.com/hayashirye/HassMeits-booster/releases) 下载 **`install.exe`**，双击。

安装器会**全程只弹一次 UAC**，然后依次做八件事：

| 步骤 | 做什么 |
| --- | --- |
| 1/8 | **检查安装包完整性** —— 确认内嵌的 `payload.zip` 在。忘了加 `/resource` 编出来的是空壳，这一步能立刻报出来，不用等装到一半 |
| 2/8 | **关闭正在运行的旧版本** —— 否则文件被占用会解压失败 |
| 3/8 | **解压程序文件**（约 27 MB）到安装目录（默认 `%LOCALAPPDATA%\ValorantCpuBoost`），顺手写入 `uninstall.exe`，并清掉上个版本遗留的多余文件 |
| 4/8 | **让 Windows Defender 放行安装目录**（见下方说明） |
| 5/8 | **安装后台守护** —— 注册 `ValorantBoostWatcher` 任务，检测到游戏自动优化 |
| 6/8 | **注册提权启动项** —— 注册 `ValorantBoostUI` 任务，以后启动界面不再弹 UAC |
| 7/8 | **注册到「程序和功能」** |
| 8/8 | **创建快捷方式** —— 桌面 + 开始菜单 |

装完桌面会出现 **「游戏 CPU 高频优化器」**。

### 关于 Defender 排除项

安装器会把**安装目录**加进 Defender 的排除列表。这不是为了绕过检测，而是因为
「登录时触发、以最高权限运行」的计划任务在 Defender 的行为检测里长得就像持久化木马，
不加排除项会被反复拦截。

**卸载时这一项会对称收回**，Defender 恢复原样。

### 绿色版

Release 里还有一个 **`portable.zip`**，解压即用，不写注册表、不建计划任务。
代价是每次运行都要手动点 UAC。

绿色版目录里有 `cleanup.cmd`，用来把绿色版在系统里留下的痕迹清干净。

> ⚠️ 绿色版和安装版**共用同一套计划任务名**。所以在绿色版目录里跑 `cleanup.cmd` 时，
> 如果检测到正式安装版存在，它会**停下来并让你改用 `uninstall.exe`** —— 否则会把正式安装那份一起拆掉。
> 确实要强拆，加 `-Force`。

---

## 3. 界面

深色标题栏 + 左侧导航，Material You 配色（主题色从壁纸取种子色，可自行调整）。
左上角有「管理员 / 未提权」徽章，主页是实时频率和占用率的仪表盘。

左侧五个页面：

| 页面 | 内容 |
| --- | --- |
| **主页** | 实时频率 / 占用率仪表盘；三个强度可选；① 应用优化 · ② 增强游戏进程 · ④ 一键还原 |
| **体检** | 只读。15 项设置逐条列出：左边是项目名和它为什么能提高频率，右边是进度条（绿色填充 = 应用后会到的位置，竖线 = 你机器当前的值），最右标着 **已应用 / 待应用 / 本机不支持** |
| **性能测试** | 内置跑分，对比优化前后的单核 / 多核表现 |
| **游戏专项** | 按游戏（无畏契约 / Apex）分别配置；查看是否登记、守护是否开启、配置文件是否找到 |
| **日志** | 最近 60 秒的 CPU 占用率 / 频率双曲线 + 完整操作日志 |

**上手顺序**：先切到「体检」挑强度（默认 **均衡**，推荐先试这个）→ 点「应用这套设置」
→ 开游戏后点「增强游戏进程」→ 不想要了点「一键还原」。

> 「增强游戏进程」只影响**当前正在运行**的那一次；下次开游戏再点一次，
> 或者干脆让守护（托盘）自动做。

### 托盘

引擎的守护进程（`ValorantBoost.exe watch`）会在托盘放一个图标。图标是白字 **V**：
待机时是主题色，检测到游戏在跑时变绿。

右键菜单：**开机自启动 / 显示主界面 / 立即优化 / 立即还原 / 退出守护**。

点「显示主界面」→ 界面已开就前置，没开就通过计划任务拉起（**管理员身份、零 UAC、不会开出第二个窗口**）。

> 托盘菜单本身是 Windows Explorer 画的，任何程序都改不了它的外观 —— 只有窗口部分是 Flutter。

---

## 4. 命令行

引擎可以脱离界面单独使用。所有命令都需要管理员权限。

### 界面相关

```powershell
.\ValorantBoost.exe show          # 显示 / 前置 Flutter 界面
.\ValorantBoost.exe watch         # 常驻守护：托盘 + 自动增强（登录时自启的就是它）
```

### 优化

```powershell
.\ValorantBoost.exe check         # 只读体检：逐项报告当前状态
.\ValorantBoost.exe apply         # 应用优化（默认 competitive 档）
.\ValorantBoost.exe apply max     # 极限高频
.\ValorantBoost.exe restore       # 一键还原
.\ValorantBoost.exe autocheck     # 探测：现在该不该优化
```

### 诊断与测试

```powershell
.\ValorantBoost.exe bench         # 跑分
.\ValorantBoost.exe audit         # 系统审计
.\ValorantBoost.exe bgcpu         # 后台进程 CPU 占用审计（采样 6 秒）
.\ValorantBoost.exe gametime      # 游戏时长统计
.\ValorantBoost.exe gpupref       # GPU 图形首选项
.\ValorantBoost.exe probe         # 环境探测
```

### 核心绑定（混合架构专用）

```powershell
.\ValorantBoost.exe pin           # 把游戏进程绑到 P 核
.\ValorantBoost.exe unpin         # 解除绑定
.\ValorantBoost.exe pinstat       # 查看绑定状态
```

### 开机自启

```powershell
.\ValorantBoost.exe install       # 注册登录自启
.\ValorantBoost.exe uninstall     # 取消登录自启
```

> 这些子命令走 `one\Ui.cs` 的 `Headless()` 白名单。
> **不在白名单里的参数会掉进老的 WinForms 窗口** —— 如果你看到那个界面，说明命令名拼错了。

---

## 5. 它到底改了什么

### 5.1 电源方案（15 项）

程序**不解析 `powercfg /query` 的文本输出**（中英文系统文本不同，容易失配），
而是把方案导出成 `.pow` 文件、直接在**字节层面**改值、再导回去 —— 跨语言、跨 Windows 版本都稳。

| 设置项 | 均衡 | 极限 | 为什么能提高频率 |
| --- | --- | --- | --- |
| **最小处理器状态** | 60% | **100%** | 默认允许空闲降到 5%，一进游戏升频有几毫秒延迟；抬高地板 = 更少等待。**这是「尽量高频」最关键的一项** |
| **最大处理器状态** | 100% | 100% | 防止被限频（有些 OEM 方案默认压到 95%~99%，直接砍掉睿频） |
| **性能提升模式** | 2 = 激进 | 2 = 激进 | 允许 CPU 主动冲到最高睿频倍频，而不是「需要时再慢慢加」 |
| **能效偏好 EPP** | 30 | **0** | Intel Speed Shift 直接按这个值选频率档位，0 = 最偏性能 |
| **性能检查间隔** | 100 | 1 | 缩短「多久检查一次负载」，负载上来时升频更快 |
| **核心停放 最小/最大** | 100 / 100 | 100 / 100 | 禁用核心停泊，核心不会「关掉睡觉」，少一次唤醒延迟 |
| **异构调度策略** | 优先 P 核（2） | 优先 P 核（2） | 12 代以后的 Intel 大小核：主线程优先落在高频 P 核 |
| **核心放置策略** | 自动（0） | 自动（0） | 交给 Windows 自己按负载挑核 |
| **性能提升阈值** | 0 | 0 | 升频最积极 |
| **性能降低阈值** | 100 | 100 | 降频最迟钝 |
| **系统散热方式** | 主动 | 主动 | 宁可风扇转快，也不靠降频降温 |
| **空闲禁用** | 1 | 1 | 禁止处理器进入深度空闲（该项默认隐藏，程序会自动解锁后再改） |
| **延迟敏感度提示** | 开启 | 开启 | 让系统知道当前对延迟敏感 |

同时把电源方案切成 **卓越性能**（本机没有就自动建一个副本并命名为 `VALORANT High Frequency`，
再退而用 **高性能**）。**原来用的方案不会被改坏** —— 切回原方案由还原功能负责。

> 只对**交流电（AC）**生效：插电玩才有效果，用电池时保持系统原样。
> 如果本机从来没有过「卓越性能」方案，程序会新建一个；还原时会把它删掉。

### 5.2 游戏相关

- **Game Mode（游戏模式）** 开启：前台游戏优先拿 CPU 资源。
- **Game DVR / 后台录制** 关闭：录制会偷 CPU 并拉低频率。
- **全屏优化** 关闭：降低无边框全屏下的输入延迟。
- **PowerThrottling 全局关闭**：防止 Windows 给游戏线程降优先级省电。
- **GPU 图形首选项**：给 `VALORANT-Win64-Shipping.exe` 标记「高性能」（独显）。
  程序会自动探测 Riot 默认安装路径；装在别处时该项会跳过（日志里会写明），
  也可以手动到「设置 → 系统 → 屏幕 → 显示卡」里给游戏 exe 指定「高性能」。

### 5.3 进程级优化

对 `VALORANT-Win64-Shipping` / `VALORANT` / `vgc` 等目标进程：

- 优先级提到 **High**；
- 通过 `SetProcessInformation(ProcessPowerThrottling)` 关掉**该进程**的 EcoQoS 节能节流
  （StateMask=0 即关闭，**不影响其它程序**）；
- 混合架构 CPU 上把游戏进程绑到 P 核（会先校验掩码合理性，避免误绑到 1~2 个核上）。

这些只作用于**当前正在运行的进程**，程序退出后新启动的游戏不受影响。

---

## 6. 针对 Core Ultra 7 155H

假如你的 CPU 是 Meteor Lake：**6 个 P 核（Redwood Cove，带超线程）+ 8 个 E 核（Crestmont）
+ 2 个 LP-E 核 = 22 线程**。程序启动时会读 WMI + 注册表识别它，并在界面上打印；
识别为混合架构时会**自动开启大小核相关优化**。

| 155H 特性 | 程序怎么用 |
| --- | --- |
| **HWP / Speed Shift**（硬件自主调频） | EPP 是这里最关键的旋钮。默认「均衡」给 EPP=30，极限模式给 0（最偏性能），比单纯锁频率更贴合 Intel 的调频逻辑 |
| **P 核 / E 核异构** | 异构调度策略设为「优先 P 核」，让游戏主线程落在高频 P 核上；瓦罗兰特是吃单核的，这一项比全核频率更重要 |
| **核心停泊（core parking）** | 停放最小/最大都设 100%，禁止核心「关掉睡觉」，减少唤醒延迟 |
| **混合架构线程调度** | 读 `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel\KGroups\00` 的 `GroupMask` 判断是否混合架构；`pin` 命令会把游戏进程绑到 P 核 |
| **笔记本功耗墙** | ⚠️ 见下一节：OEM 的 PL1/PL2 限制是程序改不动的 |

> **注意**：P 核 / E 核掩码是按 **155H 的实际拓扑**写死的。
> 换到别的 CPU 上，程序会走「识别 → 校验 → 不匹配就跳过」的路径，不会乱绑；
> 但想让它适配你的型号，得改 `one\Core.cs` 里的掩码常量再重新编译。

---

## 7. 三个必须知道的前提

### 1. 这个程序是把频率顶到上限，不是提升 CPU 性能上限

对瓦罗兰特这种吃单核的游戏，真正的大头是：单核睿频、**内存**（笔记本若能换/开 XMP 就开、
确认双通道）、以及后台干净程度。程序负责消除「系统主动降频」这一层损耗。

### 2. 155H 笔记本真正的瓶颈通常是 OEM 功耗墙（PL1/PL2），程序改不动它

Meteor Lake-H 的持续功耗由厂商的 DTT（Dynamic Tuning）服务和 BIOS 决定，
典型情况是 PL1 被压在 28~45W。电源设置只能保证「系统不主动降频」，
**不能突破厂商设的功耗上限**。

如果散热压不住，锁高频反而会因撞温度墙而掉频 —— 这种情况请用「保守」模式，
或把 EPP 调到 30~50、最小状态降到 80~90。

想真正放开功耗墙，需要厂商的性能模式（联想 Legion Space / 华硕 Armoury Crate / 微星 Center 等）
或 BIOS 里的 PL1/PL2 设置，也可以在 ThrottleStop / Intel XTU 里调 —— 那属于超频工具，风险自负。

### 3. 部分设置项在某些机器上不存在

比如被 OEM 精简的电源方案。程序会**跳过**不存在项并在日志里写明，不会因此中断；
电源方案文件的结构差异也已做了容错。

---

## 8. 项目结构

```
valorant-cpu-boost\
├─ one\                     C# 引擎（当前版本）
│   ├─ Core.cs              引擎：电源方案读写、进程增强、注册表、命令行，约 148 KB
│   ├─ Ui.cs                托盘 / 守护 / WinForms 老界面 / 命令行入口，约 144 KB
│   ├─ build-engine.ps1     引擎编译脚本（纯 ASCII）
│   └─ cli\                 命令行样例输出（审计 / 探测记录）
│
├─ flutter-app\             Flutter 界面（当前版本）
│   ├─ lib\
│   │   ├─ main.dart        应用外壳、左侧导航、5 个页面
│   │   ├─ engine.dart      引擎命令行桥接（约 24 KB，最厚的一块）
│   │   ├─ topology.dart    混合架构拓扑识别
│   │   ├─ hardware.dart    硬件信息读取
│   │   ├─ app_state.dart   全局状态
│   │   ├─ theme.dart       Material You 配色
│   │   ├─ pages\           home / check / bench / game / log
│   │   └─ widgets\         卡片、数据表格、模式选择、导航项、按压按钮
│   ├─ windows\runner\      Win32 外壳
│   └─ tool\
│       ├─ make-portable.ps1    打绿色版
│       └─ make-installer.ps1   打安装版（内嵌 payload.zip）
│
├─ installer\               安装器 / 卸载器（C#）
│   ├─ Vcb.cs               共用常量与工具（应用名、版本号、路径、卸载逻辑）
│   ├─ Installer.cs         安装流程（八步）
│   ├─ Uninstaller.cs       卸载流程（七步）
│   ├─ Launcher.cs          schtasks /run 的静默跳板
│   └─ *.manifest           两个 requireAdministrator 清单
│
├─ src\                     ⚠️ 旧版 v2（C# WinForms 单体），保留备用
├─ release\                 成品输出（按版本分目录，见下）
├─ backup\                  引擎运行期生成：原始电源方案 .pow 备份
├─ boost-log.txt            引擎运行日志
├─ boost-backup.txt         「还原初始设置」的还原点
│
├─ fix-encoding.ps1         给源码补 UTF-8 BOM
└─ build.ps1 / bootstrap.ps1  旧版 v2 的编译链
```

### `release\` 的版本目录

成品按版本号分目录，**新版本自动进新目录**，老版本原地保留：

```
release\
├─ v1.0\
│   ├─ install.exe       安装器（自带内嵌 payload）
│   ├─ uninstall.exe     独立卸载器
│   ├─ portable.zip      绿色版压缩包
│   ├─ README.md         随包说明
│   └─ portable\         绿色版解压后的样子
└─ v1.1\                 （以后）
```

版本号只有一个来源：**`installer\Vcb.cs` 的 `AppVersion`**。
打包脚本会读它、去掉结尾的一个 `.0`、加上 `v` 前缀：

| `AppVersion` | 目录名 |
| --- | --- |
| `1.0.0` | `v1.0` |
| `1.1.0` | `v1.1` |
| `1.0.1` | `v1.0.1` |
| `2.0.0` | `v2.0` |

> 刻意**不**取 `major.minor` —— 否则补丁版 `1.0.1` 会被算成 `v1.0`，
> **静默覆盖**已经发布的 1.0。

---

## 9. 从源码构建

### 依赖

| 需要 | 用途 |
| --- | --- |
| **Flutter SDK** | 编译界面 |
| **Visual Studio 2022+**（含 C++ 桌面开发负载） | Flutter Windows 构建 |
| **Roslyn `csc.exe`** | 编译引擎（装了 VS 就有；没有会自动回退到系统自带的 .NET Framework `csc.exe`） |
| Windows 10 / 11 | 运行 |

引擎**不需要** .NET SDK，也**不需要**安装 .NET Framework 运行时（Win10/11 自带 4.x）。

### 顺序

```powershell
# 1. 编译引擎 → ValorantBoost.exe
powershell -ExecutionPolicy Bypass -File one\build-engine.ps1

# 2. 编译界面
cd flutter-app
flutter build windows --release

# 3. 打绿色版 → release\<版本>\portable\ + portable.zip
powershell -ExecutionPolicy Bypass -File tool\make-portable.ps1

# 4. 打安装版 → release\<版本>\install.exe + uninstall.exe
powershell -ExecutionPolicy Bypass -File tool\make-installer.ps1
```

`build-engine.ps1` 的可选参数：

```powershell
-File one\build-engine.ps1 -Out D:\some\path\ValorantBoost.exe
-File one\build-engine.ps1 -Sources one\Core.cs,one\Ui.cs
-File one\build-engine.ps1 -Compiler fx        # 强制用系统自带 csc
```

它会打印编译器退出码和产物大小，并把**完整原始编译输出**写到 `<输出目录>\build-engine.log`。

`make-installer.ps1` 的 `-SkipPortable` 可以复用已经打好的 `portable\`，跳过第 3 步。

### 发新版本

1. 改 `installer\Vcb.cs` 的 `AppVersion`（和 `flutter-app\pubspec.yaml` 的 `version`）
2. 跑上面四步 → 自动生成 `release\v1.1\`
3. 到 GitHub 建同名 tag 的 Release，把 `install.exe` / `portable.zip` / `uninstall.exe` 传上去

---

## 10. 卸载 / 完整还原

这两件事**不一样**：

### 只还原优化（保留程序）

界面点 **④ 一键还原**，或：

```powershell
.\ValorantBoost.exe restore
```

会把原始电源方案 `.pow` 整体导回 → 切回你原来的方案 → 删掉本次新建的临时方案 →
注册表各项恢复默认 → 删除状态文件。

### 卸载程序

「设置 → 应用」里找到 **游戏 CPU 高频优化器** → 卸载，或直接运行安装目录里的 `uninstall.exe`。

卸载器会依次做七件事（**每步尽力而为，某步失败只提示、不中断**）：

| 步骤 | 做什么 |
| --- | --- |
| 1/7 | 关闭正在运行的界面进程 |
| 2/7 | 删除 `ValorantBoostUI` 提权启动项 |
| 3/7 | 调 `ValorantBoost.exe uninstall` —— 停掉守护进程、取消登录自启、删 `ValorantBoostWatcher` 任务 |
| 4/7 | 收回 Windows Defender 排除项 |
| 5/7 | 从「设置 → 应用」列表里移除 |
| 6/7 | 删除桌面快捷方式 + 开始菜单项 |
| 7/7 | 删除安装目录 |

**刻意保留的**：

- `%LOCALAPPDATA%\valorant_boost_flutter` —— 界面的设置（配色种子）。那是**用户数据**不是程序文件，重装还能接着用。要清干净就手动删。
- 电源方案、游戏配置 —— 卸载器**不碰**。要还原电源设置，先跑一次 `restore`。

---

## 11. 排错

| 现象 | 原因 / 处理 |
| --- | --- |
| 界面点「游戏专项」是空的 | 跑的可能是旧版本 exe；v1.0 起该页会显示登记状态 / 守护状态 / 配置文件 |
| 托盘双击弹出**老界面**（深色 WinForms 窗口） | 旧版本才有这个 bug；v1.0 起托盘改为拉起 Flutter 界面 |
| 命令行输错命令，弹出老界面 | 参数不在白名单里，掉进了兜底分支。核对第 4 节的命令名 |
| 「无法修改电源方案 / 拒绝访问」 | 必须以管理员运行。安装版自带提权，绿色版要手动右键 → 以管理员身份运行 |
| 界面徽章显示「未提权」 | 这次不是以管理员启动的。关掉，从桌面快捷方式（或计划任务）重新拉起 |
| 日志里大量「跳过」 | 正常：该设置项你的机器不支持 |
| 游戏更烫但帧数没变 | 你是 GPU 或内存瓶颈 → 换「保守」模式或直接还原 |
| 优化后反而掉帧 / 撞温度墙 | OEM 功耗墙或散热压不住。见第 7 节第 2 条 |
| 托盘图标是**黑方块** | 旧版本 bug。Win32 的 `Icon.FromHandle` 不保留 alpha，透明区会渲染成黑色 —— v1.0 起图标改成整块铺满 |
| 想确认生效 | 任务管理器 → 性能 → CPU，看「速度」是否稳定贴近或高于「基准速度」；或 HWiNFO 看核心有效频率 |
| Vanguard 报错 | 程序不注入、不 hook 游戏进程，只改系统电源设置和注册表；如遇报错先 `restore` 再排查 |
| 卸载后目录没立刻消失 | 正常：卸载器自己在目录里，会写一个延迟批处理，2~3 秒后再删 |
| 卸载了但桌面快捷方式还在 | 手动删，或确认卸载时没有别的窗口占用 |

---

## 12. 编码约定

这个项目踩过好几次编码的坑，规则都写在这里 —— **改文件前先看这一节**。

### `.ps1` 脚本

| 脚本 | 要求 | 原因 |
| --- | --- | --- |
| `fix-encoding.ps1`、`one\cli\` 之外的一般脚本 | **UTF-8 带 BOM** | PowerShell 5.1 读到**没有 BOM** 的 `.ps1` 会按 ANSI/GBK 解码，中文注释变乱码，还会报出**跟代码本身毫无关系**的「幽灵语法错误」，比如 `Unexpected token '}' in expression or statement`，指向的行其实是完全合法的 |
| `one\build-engine.ps1`、`make-portable.ps1`、`make-installer.ps1` | **纯 ASCII，无 BOM** | 这几个是构建入口，必须在任何环境下都能被正确解析 —— 干脆不写中文，从根上免疫 |

> ⚠️ 有些编辑器（和某些自动改写工具）会**悄悄吃掉 BOM**。改完 `.ps1` 记得验证前三个字节是 `EF BB BF`。

> ⚠️ **反过来也成立：拿 `Get-Content` / `Select-String` 去看一个无 BOM 的 UTF-8 文件，PS 5.1 同样按 GBK 解码。**
> 中文会读成别的字（`游` U+6E38 → `嫓` U+5A13），更阴的是**行数会凭空变少** ——
> GBK 的双字节字符会吞掉紧跟在它后面的换行符，一份 537 行的文件会被读成 403 行，
> 于是「第 494 行」这种定位全部指错地方。
> 要检查无 BOM 的 UTF-8 文本（比如本 README），请用 `rg`，或
> `[IO.File]::ReadAllText($p)`；非要用 `Get-Content` 就加 `-Encoding UTF8`。

### `.bat` / `.cmd` 批处理

**绝不能出现中文。**

`cmd.exe` 是**边读边解析**批处理文件的，而且用的是「读取那一刻」的控制台代码页；
文件偏移量按旧编码算，于是在文件中间就会错位 —— 哪怕开头就写了 `chcp 65001` 也救不回来。

后果是 cmd 把中文句子的**碎片当成命令去执行**：

```
'Boost.exe' 不是内部或外部命令，也不是可运行的程序或批处理文件。
```

（`Boost.exe` 其实是 `ValorantBoost.exe` 被拦腰截断后的尾巴 ——
截在哪儿取决于错位了多少字节，所以每次看到的碎片都可能不一样。）

加 BOM 也没用，**cmd 不认 BOM**。所以本项目的 `.bat` / `.cmd` 全部只写英文，
中文提示一律交给 `.ps1` 打印。

### C# 源码

| 文件 | 要求 |
| --- | --- |
| `one\Core.cs`、`one\Ui.cs`（当前引擎） | **UTF-8 无 BOM** —— 编译时靠 `build-engine.ps1` 传的 `/codepage:65001` 指定编码 |
| `src\*.cs`（旧版） | **UTF-8 带 BOM** —— 用系统自带 `csc.exe` 编译，没有 BOM 会被按 GBK 读成乱码 |
| `installer\*.cs` | **UTF-8 带 BOM** —— `make-installer.ps1` 会自动补 |

### 日志写入

**不要**用 `Tee-Object -FilePath` 写日志。PowerShell 5.1 的 `Tee-Object` 底层走 `Out-File`，
默认编码是 **UTF-16LE** —— 每个 ASCII 字符后面跟一个 NUL 字节，文件会被判定成二进制，一个字都读不出来。

用显式编码：

```powershell
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::AppendAllText($logPath, $line, $utf8NoBom)
```

### 计划任务 XML

`schtasks /create /xml` 读的任务定义**必须是 UTF-16 带 BOM**，否则中文会乱码或直接创建失败。

### Git

本仓库设了 `core.autocrlf false` —— 源码有「必须无 BOM / 必须纯 ASCII」这类**字节级**硬约束，
不能让 git 在检出时动换行符。

---

## 许可

个人项目，自用为主。参考、借鉴随意；因使用本工具造成的任何后果请自行承担 ——
它会真的改你的电源方案和注册表（虽然都能还原）。
