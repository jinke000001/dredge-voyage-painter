# DREDGE Mac 指定内容包：独立机制边界复核

日期：2026-10-16，已更新至 QA06/QA07 与最终本机交付副本。范围：原包反编译与资产引用、生产组件及补丁器源码/实际 DLL、原生 QA 报告与回读哈希、导出 PNG、原生截图，以及最终包文件和签名。复核者未启动游戏，未读取用户实际存档，未修改原始 `Contents`；只更新本文。本文区分静态证据、在 Mac Unity 进程中的组件测试、主 agent 执行的原生键盘操作，以及尚未执行的自然游戏流程。

2026-10-17 补充：下文保留 10 月 16 日独立复核的历史证据与覆盖范围；本日用户已确认正式版实际装备旗帜并切换三套均显示正常。Kimi 报告的逐项复核、构建工具修复及本日验证另见文末，不把新反馈追记为旧 QA 探针或截图的通过结果。

版本口径补正：原包 `Info.plist.CFBundleShortVersionString` 和 `PlayerSettings.bundleVersion` 为 1.5.3；独立读取原包 `gamemetadata_assets_all_3e1c7ab7c626e829061dd7917c37e3fd.bundle` 中 BuildInfo（pathID `3256321420031457649`）则为 VersionMajor=1、VersionMinor=5、VersionRevision=4、BuildNumber=2993。`VersionLabelUI` 使用 BuildInfo 而非 plist。主 agent 后续提供的供给包 QA02 原生标题截图 `evidence/native-ui/01-qa-title.png` 已由本复核者实际查看，显示 **v1.5.4 prod | build2993**。本文此前“1.5.3”是包元数据标签，不能用作通用版本兼容承诺。实际支持范围是本次提供并验证的 Mac 包，以及固定 `Assembly-CSharp.dll` SHA-256 `a071ffebe51514af5f661055f126a5041acda127623ee7c681c6ef2233a24729`。

## 结论与当前状态

仅在 `PlayerColorCustomizer.RefreshBoatColors()`、`RefreshBoatFlag()` 正常返回前追加视觉后置调用，在现有 0 号材质槽设置外观属性，符合本包已观察到的职责边界。不能改船型、换整套船模型/材质、修改升级或装备数据，也不能将原版 `BoatFlagStyle=0` 强制显示。当前组件对船体/屋顶颜色与旗帜贴图的选择保存在独立 sidecar，不需要把自定义 ID 写入原版存档。

独立确认了 **5 档船、20 个颜色 Renderer、5 个旗面 Renderer、每档 4 个破损网格**的引用覆盖。原版灯窗写入船 Renderer 的 1 号材质槽，当前外观覆盖明确使用 0 号槽。未在原 `Assembly-CSharp` 全部反编译代码内找到任何 `MaterialPropertyBlock` / `GetPropertyBlock` / `SetPropertyBlock` 写者；此结论不扩展到未逐个反编译的引擎或其他插件程序集。

最终生产 DLL 为 `a2cba006…72d40e2`。已修正输入穿透、Mac 素材路径、Unity 原生 JSON 对新增类型的字段遗漏，以及入口遮挡原版 Esc 提示。最终组件在原生可恢复暂停状态开放；外观窗期间临时持有并恢复导航、选择和输入管理器 enabled，不写暂停值或动作层。

QA06/QA07 已取得原生组件级证据：20 个颜色 Renderer、5 个旗面 Renderer 的四组选项检查通过；独立设置经生产 Select 写入并在新进程读回；三主题窗口与限定键盘隔离/恢复流程有原生截图。正式包与 QA 使用相同生产 DLL，正式包文件与签名复核通过。未发现已测试支持范围内需阻止本机交付的缺陷，但自然挂旗动画、升级/破损/装备全流程、鼠标与手柄等仍未覆盖；四张离屏旗面网格预览为空白，不能算视觉通过。完整游戏机制不变不能由静态 IL 或这些局部测试单独证明。

## 原机制与外观交界

