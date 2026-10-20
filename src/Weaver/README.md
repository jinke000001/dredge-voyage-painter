# DREDGE Mac 离线钩子补丁器

用途：为本项目中的特定 Mac 游戏程序集副本增加固定、受审计的调用。不启动游戏，不写原始 `Contents`，不改存档，不提供通用补丁能力。默认模式仍为原外观组件；商人传送选择 `--merchant-travel`，航行体验选择 `--voyage-experience`。航行模式包含原四个传送钩子，三种模式均不合并外观组件。

本包 plist 声明 1.5.3，BuildInfo / 原生标题为 v1.5.4 prod / build 2993。兼容性不依赖版本文本，输入 SHA-256 必须为 `a071ffebe51514af5f661055f126a5041acda127623ee7c681c6ef2233a24729`，MVID 必须为 `d810863b-fda8-4056-bdde-77f7c349ae63`。

默认外观模式唯一允许的变更：

| 原方法 | 每个正常返回前插入 |
|---|---|
| `PlayerColorCustomizer.RefreshBoatColors()` | `ldarg.0; call void Jinke.DredgeSkins.Bootstrap::OnColors(object)` |
| `PlayerColorCustomizer.RefreshBoatFlag()` | `ldarg.0; call void Jinke.DredgeSkins.Bootstrap::OnFlag(object)` |

运行组件必须叫 `Jinke.DredgeSkins.dll`，类型 `Jinke.DredgeSkins.Bootstrap` 及两个方法均为 `public`；方法必须为 `static void`，只接受一个 `System.Object`。补丁器复用游戏已有的 `mscorlib` 引用，只增加运行组件本身的 AssemblyRef。

商人传送模式唯一允许的变更：

| 原方法 | 插入位置与调用 |
|---|---|
| `SteamManager.Awake()` | 第一条原指令之前：`call void Jinke.DredgeMerchantTravel.Bootstrap::EnsureIsolatedProfile()` |
| `SaveManager.Init()` | 第一条原指令之前：`call void Jinke.DredgeMerchantTravel.Bootstrap::EnsureIsolatedProfile()` |
| `PlayerColorCustomizer.RefreshBoatColors()` | 每个正常返回之前：`ldarg.0; call void Jinke.DredgeMerchantTravel.Bootstrap::OnPlayer(object)` |
| `AbilityBarUI.OnCurrentPressComplete()` | 第一条原指令之前：`ldarg.0; call bool Jinke.DredgeMerchantTravel.Bootstrap::BeforeAbilityComplete(object); brtrue 原首指令; ret` |

运行组件身份必须为 `Jinke.DredgeMerchantTravel`，公开类型为 `Jinke.DredgeMerchantTravel.Bootstrap`，入口为 `public static void EnsureIsolatedProfile()`、`public static void OnPlayer(object)` 与 `public static bool BeforeAbilityComplete(object)`。两处隔离检查分别保护 Steam 重启重定向和存档初始化；检查失败时，异常直接传播，原方法的指令不会执行。能力入口返回 `true` 时从原首指令继续，返回 `false` 时直接返回，让目的地轮盘接管这次输入；确认后仍可由运行组件重新进入原方法执行原版校验、施法和历史记录。所有前置调用在原异常处理区段之外，原分支与 `try`/`catch` 边界不变。传送模式不注入旗帜方法，也不添加外观组件引用。

运行组件可以引用原 `Assembly-CSharp`，这是组件调用游戏接口所需的托管程序集引用；补丁器不遍历或解析这个引用环，仅验证入口签名和新增调用。

航行体验模式在四个传送钩子之外，固定增加以下十个目标方法，仍使用同一个 `Jinke.DredgeMerchantTravel.Bootstrap`：

