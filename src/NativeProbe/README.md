# NativeProbe：只用于隔离 QA，绝不能进入发行包

此探针补充 Mac 原生 Unity 验证，源码和产物独立于正式 Weaver / CosmeticPack。发行运行组件保持原文件哈希。没有 `JINKE_NATIVE_PROBE_OUTPUT` 环境变量时不会运行。

## 执行边界

在 `PlayerColorCustomizer.RefreshBoatColors()` 的正式视觉钩子之后增加一个独立 QA 调用。探针只调度一次，在原版 `OnEnable` 返回后最多等待 60 秒，确认 `IsPlaying` 为真、正式组件已注册 20 个船 renderer 和 5 个旗帜 renderer，再执行：

1. 只读采集 20 个船/彩旗 renderer、5 个旗帜 renderer 的 activeSelf、activeInHierarchy、enabled、材质槽位、全部已声明材质属性、`_LightStrength` 全局/槽位覆盖；记录原有船漆和旗帜 ID、船级、彩旗、暂停状态、timeScale。
2. 依次调用正式 `Bootstrap.ConfigurePreview`，测试 original / ramen / octopus / moon，不持久化选择。每套检查所有船 renderer 的实际 MaterialPropertyBlock 颜色，以及五面旗实际 GPU 贴图。
3. 对原始和合成旗帜 atlas 都执行 GPU readback，逐通道比较底部 76 行，记录不同字节数；original 额外要求整个旗面变更像素数为 0，自定义旗面要求大于 0，并导出 PNG。
4. 用原生 `SkinnedMeshRenderer.BakeMesh` 生成纯视觉 mesh 副本。仅新建 MeshFilter、MeshRenderer 和离屏 Camera，以原材质/实际 MPB 渲染 PNG；相机与副本在原点以 layer 31 隔离，同步绘制后立即删除，不复制游戏脚本、骨骼层级或原生 GameObject，不激活隐藏的原始旗帜。QA05 的远点相机产生 frustum 警告及全背景图，因此近裁剪面改为 0.1、关闭此预览相机的遮挡剔除；仍须以下像素检测及人工观察确认。
5. `finally` 恢复原始外观选择/窗口状态，再核验读取的原生状态、实际有效船色、每面旗完整 GPU RGBA 的 SHA-256。只有有效颜色的 RGBA 使用固定 `1e-6` 绝对容差，以容纳原生 MPB 色彩空间往返造成的 1–2 ULP 单精度差；报告记录 `effectiveColorTolerance` 与 `effectiveMaxAbsChannelDelta`。renderer ID、分类与旗帜哈希保持严格相等，其他状态比较仍严格。允许原版空 MPB 恢复成有效值等价的覆盖。删除探针拥有的相机和 mesh 对象。

渲染测试同步执行，不主动暂停、恢复或写 timeScale，不写原生存档或 native ID。它只报告列出的字段，没有声称对整个游戏内存/存档进行全量比较。

另一个 QA 钩子在 `ContinueOrNewButton.Start()` 返回时通知可选的自动加载 fixture。只有显式设置 `JINKE_NATIVE_QA_AUTOSTART=1`，并确认 QA bundle/company/product、当前 app、明确指定的隔离存档绝对路径和原生 save strategy 路径后才继续。它最多等待 30 秒标题初始化，要求 `CanLoadLast`、现有合法 slot、非空 SaveData，以及与 TitleController 相同的 DLC1/DLC2 资格检查；全部满足后只调用一次 `SceneLoader.LoadGameFromTitle(false)`，最多等待 120 秒世界就绪。任一条件失败就拒绝，不新建存档、不调用 UI 的 `OnClick` / `OnSubmit`，也不弹框。证据写在 `autostart/fixture.json`。

这是场景加载集成 fixture，不是 UI 点击验证。原生加载和之后的游戏流程可能正常写入隔离 QA 存档/设置；不能将渲染探针的只读边界扩展为整个游戏进程不写文件。

可选的 `JINKE_NATIVE_QA_PREFS_CHECK=1` 仅在主渲染报告通过并完成磁盘读回后运行设置往返检查。它再次核对精确 QA identity，要求 `JINKE_DREDGE_SKINS_SETTINGS_DIR` 为绝对路径且目录名为 `qa-settings`，生产 `SettingsStore.FilePath` 必须恰为该目录的 `settings.json`，路径不得含软链接。已有文件若已是目标 ramen/moon 就拒绝本轮写入检查，避免把旧值读回误认为新写入成功。随后通过生产 `SkinController.Select("ramen", "moon", true)` 写入，用生产 `SettingsStore.Read` 读回，核验文件哈希实际改变或新文件创建、只有 `schemaVersion` / `boat` / `flag` 三键、原生字段/共享材质/可见性/窗口状态不变。单独输出 `native-settings.json` 及读回哈希证明；这是程序组件检查，不能记作鼠标选择通过。它刻意保留 QA 的 ramen 船 / moon 旗供下一次进程启动验证；下一次启动不要设置 PREFS_CHECK，主报告的初始选择将记录实际恢复结果。

