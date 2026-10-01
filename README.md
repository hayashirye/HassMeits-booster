# 无畏契约 (VALORANT) CPU 高频优化程序 · v2

把 CPU 从「按需降频」改成**尽量保持高频**，并针对 **Intel Core Ultra 7 155H（Meteor Lake 混合架构）**
做了专门适配：EPP 能效偏好、P 核优先调度、大小核停泊策略、瓦罗兰特进程优先级 / EcoQoS 节流。
**所有改动全部先备份，一键即可完整还原。**

---

## 1. 怎么用（只看这一节就够）

### 第一步：生成 exe

双击 **`编译程序.bat`** → 等十几秒 → 目录里出现 **`ValorantCpuBoost.exe`**。

过程完全不用你操心，窗口会依次显示（提示已经全部是中文了）：

```
============================================================
   无畏契约 CPU 高频优化器  -  正在生成可执行程序
============================================================

[1/5] 正在检查源码文件编码（UTF-8 BOM）...
  [ok]      ValorantCpuBoost.cs
  [ok]      MainForm.new.cs
  [ok]      app.manifest
  [fixed]   UTF-8 BOM added: build.ps1
[2/5] 正在查找 C# 编译器（csc.exe）...
      编译器：C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
[3/5] 正在检查 .NET Framework 引用程序集...
[4/5] 正在编译（大约 5 - 20 秒，请稍候）...
[5/5] 完成。

================ 生成成功 ================
  文件：D:\...\ValorantCpuBoost.exe
  大小：xxx KB
=========================================

下一步：双击目录里的 ValorantCpuBoost.exe（Windows 会弹出管理员权限请求，点「是」）

Press any key to close this window...
```

编译结束时还会**弹一个中文结果框**告诉你是成功还是失败 —— 所以就算窗口被挡住、或者你手快关掉了窗口，也不会不知道结果。