| 原方法 | 固定调用合同 |
|---|---|
| `SerializedCrabPotPOIData.AdjustDurability(float)` | 唯一维护加成 getter 返回后，调用 `float AdjustPotMaintenance(float, object)` |
| `TrawlNetAbility.Update()` | 唯一维护加成 getter 返回后，调用 `float AdjustNetMaintenance(float, object)` |
| `PlayerSanity.Update()` | 唯一 `UnityEngine.Time.get_deltaTime()` 返回后，调用 `float AdjustSanityDeltaTime(float, object)` |
| `TooltipSectionDeployableDetails.RefreshUI()` | 两处原 `LanguageManager.FormatTimeStringForDurability` 调用追加 `this`，替换为 `FormatDeployableDurability(LanguageManager, float, float, LocalizeStringEvent, string, object)` |
| `OccasionalGridPanel.RefreshDurabilityUI()` | 蟹笼面板唯一原耐久格式化调用使用相同的 `FormatDeployableDurability` 代理，保留进度条计算 |
| `NetPanel.RefreshUI()` | 拖网面板唯一原耐久格式化调用使用相同的 `FormatDeployableDurability` 代理，保留进度条计算 |
| `SpiralMinigame.GenerateTargets()` | 每个正常返回前调用 `AfterSpiralTargets(object)` |
| `SerializableGrid.Init(GridConfiguration, bool)` | 每个正常返回前调用 `AfterGridInitialized(object)` |
| `SaveData.Init()` | 每个正常返回前调用 `AfterSaveDataInit(object)` |
| `UpgradeManager.OnUpgradesChanged(UpgradeData)` | 每个正常返回前调用 `AfterUpgradesChanged(object)` |

三个浮点变换仅接收原返回值和原 `this`，不读取或修改其他栈值；callee 的完整签名、程序集及出现次数必须精确匹配。提示文本代理保留原调用的全部参数；审计移除追加的 `this` 并还原原 `callvirt` 后，整个方法体必须完全等于原版。包内静态代理负责原参数调用一次的运行时语义，离线可执行 fixture 验证该转发合同。

`SaveData.Init` 的原 `foreach` 包含 `try/finally`。只有这一固定目标允许异常区段，且所有 `ret` 必须位于全部异常区段及边界之外；注入不移动任何异常边界。审计拒绝把钩子纳入异常边界的变更。

原包实际六个 MaxStack 头部变化限定为：蟹笼 `5→6`、拖网 `4→5`、理智 `4→5`、装备提示 `5→6`、蟹笼面板 `5→6`、拖网面板 `5→6`。这是传入 `this` 所需的一个临时栈位置，不是游戏数值变化；其余所有方法的头部保持不变。航行构建门禁只接受这六个精确变化。

新独立包使用 `tools/build_voyage_experience.py`，默认名称为 `DREDGE 航行体验版.app`，所有目标路径必须不存在。其固定存档身份为 `local.jinke.dredge.voyage.qa.20261018`，与已交付商人版和正式档分别隔离。复用构建器在 fresh staging 编译，源码指纹包括 `src/VoyageExperience`；没有旧 `bin` 兜底。新 guard 只对原 guard 的三个身份声明做次数为一的受限替换并进行 Python 编译检查。

## 构建与使用

在项目根目录执行（要求 .NET 10 SDK）：

```sh
dotnet restore src/Weaver/Weaver.csproj --locked-mode
dotnet build src/Weaver/Weaver.csproj -c Release --no-restore
dotnet src/Weaver/bin/Release/net10.0/Weaver.dll \
  'Contents/Resources/Data/Managed/Assembly-CSharp.dll' \
  'output/new-copy/Assembly-CSharp.dll' \
  'src/CosmeticPack/bin/Release/net472/Jinke.DredgeSkins.dll'
```

传送模式（运行组件必须由独立构建流程生成）：

```sh
dotnet src/Weaver/bin/Release/net10.0/Weaver.dll --merchant-travel \
  'Contents/Resources/Data/Managed/Assembly-CSharp.dll' \
  'output/merchant-travel-new-copy/Assembly-CSharp.dll' \
  'output/fresh-merchant-build/Jinke.DredgeMerchantTravel.dll'
```

输出必须是本工作区 `research/` 或 `output/` 下尚不存在的文件。工作区根据原始 `Contents/Resources/Data/Managed/Assembly-CSharp.dll` 定位。输出 DLL 和同路径 `.audit.json` 都不可覆盖。源码路径和输出路径相同、原包路径、符号链接逃逸、错误指纹、重复补丁、错误运行组件签名都会拒绝。