| 交界 | 本包证据 | 外观组件应保持的边界 |
|---|---|---|
| 原版颜色/旗帜 | `decompiled/all/PlayerColorCustomizer.cs:38` 初始订阅和刷新；`:86` 颜色刷新写 `materials[0]`；`:110` 起的旗帜刷新按 `style != 0` 控制显示，并用原版材质列表选旗 | 后置覆盖只改实际渲染值，保留原始刷新、原生启停状态、原生列表 |
| 升级 | `Player.cs:254` 按 tier 在 5 个已有 BoatModelProxy 中启用一个；`UpgradeManager.cs:69` 会重排库存、写 HullTier、重算耐久阈值；`PlayerColorCustomizer.cs:57` 仅未自行改色时更新原版默认色 | 不触发升级，不伪造 tier，不改 `HasChangedBoatColors`。所有 inactive renderer 也提前覆盖，因此切船沿用视觉值 |
| 破损 | `BoatSubModelToggler.cs:59` 按损坏格/耐久阈值选择 `DamageStateMeshes`，`:79` 只替换同一 MeshFilter.mesh；`:80` 保留临界损坏特效；`Player.cs:307` 决定死亡 | 对同一 Renderer 的颜色块无需跟随破损另换模型。不得修复损坏、覆盖临界特效或隐藏破损表现 |
| 装备 | `BoatSubModelToggler.cs:94` 起按装备数量启停组件、区分普通/高级灯及普通/虹吸/材料网；`:165` 按原版 Icebreaker 装备状态显示；`:83` 按鱼/饰品库存显示箱子 | 不触碰装备列表、子物体启用或网类型。染色破冰器只覆盖原版配置中已有的部分 |
| 航速/钓鱼/灯光数值 | `PlayerStats.cs:178` 耐久阈值由 tier 与配置计算；`:221` 钓鱼类型/速度；`:276` 发动机速度；`:291` 灯具流明/范围，均来自原版库存和装备状态 | 不写 PlayerStats、装备、配置或任何数值方法 |
| 灯光画面与理智 | `BoatModelProxy.cs:65` 对船 `materials[1]` 写 `_LightStrength`；`LightAbility.cs:63` 起同时启用灯、光束与理智修正；`LightFlickerEffect.cs:42` 起持续调整亮度 | 必须使用 `SetPropertyBlock(block, 0)`，不能对整个 Renderer 设置含 `_LightStrength` 的块、不能替换整套材质；不得改变灯/光束/理智组件 |
| 移动、停靠与存档 | `PlayerController.cs:153` FixedUpdate 使用原版 Input 和统计速度；`Player.cs:320` Dock、`:336` Undock 会原生保存并控制移动 | 外观窗仅借用原生暂停；不调用 Dock/Undock/Save，不改 IsMovementAllowed 或刚体 |
| 原版收集解锁 | `DredgeDialogueRunner.cs:309,347` 写漆/旗拥有状态；`:639` 改色写原版颜色索引及 HasChangedBoatColors；`:655` 写旗帜 style | 不调用这些命令来设置自定义外观；原版收集与任务继续独立工作 |

`PlayerColorCustomizer.OnEnable` 会再次刷新全部列表；因此重载场景可自然重新注册新 Renderer。升级后若玩家已改过原版色，不会再次触发原版颜色刷新，但列表中的未来船已经覆盖，静态引用链完整。自然升级/切档及其完整回退流程仍属于未覆盖的后续回归范围，不能由 Renderer 注册数替代。

## 五档对象与旗帜 UV 的独立核验

直接用 UnityPy 1.25.4 读取：

`Contents/Resources/Data/StreamingAssets/aa/StandaloneOSX/player_assets_all_aa53fb6d97843ab38da4bba4ff0c59f9.bundle`

对象位于 `CAB-0f6116e74fb758ecb3351432d4c6329b`。先解析 GameObject→Transform→Renderer→Material，再交叉比对 Customizer 和 Player 的序列化引用，未仅依据文件名推断覆盖。

| 档位/路径 `PlayerContainer/Player/…` | 主体 MeshRenderer pathID | 0 号材质 | 同档颜色附属 | 破损 Mesh 列表 |
|---|---:|---|---|---|
| Boat1 | 8000577939978729422 | SmallBoat_Mat | 1 个 IcebreakerModelPart2、2 个 bunting | Boat1_Sturdy / Ok / Poor / Critical |
| Boat2 | -1212510481368327014 | Boat2_Mat | 1 个 IcebreakerModelPart2、2 个 bunting | Boat2_Sturdy / Ok / Poor / Critical |
| Boat3 | -2178653307881444816 | Boat3_Mat | 1 个 IcebreakerModelPart2、2 个 bunting | Boat3_Sturdy / Ok / Poor / Critical |
| Boat4 | -6790555591171226989 | Boat4_Mat | 1 个 IcebreakerModelPart2、2 个 bunting | Boat4_Sturdy / Ok / Poor / Critical |
| Boat5 | -3468682762928022503 | Boat5_Mat | 破冰器和彩旗实际复用 **Boat4_Mat** | Boat5_Sturdy / Ok / Poor / Critical |

5 个主体的 1 号槽均为 `BoatWindows_Mat`。Customizer.meshRenderers 数量 10，buntingMeshRenderers 数量 10；故不能以“搜索 Boat5_Mat”代替 Renderer 引用覆盖，否则漏掉 5 档附属物。

5 个 `BoatN/FlagAccessory/Flag` 的 SkinnedMeshRenderer 均使用同一个旗面网格 `Flag`（pathID `8019196355970058770`），初始材质均为 `Flag2_Mat`。旗杆是单独 MeshRenderer，不在旗面列表中。自定义纹理应覆盖旗面 Renderer 的属性 `Texture2D_9aa7ba2263944b48bbf43c218dc48459`。

对 `research/extracted/Flag.obj` 独立解析的结果：90 顶点、90 UV、128 三角面；45 个位置/UV 各复制成一对相反法线，两个面使用同一组 UV。旗面 UV 为 **u=0.00798034668…0.991210938，v=0.305419922…0.993652344**；v 增大对应旗面向上，u 增大对应本地 z 增大。结合 Boat1 的旗面 z 偏移 -0.038204655 与旗杆范围，u 接近 1 是靠旗杆一端。另一侧图案会镜像，不能承诺文字两面均正读；中央符号/无文字图案适合该模型。

原版 7 张旗图均为 256×256；独立读取 `FlagPole` 网格 UV 为 u=0.251464844…0.758300781、v=0.118591309…0.122497559。实现把新图映射到 y=76…255，保留底部 76 行，覆盖旗面所需 v 范围且不侵入旗杆 UV 区。旗杆本来就不是覆盖对象，保存下方区域仍是合理的纹理布局约束。QA06 已独立查看原生 GPU 读回的三张图集，图案方向及底部保留区符合该布局；自然挂旗正反面、风动和日夜视觉仍未验证，OBJ/图集检查不能代替这些场景。