## 构建和 QA 补丁

在项目根目录：

```sh
dotnet build src/NativeProbe/NativeProbe.csproj -c Release
dotnet run --project src/NativeProbe/PatchHelper/PatchHelper.csproj -c Release -- \
  'Contents/Resources/Data/Managed/Assembly-CSharp.dll' \
  'output/builds/DREDGE-Skins-QA-04/Assembly-CSharp.dll' \
  'output/builds/DREDGE-Skins-QA-04/Jinke.DredgeSkins.dll' \
  'src/NativeProbe/bin/Release/net472/Jinke.DredgeSkins.NativeProbe.dll' \
  'src/NativeProbe/qa-output-new/Assembly-CSharp.dll'
```

助手先执行正式全程序集审计验证发行补丁，然后加入上述两处 QA 调用。重新读取文件后，除两个方法以外的 9,373 个方法体必须一致；剥离 QA 调用后所有原定义、方法体和 AssemblyRef 必须与发行补丁相同。输出审计有 `QA_ONLY_DO_NOT_SHIP: true` 和所有输入/输出哈希。它拒绝直接写 `.app/Contents`、覆盖文件、原文件同路径、路径软链接和非 QA 命名目录。

仅将该 QA 专用 `Assembly-CSharp.dll` 及 `Jinke.DredgeSkins.NativeProbe.dll` 放入独立 QA app 的 Managed；正式 `Jinke.DredgeSkins.dll` 不变。签名/运行仍由上层 QA 流程负责。直接启动隔离 QA app 的可执行文件时指定一个新建的空目录，例如：

```sh
JINKE_NATIVE_PROBE_OUTPUT='/absolute/path/to/new-qa-evidence' \
JINKE_NATIVE_QA_AUTOSTART=1 \
JINKE_NATIVE_QA_SAVE_ROOT='~/Library/Application Support/local.jinke.dredge.skins.qa.20261016' \
  '/absolute/path/to/QA.app/Contents/MacOS/DREDGE'
```

上述路径必须与真实 QA app 和新证据目录匹配；省略 AUTOSTART 时可按正常 UI 加载。空目录检查仅忽略 macOS 生成的 `._*` 与 `.DS_Store` 元数据文件，真实结果文件仍会拒绝。原游戏关闭了 Unity 全局日志，不能依赖 Debug 的完成消息。产物包括 `native-probe.json`、实际 GPU atlas 和四张 `*-native-flag-mesh.png`。

## 如何解释结果

报告使用原包 Newtonsoft.Json，通过独立 serializer 严格序列化公共字段，避免 Unity Color/Vector3 属性递归及外部全局 JSON 设置影响。运行前会验证修改嵌套材质、灯光、有效颜色、旗帜哈希均能使比较结果变为不等。清单复用正式组件的纯解析入口。

有效通过证据必须同时满足 `native-probe.json` 中 `serializationVersion: 2`、`passed: true`、完整的 25 个 renderer / 4 套皮肤 / 每套 5 旗数据，以及 `native-probe.json.readback.json` 中 `passed: true` 且 SHA-256 与报告匹配。读回会再检查所有关键对象/数组及整份 DTO 等价；出现 `verification-error.txt` 就不能验收。旧 QA03 使用 JsonUtility，存在字段遗漏，旧 `Same=true` 结果已失效，不作为原生通过证据。离线序列化测试只证明报告机制，不能替代原生验证。

有效报告的 `passed` 覆盖数据、材质、颜色、原旗可见性、底部 76 行图集、恢复状态与预期 renderer 数量；不把预览图自动视为视觉通过。每张图以实际角落像素为背景，统计 RGB 任一通道差超过 2 的像素，至少需要画面 1% 的非背景像素；不足时明确标记 `previewUnavailable: true` / `previewError`，并保留 PNG 作为失败证据。相机/渲染管线不能离屏渲染时也记录不可用，atlas 和运行时断言仍独立有效。预览使用原网格/材质/贴图，但独立镜头与背景，不等于完整游戏场景截图。必须人工查看 PNG，原游戏操作与存档往返由主 QA 流程分别验证。
