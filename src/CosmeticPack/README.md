# DREDGE 纯外观组件

该组件针对本项目固定指纹的 DREDGE Mac / Unity 2021.3.5f1 构建。原生标题为 v1.5.4 prod / build 2993；plist 仍声明 1.5.3，不用版本字符串判定兼容。它不引用 `Assembly-CSharp.dll`，只通过两个尾部钩子读取 `PlayerColorCustomizer` 已有渲染器列表：

- `Jinke.DredgeSkins.Bootstrap.OnColors(object)`
- `Jinke.DredgeSkins.Bootstrap.OnFlag(object)`

程序集名称为 `Jinke.DredgeSkins.dll`。钩子不调用保存、暂停或动作层切换等游戏命令，不读写游戏存档，不修改旗帜可见性、解锁、物理、模型、船速或时间。只在玩家已打开原游戏暂停菜单时接受外观面板输入：先按 Esc 暂停，再按 F8 或右上角按钮；Esc 关闭外观舱，F8 切换。入口在外观舱打开时隐藏，入口及其透明命中区均避开原版右下角“继续游戏”。关闭外观舱后仍保持原游戏暂停，玩家自行继续。

## 资源协议

在 `Application.streamingAssetsPath/JinkeSkins/manifest.json` 放入 UTF-8 JSON。该 macOS 包实际位置为 `.app/Contents/Resources/Data/StreamingAssets/JinkeSkins`；不能用 `Application.dataPath` 拼接，因为本包的 dataPath 为 `.app/Contents`：

```json
{
  "schemaVersion": 1,
  "skins": [
    {"id":"ramen","name":"深海拉面","hullColor":"#D8BF94","roofColor":"#8C4436","flagFile":"ramen/flag.png"},
    {"id":"octopus","name":"罐头章鱼","hullColor":"#76A49A","roofColor":"#C49B55","flagFile":"octopus/flag.png"},
    {"id":"moon","name":"月航信使","hullColor":"#A398AD","roofColor":"#4D3859","flagFile":"moon/flag.png"}
  ]
}
```

ID 必须恰好为这三项；名称与配色由最终素材清单决定。旗面必须为 3:2 PNG，单边不超过 2048 像素、文件不超过 8 MiB。`flagFile` 是此资源目录内的相对路径。

清单和独立设置均由 `SkinJson` 使用原包 `Newtonsoft.Json.dll` 解析，不依赖 Unity `JsonUtility`。已核实程序集版本为 13.0.0.0、文件版本 13.0.2，原包 SHA-256 为 `a56146202232958f46bd6a28b5a7da166aea123ee0d646735a46e5c341dfbf1f`。构建只引用此现有 DLL，不下载或复制替换游戏依赖。解析器显式创建设置，不继承全局 `JsonConvert.DefaultSettings`；`TypeNameHandling.None`，拒绝 `$type` 等额外字段，最多 8 层。清单最多 64 KiB，独立设置最多 4 KiB，按实际 UTF-8 字节限制读取；拒绝重复属性、额外文档、无效 UTF-8、错误字段类型或未显式声明的整数 `schemaVersion`。

真实 `Flag.obj` 的 UV 范围为 u=0.00798034668..0.991210938、v=0.305419922..0.993652344。运行时从当前原版旗帜材质读取 256×256 atlas，以 GPU 拷贝兼容不可读原贴图，只覆盖上部 256×180 区域（Unity bottom-up y=76..255）。下部 76 行为旗杆颜色区域，由 compositor 逐字节保留。独立旗面会缩放贴合原 UV，几何形状和飘动仍完全使用原模型及材质。

## 覆盖与恢复

船体仅设置材质槽 0 的 `_Hull_Color` / `_Roof_Color`。旗帜仅设置材质槽 0 的 `Texture2D_9aa7ba2263944b48bbf43c218dc48459`。使用 MaterialPropertyBlock，不创建材质副本；不改 shader、其他贴图、遮罩、灯光或损坏参数。