验证先在内存进行，全部通过后才创建输出。返回钩子会将所有分支及 `switch` 指向旧 `ret` 的目标改指向新 `ldarg.0`，确保执行钩子；若这些方法含异常处理区段、或插入会导致短跳转溢出时直接拒绝。前置钩子保留所有原分支与异常区段目标；审计拒绝任何跳回前置钩子的分支，或将前置钩子纳入原异常区段的改动。

## 审计范围

补丁序列化后重新读取，比较整个程序集：

- 所有原方法体的操作码、操作数、局部变量、初始化标记、异常处理区段。类型/方法/字段采用名称和程序集身份规范化，分支采用指令序号，避免 metadata token 重排产生误报。
- 所选模式的目标方法必须恰好包含指定位置和签名的钩子（外观 2 个方法；当前轮盘传送 4 个方法）。条件前缀的布尔方向、原首指令目标、传入对象和新增返回均逐一检查。移除钩子并归一化分支后，方法体必须与原版完全相同；其他所有原方法体完全不变。
- 类型、字段、常量、方法定义、参数、泛型约束、属性、事件、自定义属性、声明式安全信息、托管资源、程序集身份、模块身份等必须一致。
- 原 AssemblyRef 全部保留，新增必须恰为运行组件。MaxStack 头部单独记录；本包实测 0 个变化。

审计文件含所选模式、原版 MVID、原版/补丁/运行组件文件哈希、目标方法原/后/剥离钩子后的语义哈希、未变化方法体数量、程序集引用变更、元数据语义哈希及验证时间。失败退出码为 1，不输出可用补丁。

这证明了补丁器只添加指定调用；它不能单独证明运行组件的隔离检查正确或传送机制安全。运行组件仍需独立源码复核，以及 Mac 原生场景和存档往返验证。

## 测试

```sh
dotnet run --project src/Weaver/tests/Weaver.Tests.csproj -c Release -- "$PWD"
```

测试使用自制运行组件及原包的只读副本，结果写到 `output/weaver-tests/<唯一目录>/test-results.json`。这些 fixture DLL 只用于测试，不能安装到游戏。测试覆盖分支到 `ret`、`switch`、短跳转溢出、异常区段、额外 IL/常量/资源/引用变更、指纹与输出路径约束，以及模式串用、合法游戏引用环和隔离检查的前置位置。自制可执行 fixture 验证检查失败时，原存档/Steam 代码以及原异常处理器均未执行；检查成功时原方法正常执行。没有运行原游戏。

2026-10-16 已在原包静态验证：总计 9,375 个方法体，9,373 个保持原样，两个指定方法各增加一次调用；字段/定义/资源语义一致、MaxStack 变化为 0。证据：`research/evidence/weaver-runtime/Assembly-CSharp.dll.audit.json`。未运行游戏。

2026-10-18 两种模式 38 项测试通过：默认外观模式仍为 9,373 / 9,375 个原方法体不变；传送模式为 9,372 / 9,375 个原方法体不变，其余 3 个剥离指定钩子后完全相同。两者字段/定义/资源/MVID 语义不变、MaxStack 变化为 0；原程序集哈希保持一致。证据：`output/weaver-tests/20261018T005514-7ab945a4/test-results.json` 及同目录两份审计文件。

2026-10-18 轮盘入口扩展后 47 项离线测试通过：当前传送模式为 9,371 / 9,375 个原方法体不变，4 个目标方法剥离钩子后与原版完全相同，MaxStack 变化为 0。新增可执行 fixture 验证传入对象身份、`false` 阻止原方法、`true` 恰好执行原方法一次；变异测试拒绝条件反转、跳过原首指令、修改原方法、错误返回类型和跳回前缀。证据：`output/weaver-tests/20261018T034508-b944680f/test-results.json`。fixture 不是游戏运行证据；已封装的旧 3 钩子版本仍按各自原有签名和清单验证，新构建门禁只接受当前 4 钩子合同。