## MaterialPropertyBlock 与当前运行组件

`src/CosmeticPack/RendererOverrides.cs` 对每个 Renderer 使用 `GetPropertyBlock(block, 0)` / `SetPropertyBlock(block, 0)`，保留块内其他属性，只写 `_Hull_Color`、`_Roof_Color` 或旗纹理。它没有写槽 1，也没有调用 renderer.materials 来替换材质、改变 flag GameObject 的启停或原版 SaveData。`SkinController.RegisterField` 直接遍历原始三个列表，包含 inactive 对象。

“恢复原版”写回原材料当前值或捕获到的现存块值；不是清空整个块，因此不会删除同槽其他属性。后续原生刷新钩子可刷新回原版材质当前颜色/旗图。但 Unity 的每材质槽 MPB 优先于整个 Renderer 的 MPB；如果以后另装会写全 Renderer MPB 的插件，它与本方案的组合并未验证，不能宣称通用兼容。参考 [Renderer.SetPropertyBlock](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Renderer.SetPropertyBlock.html)、[Renderer.GetPropertyBlock](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Renderer.GetPropertyBlock.html)。

`SkinAssets` 保留原旗纹理布局，在内存中生成 atlas，`SettingsStore` 只写 `~/Library/Application Support/JinkeDredgeSkins/settings.json` 或显式测试路径。未发现对原版 SaveData、画师解锁、库存或能力的写调用。PNG 失败应回落原版外观，但运行 DLL 缺失/不兼容可能在 CLR 解析新调用时就失败，无法靠 Bootstrap 内部 try/catch 兜底；交付必须包含匹配的 DLL，并以构建哈希和原生启动检查该配套关系。

## 输入隔离：证据、修正要求与恢复边界

原生正常暂停链为 `GamePauseListener.cs:61` → `GameManager.PauseAndShowSettings`（`:266`，timeScale=0、IsPaused=true、CanUnpause=true、PauseChange事件）→ `DredgeInputManager.cs:196` / `:220` 缓存当前动作层并切 SYSTEM。恢复通过 `ResumeGameInput` 重置动作后回到缓存层。地图等 `PopupWindow.PauseLite` 不发同一暂停事件，不应当作可替代入口。

因此只读 **IsPlaying && IsPaused && CanUnpause** 是合适起点：排除主菜单、普通地图轻暂停、结束状态及原版确认/控制器掉线弹窗。`PopupDialog.cs:55` 和 `PopupControllerDropout.cs:25` 会把 CanUnpause 置 false，外观窗应立即收起并释放占用。原生异常/焦点切换后仍应做实际恢复检查。

另外直接用本地 `tools/dotnet/ilspycmd -t InControl.InControlInputModule Contents/Resources/Data/Managed/InControl.dll` 读到了以下真实路径：

1. `Process()` 用 `eventSystem.sendNavigationEvents` 包住方向/Submit/Cancel，可临时禁导航；`SendUpdateEventToSelectedObject()` 在条件外，需暂存/清空选中对象并谨慎恢复。
2. 鼠标处理独立于导航，需最高层自有 Canvas + GraphicRaycaster + raycastTarget Image；仅 IMGUI 的 Event.Use 或移动按钮位置不能防止原生点击。
3. `ProcessDrag` 使用缓存的 pointerDrag。若原生滑条正在拖动再按 F8，新增挡板不能中止已有拖动；打开外观窗前应拒绝任一鼠标按钮按住/触摸进行中，待释放后才能打开。
4. `TabbedPanelContainer` 用 DredgePlayerActionPress 派发 TabLeft/Right，`SettingsDialog` 的 slider Back、ControlMappingContainer 的 UnbindControl 也直接走 SYSTEM，不经过 EventSystem。仅禁导航/清选择不足。最小方案是窗口期间暂存并禁用 **当前 DredgeInputManager 的 Behaviour.enabled**，关闭时恢复该同一实例原值。该类无 OnEnable/OnDisable 副作用；不需要改其 activeActionLayer/cachedActionLayer、暂停或运动字段。
5. InControl 的原始按键绑定监听仍独立运行，打开前应拒绝 `Controls.IsListeningForBinding`。外观窗开启期间自行处理 Escape 关闭，再交回原生暂停菜单，不在自身关闭时调用原版 Unpause。

所有临时占用必须在正常关闭、失去安全状态、异常、OnDisable、OnDestroy 均释放。只恢复捕获的同一输入实例/EventSystem 的原值；选中对象须仍存活且 active，并避免覆盖新弹窗已经选中的控件。已确认输入停用可保留动作层；最终原生键盘路径另有证据，但这仍不等于鼠标拖动、所有异常恢复和手柄路径均已验证。

最终实现已包含上述门控、持有与释放路径，正常关闭还等待 Esc/F8/鼠标释放后至少下一帧再交还输入。复核曾发现 Dispose 固定不恢复选中项；已修为仍在安全暂停时走条件式恢复，失去安全状态时不抢占原生新窗口选择，并由 finally 恢复输入和销毁挡板。

一个应准确描述的原生 UI 行为：清空选中项会触发 `SliderDisabler.OnDeselect`，退出控制器滑条的内部编辑状态。之后恢复选中对象不等于恢复该编辑子状态；不应强行写回原生滑条/动作监听。关闭外观窗后应能重新正常选中和操作滑条；该滑条/手柄操作仍未原生验证。最终已验证的 E 分页恢复不能扩写成这项也通过。