> 用的是 Windows 自带的 C# 编译器（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），
> **不需要安装 Visual Studio，也不需要 .NET SDK**。它会一次编译 `src\` 下的两个源码文件
> （`ValorantCpuBoost.cs` 引擎 + `MainForm.new.cs` 界面）。编译完就可以把 `src\`、`build.ps1`、
> `bootstrap.ps1`、`fix-encoding.ps1`、`编译程序.bat` 全部删掉，exe 是独立运行的。
>
> 窗口**不会闪退**：成功或失败都会停下来等你看完，并按任意键才关闭。

**为什么要有 `fix-encoding.ps1` 这一步：** 程序界面是中文的，源码里有大量中文字符串。
系统自带的 C# 编译器靠文件开头的 BOM 判断编码——没有 BOM 时它可能按 GBK 去读 UTF-8 文件，
结果是界面变成乱码、甚至直接编译报错。这一步在编译前自动给源码补上 UTF-8 BOM，重复运行无副作用。

同样的问题也发生在 **`.ps1` 脚本**上（Windows PowerShell 5.1 读到没有 BOM 的 `.ps1` 会按 GBK 解码，
中文变乱码，还会报出**跟代码本身毫无关系**的「幽灵语法错误」，比如
`Unexpected token '}' in expression or statement`，指向的行其实是完全合法的）。
所以 `build.ps1`、`bootstrap.ps1`、`valorant-cpu-boost.ps1` 也都在 `fix-encoding.ps1` 的修复清单里。

**一条容易踩的规则：`.bat` 文件里绝不要写中文。** `cmd.exe` 是**边读边解析**批处理文件的，
而且用的是「读取那一刻」的控制台代码页；文件偏移量按旧编码算，于是在文件中间就会错位——
哪怕开头就写了 `chcp 65001` 也救不回来。后果是 cmd 把中文句子的**碎片当成命令去执行**，例如：

```
'puBoost.exe' 不是内部或外部命令，也不是可运行的程序或批处理文件。
```

（`puBoost.exe` 其实是 `ValorantCpuBoost.exe` 被拦腰截断后的尾巴。）
加 BOM 也没用，**cmd 不认 BOM**。所以本项目的两个 `.bat` 都刻意只写英文，
中文提示全部放在 `.ps1` 里打印。

### 第二步：运行

双击 **`ValorantCpuBoost.exe`**（会自动弹 UAC 请求管理员权限，点「是」）。界面是深色标题栏 +
左侧导航的可视化窗口，左上角有「● 管理员 / ● 未提权」徽章，右边是实时频率和占用率两个仪表盘。

上手顺序：

1. 先切到左侧 **「设置明细」** —— 只读页面。这里把 15 项设置逐条列出来：左边是项目名称和
   它为什么能提高频率，右边是进度条（绿色填充 = 应用后会到的位置，竖线 = 你机器当前的值），
   最右边标着 **已应用 / 待应用 / 本机不支持**。页面上方还有三个强度可选，**点一下就能看到
   每一项会变成多少**，不会真的改任何东西。
2. 挑好强度（默认 **均衡**，推荐先试这个），点 **「应用这套设置」**（或仪表盘上的 **① 应用优化**）。
3. 开游戏后点 **② 增强游戏进程** —— 把正在跑的瓦罗兰特进程提到高优先级 + 关掉它的节能节流。
   （只影响当前这次运行；下次开游戏再点一次，或者直接用下面的命令行模式。）
   顶部标题栏右侧也有一个「一键增强游戏进程」按钮，随时可点。
4. 左侧 **「曲线日志」** 页能看最近 60 秒的 CPU 占用率和频率双曲线，以及完整的操作日志。
5. 不想要了：点 **④ 一键还原**。

### 命令行模式（不弹界面）

```powershell
.\ValorantCpuBoost.exe check      # 只读体检
.\ValorantCpuBoost.exe apply      # 均衡模式应用
.\ValorantCpuBoost.exe apply max  # 极限高频（地板 100% + EPP 0）
.\ValorantCpuBoost.exe apply safe # 保守（不动频率地板 + EPP 25）
.\ValorantCpuBoost.exe tune       # 只增强正在运行的游戏进程
.\ValorantCpuBoost.exe restore    # 一键还原
```

---

## 2. 文件说明

| 文件 | 作用 |
| --- | --- |
| `编译程序.bat` | **双击这个生成 exe**（纯英文 + 纯 ASCII 的单行启动器，原因见本节最后一条注意；调用 bootstrap.ps1，失败时停住等你看结果） |
| `bootstrap.ps1` | 中继：先修文件编码，再把活交给 build.ps1（中文提示从这里开始） |
| `build.ps1` | 编译脚本：补源码 BOM → 找 csc.exe → 查引用 → 编译（失败自动重试一次）→ 弹中文结果框 |
| `fix-encoding.ps1` | 给源码和脚本补 UTF-8 BOM，防止中文变乱码 / 幽灵语法错误（另两个脚本会自动调用，可单独运行） |
| `src\ValorantCpuBoost.cs` | C# 源码（引擎：电源方案读写、进程增强、注册表、命令行模式，约 1590 行） |
| `src\MainForm.new.cs` | C# 源码（可视化界面：导航 / 仪表盘 / 15 项设置对比 / 60 秒曲线 / 日志，约 1185 行） |
| `src\app.manifest` | 清单：`requireAdministrator` 提权 + 高 DPI 感知 + Win10/11 兼容声明 |
| `ValorantCpuBoost.exe` | **编译产物，双击运行的就是它**（编译后才有） |
| `backup\` | 运行后自动生成：原始电源方案 `.pow` 备份 + 上次改动报告 |
| `restore-state.json` | 还原所需的状态记录（**.exe 还原全靠它，别删**） |
| `以管理员运行.bat` + `valorant-cpu-boost.ps1` | v1 的纯 PowerShell 版，保留备用（见第 8 节） |

---

## 3. 针对 Core Ultra 7 155H 做了什么

你的 CPU 是 Meteor Lake：**6 个 P 核（Redwood Cove，带超线程）+ 8 个 E 核（Crestmont）+ 2 个 LP-E 核 = 22 线程**。
程序启动时会读 WMI + 注册表识别它，并在界面上打印；识别为混合架构时会**自动开启大小核相关优化**。

| 155H 特性 | 程序怎么用 |
| --- | --- |
| **HWP / Speed Shift**（硬件自主调频） | EPP 是这里最关键的旋钮。默认「均衡」给 EPP=30，极限模式给 0（最偏性能），比单纯锁频率更贴合 Intel 的调频逻辑 |
| **P 核 / E 核异构** | 异构调度策略设为「优先 P 核」，让游戏主线程落在高频 P 核上；Valorant 是吃单核的，这一项比全核频率更重要 |
| **核心停泊（core parking）** | 停放最小/最大都设 100%，禁止核心「关掉睡觉」，减少唤醒延迟 |
| **混合架构线程调度** | 读 `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel\KGroups\00` 的 `GroupMask` 判断是否混合架构；命令行 `tune` 模式会把游戏进程绑到 P 核（会先校验掩码合理性，避免误绑到 1~2 个核上） |
| **笔记本功耗墙** | ⚠️ 见第 5 节：OEM 的 PL1/PL2 限制是程序改不动的，需要额外处理 |

---

## 4. 它到底改了什么

### 4.1 电源方案（15 项，全部走 `powercfg /export` → 改二进制 → `/import`）

程序**不解析 `powercfg /query` 的文本输出**（中文/英文系统文本不同，容易失配），而是把方案导出成
`.pow` 文件、直接在字节层面改值、再导回去 —— 跨语言、跨 Windows 版本都稳。

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
> 注意：如果本机从来没有过「卓越性能」方案，程序会新建一个；还原时会把它删掉。

### 4.2 游戏相关

- **Game Mode（游戏模式）** 开启：前台游戏优先拿 CPU 资源。
- **Game DVR / 后台录制** 关闭：录制会偷 CPU 并拉低频率。
- **全屏优化** 关闭：降低无边框全屏下的输入延迟。
- **PowerThrottling 全局关闭**：防止 Windows 给游戏线程降优先级省电。
- **GPU 图形首选项**：给 `VALORANT-Win64-Shipping.exe` 标记「高性能」（独显）。
  程序会自动探测 Riot 默认安装路径；装在别处时该项会跳过（日志里会写明），
  也可以手动到「设置 → 系统 → 屏幕 → 显示卡」里给游戏 exe 指定「高性能」。
- **进程级优化（`tune` / ② 按钮）**：
  - `VALORANT-Win64-Shipping` / `VALORANT` / `vgc` 优先级提到 **High**；
  - 通过 `SetProcessInformation(ProcessPowerThrottling)` 关掉**该进程**的 EcoQoS 节能节流
    （StateMask=0 即关闭，不影响其它程序）；
  - 混合架构 CPU 上把游戏进程绑到 P 核。
  - 这些只作用于**当前正在运行的进程**，程序退出后新启动的游戏不受影响。

---

## 5. ⚠️ 三个必须知道的前提

1. **这个程序是把频率顶到上限，不是提升 CPU 性能上限。**
   对瓦罗兰特这种吃单核的游戏，真正的大头是：单核睿频、**内存**（笔记本若能换/开 XMP 就开、确认双通道）、
   以及后台干净程度。程序负责消除「系统主动降频」这一层损耗。

2. **155H 笔记本真正的瓶颈通常是 OEM 功耗墙（PL1/PL2），程序改不动它。**
   Meteor Lake-H 的持续功耗由厂商的 DTT（Dynamic Tuning）服务和 BIOS 决定，
   典型情况是 PL1 被压在 28~45W。电源设置只能保证「系统不主动降频」，
   **不能突破厂商设的功耗上限**。如果散热压不住，锁高频反而会因撞温度墙而掉频 ——
   这种情况请用「保守」模式，或把 EPP 调到 30~50、最小状态降到 80~90。
   想真正放开功耗墙，需要厂商的性能模式（联想 Legion Space / 华硕 Armoury Crate / 微星 Center 等）
   或 BIOS 里的 PL1/PL2 设置，也可以在 ThrottleStop / Intel XTU 里调 —— 那属于超频工具，风险自负。

3. **部分设置项在某些机器上不存在**（比如被 OEM 精简的电源方案）。
   程序会跳过不存在项并在日志里写明，不会因此中断；电源方案文件的结构差异也已做了容错。

---

## 6. 排错

| 现象 | 原因 / 处理 |
| --- | --- |
| 双击 `编译程序.bat` 一闪而过 | 正常情况下窗口会停在「Press any key to close this window...」；若真闪退说明 cmd 被执行策略限制，在本窗口手动跑 `powershell -NoProfile -ExecutionPolicy Bypass -File .\bootstrap.ps1` |
| 窗口提示找不到 `csc.exe` | 系统不是 Win10/11，或 .NET Framework 被精简过；装个 **.NET Framework 4.8 运行时**后重试（中文提示框里也写了） |
| 中文界面变成乱码 / 报 `error CS` | 源码编码问题：先单独跑一次 `fix-encoding.ps1`，再重新编译 |
| 报 `Unexpected token '}' in expression or statement` 之类、指向的行明明是对的 | 这是 **`.ps1` 缺 UTF-8 BOM** 导致的「幽灵语法错误」（PowerShell 5.1 按 GBK 读 UTF-8 脚本，中文注释会吞掉后面的花括号/引号，解析器失步）。跑一次 `fix-encoding.ps1`，或直接双击 `编译程序.bat` / `以管理员运行.bat`，它们启动时会自动修 |
| 黑窗里出现 `'puBoost.exe' 不是内部或外部命令` 这类怪报错 | **`.bat` 里混进了中文**（cmd 边读边解析、代码页切换时偏移量错位，把句子碎片当命令执行）。`.bat` 必须纯 ASCII；本项目的两个 `.bat` 已经改成纯英文 |
| 窗口提示 `生成失败 / BUILD FAILED` | 看上面以 `error CS` 开头的那几行，复制发给开发者即可定位 |
| 编译成功但界面是乱码/方块 | 源码编码问题：先单独跑一次 `fix-encoding.ps1`，再重新编译 |
| 双击 exe 没弹 UAC | 说明 `app.manifest` 没被编进去，重新用 `编译程序.bat` 编译 |
| 「无法修改电源方案 / 拒绝访问」 | 必须以管理员运行；exe 自带提权清单，正常会弹 UAC |
| 标题栏徽章显示「未提权」 | 说明这次不是以管理员启动的。关掉程序，右键 →「以管理员身份运行」（exe 清单本来会弹 UAC，若被安全软件拦下就会这样） |
| 仪表盘数字显示 `--`，曲线是空的 | 还在采第一个样本（约 1 秒）或采样被拦；「设置明细」页的频率信息仍可正常使用 |
| 日志里大量「跳过」 | 正常：该设置项你的机器不支持 |
| 想确认生效 | 任务管理器 → 性能 → CPU，看「速度」是否稳定贴近或高于「基准速度」；或 HWiNFO 看核心有效频率 |
| 游戏更烫但帧数没变 | 你是 GPU 或内存瓶颈 → 换「保守」模式或直接还原 |
| 还原后仍有残留 | 电源方案按「导出原始 .pow 整体导回」还原，最干净；若状态文件被删，可手动 `powercfg /setactive SCHEME_BALANCED` 回到平衡 |
| Vanguard 报错 | 程序不注入、不 hook 游戏进程，只改系统电源设置和注册表；如遇报错先还原再排查 |

---

## 7. 卸载 / 完全还原

界面上点 **④ 一键还原**，或者：

```powershell
.\ValorantCpuBoost.exe restore
```

还原会做四件事：把原始电源方案 `.pow` 整体导回 → 切回你原来的方案 → 删掉本次新建的临时方案 →
注册表各项恢复默认 → 删除状态文件。

之后：

```powershell
powercfg /setactive SCHEME_BALANCED   # 可选：确认回到平衡
```

最后删掉整个 `valorant-cpu-boost` 文件夹即可（无服务、无驱动、无开机自启）。

---

## 8. 附：v1 纯 PowerShell 版（备用）

`以管理员运行.bat` + `valorant-cpu-boost.ps1` 是第一版实现，功能覆盖大致相同，
但**依赖 `powercfg /query` 的中文/英文文本解析**，在不同语言的 Windows 上可能失配；
新版 exe 已改用二进制 `.pow` 方式规避这个问题。

如果 exe 编译不出来（比如机器上真的没有 csc.exe），还可以用这个：

```powershell
# 双击 以管理员运行.bat 走中文菜单；或命令行：
powershell -NoProfile -ExecutionPolicy Bypass -File .\valorant-cpu-boost.ps1 -Action check
powershell -NoProfile -ExecutionPolicy Bypass -File .\valorant-cpu-boost.ps1 -Action apply
powershell -NoProfile -ExecutionPolicy Bypass -File .\valorant-cpu-boost.ps1 -Action watch    # 常驻守护，自动给游戏提优先级
powershell -NoProfile -ExecutionPolicy Bypass -File .\valorant-cpu-boost.ps1 -Action restore
```

它的备份文件是 `valorant-cpu-boost.backup.json`，日志 `valorant-cpu-boost.log`。
两套实现的备份互相独立，各自还原各自的改动。