每次应用先读取当前 MPB，再仅修改自己的属性。Unity 2021 的 MPB 没有单属性删除 API，因此恢复原版会将这几个属性恢复为原有 MPB 值，或当前游戏材质原值，并在之后每次游戏颜色/旗帜刷新时继续同步。其他 MPB 字段保持不变。首次默认 original 时不会写渲染属性。已分析的游戏 C# 未发现其他 MPB 写者；同时安装其他修改相同属性或 renderer-wide / per-material MPB 的模组未验证兼容性。

不调用 `SetActive` 或变更渲染器 `enabled`。原游戏 flag style=0 时旗帜继续隐藏；装备资格和隐藏逻辑均由原游戏决定。

## 防止操作穿透

面板只读 `GameManager.IsPlaying && IsPaused && CanUnpause`，并要求未在 `Controls.IsListeningForBinding` 状态。鼠标任一按钮或触摸仍按住时拒绝打开，避免接管正在拖动的原生滑条。Unity 透明 Image 与最高顺序 Canvas 只挡住自有按钮，面板打开后扩展为整屏射线挡层。

窗口期间缓存同一个 `DredgeInputManager` 的真实 `Behaviour.enabled`，暂时禁用其 Update/LateUpdate 派发；同时缓存并关闭同一个 `EventSystem.sendNavigationEvents`，暂时清空原选择，阻止底层方向、提交、快捷键及 UpdateSelectedObject。DREDGE 本包的 `DredgeInputManager` 无 OnDisable/OnEnable 副作用；它的动作层和游戏暂停状态不变。此行为仅是暂停面板期间的临时输入隔离，不修改移动绑定或游戏运动参数。

正常关闭会等 Esc/F8/鼠标/触摸全部释放后至少下一帧，再恢复原输入启用值和导航值。原选中对象仍活动、当前选择为空且 EventSystem 未更换时才恢复选中。原生暂停失效、出现禁止恢复的原生弹窗、组件异常、OnDisable 或 OnDestroy 都立即恢复输入并销毁挡层；不会等待按键释放而遗留禁用状态。输入隔离路径仍须原生验证，纯托管测试不覆盖它。

原游戏会在取消滑条选中时退出滑条编辑模式；关闭外观舱会恢复可用的选中对象，但不会擅自重建该编辑模式。需在原生验收中确认菜单导航、重新编辑滑条和 Tab 切换正常。

## 独立偏好与测试入口

首次船色、旗帜均为 `original`。面板选择只保存到 macOS `~/Library/Application Support/JinkeDredgeSkins/settings.json`，不使用游戏的 `Application.persistentDataPath`：

```json
{"schemaVersion":1,"boat":"original","flag":"original"}
```

开发 / QA 可设置绝对路径环境变量 `JINKE_DREDGE_SKINS_SETTINGS_DIR`，文件直接保存在该目录的 `settings.json`。未知 ID、错误版本和资源异常回退原版。

组件日志写入同一独立设置目录的 `cosmetics.log`，记录初始化资源路径、加载错误、选择和输入隔离/恢复，统一前缀 `[JinkeSkins]`。当前文件最多 128 KiB，轮换后仅保留一个最多 128 KiB 的 `cosmetics.log.previous`，单条消息限制 4096 字符。原游戏关闭了全局 Unity logger，因此不再依赖 Player.log，也不修改全局 logger 设置。日志目录不可写时吞掉日志 I/O 错误，不向游戏回调抛出；游戏存档目录不作日志后备位置。

原生 Unity 测试可调用 `Bootstrap.GetDiagnostics()` 读取组件自己的状态，或在至少一次游戏钩子之后调用 `Bootstrap.ConfigurePreview(boatId, flagId, showWindow)` 临时选择外观；后者不写设置文件，也不调用游戏代码。调用必须在 Unity 主线程。不会通过反射寻找或触发游戏保存接口。

## 内存与生命周期