最终入口移到右上；独立对比 `947bf185…` 与 `a2cba006…` 的实际 DLL 反编译，差异仅入口/提示位置、打开时隐藏入口及相应透明命中区定位。IMGUI 的右上 128×32 按钮与 Canvas 的 anchor/pivot(1,1)、边距16（均乘同一 scale）一致。实际截图 `evidence/native-ui/06-entry-fixed.png` 证实与右下原生 Esc 提示分离。

## 正式补丁、最终包与配套身份

独立审读 `src/Weaver/Program.cs`、`Patcher.cs`、`SemanticAudit.cs` 及测试实现。Program 固定原版 SHA，只输出新文件，拒绝原包路径、已存在输出/审计、重复补丁与符号链接逃逸。Patcher 只允许两个视觉刷新方法，在正常返回前追加 `ldarg.0; call`；直达 ret 的分支/switch 改指向钩子，异常区域和短跳转溢出拒绝。SemanticAudit 在序列化后重读，比较定义/元数据与所有方法体，并要求剥离钩子后恢复原语义。

最终 `output/builds/DREDGE 外观版/Assembly-CSharp.dll.audit.json` 仍为 **9,373/9,375 方法体语义相同，两处白名单钩子剥离后相同，新增一个生产程序集引用，MaxStack 头变更为 0**。该审计的 `sourceGame` 沿用 plist 的“1.5.3”标签；兼容判断以固定指纹为准，游戏画面版本为 1.5.4 build2993。IL 白名单证明原代码的改动范围，不能单独证明新增组件或整体玩法正确。

| 最终身份 | SHA-256 |
|---|---|
| 原包 Assembly-CSharp.dll | `a071ffebe51514af5f661055f126a5041acda127623ee7c681c6ef2233a24729` |
| 正式 Assembly-CSharp.dll | `3cfc648cd27e6ebdae52748b3112cb31aa928d794fbaa9ac80a6779606c4dc2a` |
| 正式及 QA 共用 Jinke.DredgeSkins.dll | `a2cba00650e7e6bf6044b76120c7ed57f5d4e4d11ee0245d00f6cfa5072d40e2` |
| 仅 QA 的 NativeProbe.dll | `61fa091756efc55a23270ed2b2efd3d9c2adcd95e1689ed9164c54bc1ad58c2a` |
| 仅 QA 的 Assembly-CSharp.dll | `bcb29ee5eaa8e30fd04c3953ab049f4147510e121359fec818f310b9658760de` |

本复核者独立重算最终 `output/DREDGE 外观版.app` 与原 `Contents` 的全部对应文件（排除 `._*`、`.DS_Store`）：原包 355 个文件，副本 361 个，无缺失；差异仅原生执行文件签名、Assembly-CSharp.dll 和 CodeResources。新增仅生产 DLL、manifest、三张 flag.png、Resources/steam_appid.txt。三张 PNG 与 manifest 均逐文件匹配自有素材；Info.plist 与原包逐字节相同。正式包未包含 NativeProbe 文件；其 Assembly 指纹与正式语义审计相符，没有 QA 引用。结果与 `evidence/final-package-audit.json` 一致。

再次独立执行 `codesign --verify --deep --strict` 和启动器 `zsh -n` 均退出 0。这是本机 ad-hoc 签名/文件检查，不是公证、公开发行或正式包使用用户存档的原生运行验证。原生测试使用独立 QA 身份，其生产 DLL 与正式包相同；为保护用户进度，未启动正式包读取用户档。

`tools/build_local.py` 的输出路径检查在创建前解析 dest/staging/launcher，要求落在本项目 output 中且不存在；启动器以独占创建避免覆盖。构建仅复制原包，在新副本内补丁和签名；QA 身份修改只在 `--qa` 分支。实际正式启动器先拒绝并行 DREDGE 进程，要求 Steam 已启动，在 Resources 工作目录读取本地开发 appid `1562430` 后执行本副本。Steam 身份认证/许可证代码没有修改。独立外观设置默认写 `~/Library/Application Support/JinkeDredgeSkins/settings.json`，不会借用游戏进度文件。

## 已解决问题与旧证据的有效范围

| 阶段 | 实际发现与处理 | 旧证据如何使用 |
|---|---|---|
| 首版输入 | IMGUI 不能阻止 uGUI 与 SYSTEM 快捷；加入暂停/改键/指针门控、透明射线挡板和可恢复输入持有，修复 Dispose 的条件式选中恢复 | 旧 QA01 不能证明最终输入正确；使用最终 a2 原生键盘记录 |
| QA02 路径 | 本 Mac 的 dataPath 指向 Contents，拼接 StreamingAssets 错误；改用 `Application.streamingAssetsPath` | 旧 FAILSAFE 只能证明当时失败；最终 READY 与三主题显示由新证据确认 |
| 日志 | 原版 `GameManager` 关闭 Unity 全局 Debug logger；新增独立有界日志 | 每份日志最多128 KiB，仅一份 previous，消息最多4096字符，按 UTF-8 字节轮换；I/O失败不改变游戏 |
| QA03 JSON | manifest.skins 为 null；旧报告遗漏 renderers/effective 等复杂字段，旧 Same 可能比较两个被截空的对象 | **旧 JsonUtility 报告中的 native/renderer/effective true 均不能作为通过证据**；从 v2 完整快照+回读 sidecar 重新建立证据 |
| QA04 输出目录 | exFAT 自动生成 `._autostart`，触发空目录保护 | 仅忽略 basename `._*` 与精确 `.DS_Store`；真实结果和其他隐藏文件仍拒绝覆盖。该次 renderer 报告明确失败 |
| 入口位置 | 原右下入口遮住原生 Esc 提示 | 最终只调整右上入口和匹配命中区；截图06已确认，不改变输入捕获/释放 |
| QA05 浮点与预览 | 颜色恢复的严格十进制相等失败；四张旗面离屏 PNG 实为纯背景 | 仅有效 RGBA 改为1e-6比较并记录最大差；其他 Same 保持严格。新增像素覆盖检查，空白图片明确 unavailable |

生产解析已改用原包既有 Newtonsoft.Json（程序集13.0.0.0、文件13.0.2），无需改 Unity 的 ScriptingAssemblies 清单。`SkinJson` 使用显式 `JsonSerializer.Create`，避免全局 DefaultSettings 污染；TypeNameHandling.None、元数据忽略并由键白名单拒绝、重复键/尾部内容/深度与实际 UTF-8 字节上限、显式 schema 校验。清单上限64 KiB，设置上限4 KiB；只允许已知主题标识。生产 `SkinAssets.Load`、`SettingsStore.Read/Write` 直接接入同一实现。

Unity JsonUtility 使用原生 Unity 序列化规则，对不支持内容可能忽略；此次新增程序集的复杂字段丢失已由原生报告观察到。原生类型注册/缓存是符合症状的推断，未证明引擎内部根因，不将其写作已证实 Unity 缺陷。参见 [Unity JSON serialization](https://docs.unity3d.com/2021.3/Documentation/Manual/JSONSerialization.html)。

已执行并独立核验的生产纯托管测试为 **67 个断言**，包含16种船/旗组合持久化往返、实际素材清单、解析边界及日志。另独立审读 Weaver 的20项测试实现/记录，以及最终 Probe 的18项测试实现和 `src/NativeProbe/Tests/results/20261016T130653-875a23/checks.json`。这些分别验证其限定风险，不将数量相加当成游戏全流程通过比例。

## QA 探针、自动加载与证据可信度

最终 Probe、PatchHelper 的源码与实际 DLL 已独立反编译核对。QA 在正式补丁之外，再追加 `RefreshBoatColors→Probe.AfterColors` 和 `ContinueOrNewButton.Start→Probe.AfterTitle` 两调用；`output/builds/DREDGE-Skins-QA-05/Assembly-CSharp.dll.qa-only.audit.json` 记录剥离后恢复正式补丁语义；已核对它与原 QA 输出审计逐字节相同。QA 审计的9373个未改方法是**相对正式补丁**；相对原始游戏合计三个方法有追加调用，不能混淆两种统计。探针及自动加载器不进入正式包。

自动加载器只在显式开关、精确 QA bundle/company/product、显式隔离根与实际 persistentDataPath/save strategy 路径一致、无软链接、既有有效槽、非空存档及原版 DLC 双资格检查后，一次调用 `LoadGameFromTitle(false)`。它不调用 OnClick/OnSubmit，不创建新档、不绕过资格；这是测试场景加载，不是 UI 点击。原生加载流程可能正常写隔离 QA 档，不能说整个 QA 进程完全只读。

渲染探针调用生产组件 `ConfigurePreview(..., persist=false)`，同步检查 original/ramen/octopus/moon，再恢复初始选择。仅可选的设置检查在主报告通过并完整回读后、显式 PREFS_CHECK 开启且精确 QA 身份/独立 qa-settings 路径成立时，调用生产 `Select("ramen", "moon", true)` 和生产 Read。初始文件已同目标会拒绝；必须文件新建或 SHA 变化才能通过。它刻意保留 QA 选择用于下一进程测试，不操作原生存档、暂停、窗口或输入。该调用不是鼠标选择证据。

报告使用只序列化显式 DTO public fields 的独立 Newtonsoft 序列化器，Color仅RGBA、Vector3仅xyz；已有敏感性测试能识别嵌套材质、灯光、颜色和旗帜hash变化。主报告必须有 v2、25个渲染器/有效前后快照和4组选项；落盘后重新解析、对比并写 SHA sidecar。**sidecar.passed 只说明报告完整读回；必须同时检查主报告 passed、error 和对应子项。**

## 原生 QA06 / QA07 的独立核验结果

两轮均由本机 Apple M2、Unity 2021.3.5f1 实际进程产生，应用路径为 `output/runtime/DREDGE-Skins-QA-05.app`；06/07是报告轮次，不是应用文件名。自动加载 fixture 均报告精确隔离身份、loadCallCount=1、资格通过和 world-ready。复核者直接读取实际 JSON、重算文件哈希并查看 PNG/截图，未只接受汇总状态。

| 项目 | QA06：生产设置写入轮 | QA07：新进程读取轮 |
|---|---|---|
| 主报告 SHA-256 | `5adccf5bcc1a7826e99f1fd32c272c2d9a286a8b88f3bdb43561190fc965ca8b` | `c326be5099d3c34c4fd173986e35d9d4c258d12045d83112024d4b9b48deb592` |
| 主报告 / 回读 SHA | 通过 / 匹配 | 通过 / 匹配 |
| 初始船色、旗帜 | original、original | ramen、moon |
| 完整数组 | 25 Renderer、25 effectiveBefore、25 effectiveAfter、4组选项 | 同左 |
| 四组选项 | 每组20船/5旗颜色或纹理检查通过；原生状态及共享材质检查一致 | 同左 |
| 原生字段/材质/初始选择恢复 | 通过 | 通过 |
| 有效颜色最大绝对误差 | `1.1920928955078125e-7`，小于1e-6 | `0`，前后有效快照完全相同 |
| 离屏旗面网格预览 | 四张均0个非背景像素，**不可用** | 同左 |

实际证据位于 `evidence/native-probe-06/`、`evidence/native-probe-07/`。QA06 的25个 Renderer 快照包含30份材质、650条材质属性、55条 `_LightStrength` 覆盖记录；完整嵌套内容存在，不再出现旧报告的空对象假通过。

颜色容差仅作用于 EffectiveState 的 hull/roof RGBA；rendererId、category、旗帜整图RGBA SHA、原生状态和共享材质均保持严格比较，NaN/Infinity拒绝。QA06 最大值已由复核者按float32重算，与报告完全相同。原版是线性色彩空间；恢复原版走 Material.GetColor→MPB.SetColor→探针MPB.GetColor，Unity明确在这组接口间进行sRGB/线性转换。观察到的差异仅1–2个float32步长，符合往返舍入；不需要为这点误差改生产恢复路径。测试确认1e-4的RGB/alpha差仍失败，一般 Same 不放宽。参见 [SetColor](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/MaterialPropertyBlock.SetColor.html)、[GetColor](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/MaterialPropertyBlock.GetColor.html)。

### 独立外观设置与冷启动

QA06 `native-settings.json` SHA为 `ecb8a6e3c0ee5576f7fe2132284f21e68d2a8826ed44419c316e4eef5a273f4f`，与回读 sidecar匹配。原文件不存在；生产 Select 创建文件，生产 Read 读到 schema=1、boat=ramen、flag=moon，仅这三个键。原生快照、25个共享Renderer状态及窗口值不变。

复核者读取实际独立 QA 外观设置文件，SHA为 `825fc18bd5866f7481e34c0b85fd433a2009f9a21e6349e91ab9d8418e837286`。主 agent 的 QA07 启动记录确认未带 PREFS_CHECK；本复核者确认该轮目录没有 native-settings 报告，启动初始诊断已经是 ramen/moon，测试结束恢复同组合，设置文件SHA仍完全相同。这支持生产写入→新进程读取；不是模拟点击，也不证明所有原生游戏存档往返场景。

### GPU 图集、实际画面与未显示的旗面

独立查看 QA06 三张 GPU 读回图集：拉面、罐头章鱼、月航信使对应正确，均256×256。独立用PNG像素重新比较：每张自定义图的上180行共46,080像素改变，下76行差异为0字节；original全图差异为0。全部5个旗Renderer仍visible=false、原生BoatFlagStyle仍0，说明选择自定义主题没有强制挂旗或解锁。

这些是实际GPU纹理读回与布局检查，**不是自然挂旗/风动/正反面视觉通过**。四张离屏网格预览均589,824像素纯背景，已实际复核；报告明确 previewUnavailable、0 non-background、至少5898阈值及错误说明。即使主报告 passed=true，离屏预览仍不可用，不作为皮肤显示证据，也未为修复QA相机扩大到改游戏shader。

截图10可见港口中的船，截图11显示程序选择后拉面船色/月航旗帜的勾选，截图12显示新进程保留同一混合选择；截图已逐张独立查看。港口船体部分受码头遮挡，因此不扩写成三套船色、全角度或旗面动画的完整视觉验收。

### 原生键盘与界面

`evidence/native-ui/final-keyboard-observations.json` 记录主 agent 用 CUA 键盘执行：港口Esc打开暂停→F8打开外观舱→E不改变底层通用页→Esc只关闭外观舱→E正常切到显示页。复核者已查看截图06–09，状态变化与记录一致；三主题图、中文、分离的船/旗按钮可见，右上入口不遮挡右下原生Esc。

键盘执行由主 agent 完成，截图和代码由本复核者独立复核。CUA合成鼠标点击没有被Unity接受，不能记为鼠标选择通过，也不足以认定真实鼠标存在缺陷；选择/保存由上述生产组件调用测试覆盖。手柄、拖动滑条、改键监听、滚轮和失焦恢复尚未原生覆盖。

### 原包、用户档与进程边界

原始 Assembly 指纹仍相同，最终副本差异符合构建白名单。主 agent 的 `evidence/resumed-save-integrity.json` 记录：从用户游玩后重新建立的 **20:28:44 到21:16:56**，真实profile的56个文件无变化、无新增、无缺失（排除系统元数据）。这是该时间窗口的文件哈希比较记录；复核者只读该报告，没有二次读取真实存档，不能外推到用户此前游玩或全部会话。

`evidence/native-final-verification.json` 汇总的06/07哈希、初始选择及实际设置哈希均与本次直接复核相符。检查时未发现DREDGE进程。原包 `GameManager.OnApplicationQuit` 本身调用 `Process.GetCurrentProcess().Kill()`，且方法体不在补丁白名单；因此正常退出可能呈现137，不能单凭此退出码判定外观组件崩溃，也不能忽略实际错误日志。

## 截至 2026-10-16 的动态矩阵与后续回归范围

本节保留早期建议矩阵，更新为实际状态；它不是用户要求逐项跑完全部游戏的清单，也不应因本机交付而全部标为通过。后续只在隔离副本和测试档中补充，不读取或修改用户真实档。

| 项目 | 本次已获得的支持证据 | 仍未覆盖 |
|---|---|---|
| 启动与加载 | 最终生产DLL在Mac QA进程READY、20船/5旗注册、完整06/07报告；正式包配套哈希/签名正确 | 正式身份副本读取用户档的实际运行，跨平台/其他版本 |
| 三主题/原版恢复 | 4组生产组件选择、颜色/纹理断言、GPU图集、初始选择恢复；三主题UI可见 | 三主题自然航行全角度、日夜、自然挂旗正反面/动画 |
| 5档与破损 | 5档20颜色Renderer/5旗引用及全部破损网格静态链；当前进程对全部引用写入检查 | 自然升级、全部破损/修复链、临界特效动态验证 |
| 灯光和装备 | 槽0边界；共享材质全部属性与光照覆盖快照在切主题前后相同 | 开关灯/闪烁、装卸装备/网/破冰器的自然流程 |
| 无旗/原生旗/彩旗 | 原生style0时5旗始终隐藏，无自定义解锁写入 | 有旗时原生换旗、彩旗启停及完整视觉 |
| 输入占用与恢复 | 原生暂停、F8开窗、E隔离、Esc只关层、E恢复；入口无重叠 | 真实鼠标选择、手柄、滚轮、拖动滑条、按住指针、改键监听及航行恢复完整矩阵 |
| 状态转换 | 代码的异常/OnDisable/销毁/失去安全态恢复路径已审 | 失焦/恢复、控制器掉线、新原生弹窗、窗口开着返回标题等 |
| 独立持久化 | 16组合离线往返；原生生产Select新建混合选择文件，QA07新进程读回且不重写 | 所有组合跨进程、完整原生存读档及未补丁隔离原版再读同测试档 |
| 素材失败回退 | 解析边界负测；早期路径失败显示原版FAILSAFE；最终清理/回退代码已审 | 在最终Unity包中专门移除PNG/损坏manifest后的整套操作回归 |

## 当前交付判断

本机指定内容包的可回退外观副本，已完成机制边界分析、素材/运行组件、正式补丁与签名、独立代码复核，以及上述原生组件和键盘验收。没有剩余的已确认代码阻塞；最终包保留原机制入口、原始包和原生存档格式，QA专用代码不随包交付。

该结论限于已记录支持范围。未验证的自然流程、鼠标/手柄和旗面动画，以及不可用的离屏网格预览均如上保留，不宣称“完整游戏机制全部测试通过”。正式包的使用与回退入口见项目README；本复核未新增发布、安装或修改用户存档的操作。

## 2026-10-17：Kimi 旗帜审查的复核与处理

用户指定 `旗帜功能深度审查报告.md` 仅作参考，授权本任务判断是否优化修复。本节按实际源码、指定包的反编译结果和验证记录重新判断，保留原报告不变。结论是修复构建工具使用旧产物的实际缺陷，保留用户已经实玩确认的运行组件；不因未来版本或异常条件的假设而替换当前游戏副本。

| 报告条目 | 核实结果与决定 |
|---|---|
| M1：暂停状态绑定半初始化 | 分步赋值的异常状态确实存在。但本包所需成员齐全，Weaver 对其他游戏 Assembly 指纹拒绝构建；首次注册失败已进入 FailSafe，OnGUI 异常后还会销毁窗口和 guard，并非永远在所有 UI 事件重复失败。全成功后再赋值是未来调整此接口时可做的健壮性改进，本轮保留已验收运行组件。 |
| M2：已装备旗帜的显示未验证 | 旧 QA06/07 确实只覆盖原生 `flagId=0`，空白离屏图仍不可用。用户在本日针对“已装备旗帜并切换三套旗面”的明确问题回答“三套都切换过，显示正常”；核心装备后切换/显示已有用户实机确认。风动、背面、昼夜和特定输入设备仍未逐项确认。 |
| M3：构建静默使用旧 DLL | 确认，且同时涉及 CosmeticPack 与 Weaver。原脚本仅检查旧 `bin` 文件存在。改为在每次新 staging 内真实编译两个项目，编译失败立即终止，再使用新输出做补丁；不以文件修改时间替代编译。验证结果见下节。 |
| L1：图集缓存上限 | 48 上限前已清理无效源纹理/输出，另有周期清理。`7×3=21` 只是原旗图与主题的组合估算，并非实测所有重载路径的上限。未证实正常流程达到 48，保留有界资源与失败回退策略。 |
| L2：InstanceID 复用误中旧缓存 | 该报告路径不成立。缓存 key 命中后仍检查源对象；本包 `UnityEngine.Object.CompareBaseObjects` 在两边非空时使用引用比较（IL `ceq`），不以 InstanceID 判等。新包装对象即使复用 ID 也会替换旧缓存，证据见下文。 |
| L3：注册集合/字典半更新 | 构造 `RendererOverrides` 抛异常时确有集合/字典不一致的条件路径；现有注册入口立即 FailSafe。当前包没有此异常证据，不为假设改动运行组件；在后续涉及注册逻辑的修订中可改为构造成功后再加入集合。 |
| L4：失败后点原版行抹掉偏好 | 不成立。FailSafe 将内存选择置为 original；`SkinWindow.Choice` 的 `&& !selected` 阻止已选原版行调用 Select，因此不写盘；Select 应用失败时也会 return，不进入设置写入。显式“全部恢复原版”是独立的用户重置指令，保留其写盘行为。 |
| L5：QA 与正式 Assembly 不同 | 属于已明确记录的验证边界。QA 有额外两处探针调用，审计限定其差异，生产外观 DLL 完全相同；用户又实际体验了正式副本。无需修改。 |
| L6：只允许两个钩子 | `changes.Count == 2` 是刻意的改动白名单。未来新增钩子应经过审计边界调整，拒绝未审过的第三处改动是正确行为。 |
| L7：画师色被自定义色覆盖、假设的标题船 | 自定义船色本就作为显示覆盖；画师原生选择仍保存，切回原版即可显示。标题船是否存在未证实；10 秒清理只删除失效对象，不能称为会自动撤销仍存活的标题船。均不据此改运行逻辑。 |

L2 的核实对象为原包 `Contents/Resources/Data/Managed/UnityEngine.CoreModule.dll`，SHA-256 为 `5d755181dd82a2d731012d5d8e03eb70912321db264db0f99c66bd53b389425c`，与原包基线和正式副本一致。使用本地 ILSpy 11.1.0.9782，独立复核者读取 C# 及 IL，主 agent 另行反编译核对。`op_Equality` 调用 `CompareBaseObjects`；后者两边均非空的最终分支是：

```il
IL_003c: ldarg.0
IL_003d: ldarg.1
IL_003e: ceq
```

报告其他表述按以下口径使用：语义审计是规范化 IL/元数据等价检查，不能称为整个 DLL 逐字节还原；正式副本新增文件为 6 个而非 7 个；恢复颜色按已记录的 1e-6 容差，不能称为全部浮点位完全相等。“没有 MPB 写者”限于已检查的游戏程序集。已验证原包为 Linear，未新增 Gamma 原生验证或纹理生成耗时测量。

资源 `<2MB` 不是可靠的实测上限：三份 384×256 RGBA 预览像素加三份同尺寸 FlagPixels 已约 2.25 MiB，每份 256×256 RGBA 图集另约 0.25 MiB。这仅是像素存储估算，不包含引擎开销，也不是性能测量。`AtlasLayout.cs` 原注释已经说明旗杆区域；独立 FlagPole 网格的事实及保留底部 76 行的设计无需改动。真实存档 56 文件不变仍只限于上文记录的历史时间窗口，不能解释成游戏正常游玩不会保存进度。

### M3 构建修复的验证范围

`tools/build_local.py` 保留原始游戏指纹、输出路径及拒绝覆盖检查。新流程在全新的 `output/builds/<名称>/managed` 下，以 Release、`--no-incremental` 和 `--artifacts-path` 编译 CosmeticPack 与 Weaver；bin/obj 均与既有项目产物隔离，Weaver 的依赖仍用锁文件恢复。只接受新目录的两个 DLL，构建失败或缺少新产物立即退出，不回退旧 bin。`managed-build.json` 及最终 `build.json` 记录 SDK、实际命令、生成 DLL 的路径和 SHA；这些哈希标识构建产物，不宣称是源码摘要。

新增 `tools/tests/test_build_local.py` 的回归覆盖两项目的编译失败、成功源码变更、既有输出/错误名称、符号链接逃逸、错误原包指纹及新产物缺失。关键负例在独立 fixture 保留有效旧 DLL，加入 `#error` 并把源文件时间回拨到旧 DLL 之前：新脚本因该编译错误终止，原脚本却继续使用旧 DLL 完成织入并走到游戏复制边界。成功用例向两项目分别添加常量，确认新 DLL 含该常量、旧 DLL 不含且哈希未变，实际 Weaver 审计通过；这是程序集字节检查，不是反射或原生游戏验证。

另用当前未改动的生产源码实际编译两个项目并执行正式 Weaver，SDK 为 10.0.301，审计仍为 9,373/9,375 方法体不变、两处指定钩子剥离后语义相同、MaxStack 头变化为 0。新编译的 DLL 仅作离线构建验证，未替换已验收的 a2 运行组件。此次验证在复制游戏内容之前停止，没有重新打包、签名、启动游戏或读取存档；不把它计为新的实机验收。

最终回归 [7/7 通过](evidence/build-freshness-2026-10-17/tests-final/results.json)，独立复核者在另一个临时工作区 [重新执行 7/7 通过](evidence/build-freshness-2026-10-17/independent-review/results.json)。主 agent 又核对了两轮的原始命令/退出结果、失败前后的复制边界、新 DLL 常量以及实际审计文件；构建器哈希均为 `131bbb2415c965f5a8e9b54c4a9ddd1159ad1f6998af9c44431453178d0bf037`。这里的 7 项是针对本次构建缺陷的回归用例数量，不代表游戏流程覆盖率。

[生产构建记录](evidence/build-freshness-2026-10-17/production-verification.json)确认既有 bin/obj、锁文件、原 Assembly 及当前游戏两 DLL 共 50 个文件的哈希、大小、修改时间和文件集合未变；[本轮保护记录](evidence/build-freshness-2026-10-17/review-preservation.json)另从开工基线核对运行源码、原启动器、README 及 Kimi 原报告等 20 项全部未变，两组有重叠，不相加计数。代码和记录完成独立复核；当前可玩版本继续使用已验收的 a2 组件，无需替换。