3 张 384×256 预览保留 GPU 纹理和 compositor 所需的 RGBA 字节；解码源 PNG 立即销毁。256×256 组合 atlas 按原图引用 / Instance ID 与皮肤 ID 复用，最多 48 张；每次 GPU 临时 RenderTexture 均在 finally 中释放，之前的 active RenderTexture 与 sRGBWrite 状态均恢复。所有组件自建纹理、动态字体在控制器销毁时释放；不会销毁原游戏纹理或材质。被销毁的渲染器引用和源图已销毁的组合 atlas 每 10 秒清理。

## 验证

```sh
dotnet build src/CosmeticPack/CosmeticPack.csproj -c Release
dotnet run --project src/CosmeticPack/Tests/CosmeticPack.Tests.csproj -c Release -- assets/JinkeSkins
```

验证记录（2026-10-16）：编译 0 警告 / 0 错误；67 项纯托管断言通过。测试直接编译并调用生产 `SkinJson`，使用同一原包 Newtonsoft DLL，读取真实三套素材清单并检查中文名称、嵌套字段和 PNG 头；16 组船色 / 旗帜独立选择经过实际设置文件写入、替换和读取。异常用例覆盖缺失 schema、错误字段类型、重复属性、元数据、全局 JSON 设置干扰、尾部内容、深度 / UTF-8 容量边界与无效编码。原有默认原版、资源路径、PNG 尺寸、旗杆字节、旗面方向和独立日志轮换 / I/O 隔离测试继续通过。测试只使用临时自有文件，不启动游戏、不访问游戏存档。

QA03 原生曾观察到：旧 `JsonUtility.FromJson<PackManifest>` 返回后 `skins` 为空，导致清单验证失败；其底层原因尚未证实。旧纯托管测试使用另一套 JSON 实现，未覆盖此生产路径，已由上述回归测试替换。新的解析实现及 macOS `streamingAssetsPath` 修复已在 QA06 / QA07 原生加载成功，三套主题可见，非默认设置重启读回通过。

原生证据（2026-10-16）：Apple M2 / Unity 2021.3.5f1，生产 DLL 冻结 SHA-256 为 `a2cba00650e7e6bf6044b76120c7ed57f5d4e4d11ee0245d00f6cfa5072d40e2`。

- [QA06 渲染属性报告](../../research/evidence/native-probe-06/native-probe.json)及其[读回校验](../../research/evidence/native-probe-06/native-probe.json.readback.json)通过：原版与三主题分别覆盖场景已加载的 20 个船色渲染器、5 个旗帜渲染器；船色匹配、旗杆区域字节不变，检查到的原生字段和渲染器状态保持不变，结束后有效外观恢复通过。
- [QA06 设置报告](../../research/evidence/native-probe-06/native-settings.json)及其[读回校验](../../research/evidence/native-probe-06/native-settings.json.readback.json)通过：通过生产组件 `Select` 从不存在的独立设置文件写入 `boat=ramen` / `flag=moon`，再由生产读取路径读回，文件仅包含三个规定字段。这是组件设置集成测试，不是鼠标点击测试。
- QA07 在不执行设置写入测试的新进程中，初始值即为 `ramen` / `moon`；[QA07 报告](../../research/evidence/native-probe-07/native-probe.json)及其[读回校验](../../research/evidence/native-probe-07/native-probe.json.readback.json)通过。[冷启动截图](../../research/evidence/native-ui/12-cold-start-mixed-selection.png)显示三套主题、中文名称及“深海拉面船色 + 月航信使旗帜”的独立选中状态。

验证边界：测试档未装备旗帜，原生旗帜始终保持隐藏。旗帜材质属性及图集字节检查通过，但离屏 mesh 预览为空白，不能作为已装备旗帜的可见外观或飘动验收。上述渲染器数量不代表完成自然升级或损坏流程；真实鼠标点击、手柄、自然升级与损坏流程仍未验收，未访问或验收用户真实存档。
