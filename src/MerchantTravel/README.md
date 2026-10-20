# 显形目的地轮盘运行组件

`Jinke.DredgeMerchantTravel.dll` 2.1.0 用于 DREDGE v1.5.4 / build 2993 的固定隔离副本。原 `Assembly-CSharp.dll` SHA-256：`a071ffebe51514af5f661055f126a5041acda127623ee7c681c6ef2233a24729`。只能从本轮交付的独立启动脚本运行，不覆盖原包或正式存档。

2026-10-19 的航行体验版新增八级货舱扩建与畸变／笼网加强档，采用新的固定身份 `local.jinke.dredge.voyage.qa.20261019`；保留 10/18 的交付包和用户存档。显形行为不变，当前版本细节与各类证据边界见[航行体验记录](../../research/航行体验实现与验证.md)。

## 使用与规则

原剧情解锁「显形」后，先在原能力轮盘选中显形。自由航行时长按原能力按键（默认鼠标右键，尊重原版重绑），完成原版 1 秒蓄力后打开目的地轮盘。开盘后移动鼠标至少 8 个物理像素，再按方向选择；松开能力按键前往。移回中心、未移动直接松开、Esc、失焦或其他游戏流程开始均取消，取消不消耗理智或冷却。方向键和 Enter 作为辅助操作。

固定七个位置：原黑石岛、积灰／闪亮／摇晃／焦黑／荒废的浮码头，以及钢铁钻井。额外六处仅在真正停靠后开放；荒废的浮码头还需原 `DLC_1`，钢铁钻井还需原 `DLC_2`。未发现或无资格的地点显示原版问号，不泄露具体地点名称。平台目标使用原海面泊位附近的安全水面，抵达后仍需自己按原规则停靠、触发原对话。

七个目标共用**原版显形**的历史和半个游戏日冷却，沿用原 -0.4 理智变化。确认后回到原 `AbilityBarUI.OnCurrentPressComplete → TeleportAbility.Activate → PlayerTeleport.Teleport`；镜头、隐藏船体、特效、音效、震动、免疫、原距离成就判定与冷却写入全部由原方法执行。原版动画的 1 秒前段和 0.5 秒后段不缩短。显形冷却期间入口依原规则不可用。金船锚保持原逻辑。

轮盘使用原整圆 `circle-high-res`、显形图标、码头图标、钢铁钻井地图图标、`question-mark-solo`、Fonts 表字体／材质和原轮盘音效。重建的对象只有自己的 RectTransform、Image、TMP 和 Canvas；不克隆或启用原 `AbilityRadial`／`AbilityRadialWedge` 脚本，不广播原全局轮盘事件，也不改选中能力、LastSelectedAbility 或 unseen 标记。若已加载码头／平台图标不唯一或不可用，仅回退到已有的原显形图标；诊断会报告实际图标名称。

轮盘与原能力轮盘一样把时间缩放到 0.3；输入租约只暂停原输入派发和 UI 导航，并显示鼠标。不改原 action layer、原暂停状态或移动许可。恢复时间／输入后才调用原显形，避免减慢原动画。关闭时等待操作按键释放，防止输入穿透。原 F9 矩形面板和 HUD 提示已经移除。

## 提交边界

唯一新增游戏钩子是 `AbilityBarUI.OnCurrentPressComplete()` 的前置 `Bootstrap.BeforeAbilityComplete(object owner)`，返回 true 放行原方法，false 跳过。非显形能力直接放行。显形第一次完成蓄力只打开轮盘，并重置这一条确切的原 hold；其它原技能蓄力仍是阻塞条件。

确认时重查原 bar／ability／player／SaveData 身份、原 VerifyAbilityCast、自由航行门禁、目标访问资格和安全落点。单次票据只能放行同一个 bar 与 AbilityData 的一次重入；已消费或嵌套重入不会再次施法。商人／平台目标只在这次同步原调用中，把该 TeleportAbility 的私有目标引用换成一个仅含 Transform 的私有临时对象；原黑石岛对象本身不移动。`finally` 恢复并核对原引用、清票据、销毁临时对象。原 PlayerTeleport 协程保存的是 Vector3 值，不持有临时对象。

提交失败绝不回退到黑石岛，不手工扣费／退款、不补发完成事件、不重试已经有副作用的原施法。原传送开始后不能取消；异常保持阻塞直到原完成或换场景。诊断记录原 Begin 次数、成功提交数、完成数、目标引用恢复结果和实际抵达。

生产组件只读解锁、访问、DLC、任务和锚字段。原 `abilityHistory["manifest"]` 的唯一写入来自原成功施法方法。以前的独立冷却侧车代码已删除；旧磁盘侧车既不读取、修改，也不参与判断。保存、读档、切槽和回档均跟随原游戏的显形记录，没有第二套冷却状态。

## 安全落点

除原黑石岛原目标外，从对应场景的 DockPOI 与 dock slots 生成有限候选，不接受任意坐标。六个额外 id：`dock.pontoon-gc`、`dock.pontoon-sb`、`dock.pontoon-ts`、`dock.pontoon-ds`、`dock.pontoon-tpr`、`dock.the-iron-rig`。

每个候选检查船体占地中心、四角和四边中点的原水深纹理。原真实船体层碰撞矩阵过滤实体障碍；普通采集／天气探测区域不作为墙。自动对话、危险触发器、怪物探测、OozePatch／Ooze 污染区域保持禁止。原 culled inactive 对象的 enabled 几何仍检查，不激活原物体或改碰撞矩阵。

几何粗筛后，在独立持久 `LocalPhysicsMode.Physics3D` 场景中，用有效启用的私有 Box 与原几何的只读形状副本执行 ComputePenetration。只复制 Transform 与 Box/Sphere/Capsule/Mesh 参数，Mesh 只读共享；不复制原脚本、刚体。每次检查须先通过 7 类形状的相交／分离／粗筛共 21 项自检，失败全拒绝。含 inactive 原对象、凹网格及非均匀缩放球／胶囊。查询对象按轮销毁，私有场景不 unload：原 InputManager 对任何 sceneUnloaded 都会清空动作集。

## 隔离与接口

共四处窄钩子：SteamManager.Awake 与 SaveManager.Init 的最前方执行 `EnsureIsolatedProfile()`；PlayerColorCustomizer.RefreshBoatColors 每个正常返回前执行 `OnPlayer(this)`；上述 AbilityBarUI 前置 bool。构建器验证剥离四处 hook 后与原 IL 一致，不修改 TeleportAbility／PlayerTeleport 原 IL。

隔离检查仍固定 bundle/company/product、持久目录、runtime 加载位置、output 内独立 app、proof 核心哈希、launcher 环境、Steam app id 与 cwd，以及 profile 全部现存路径的软／硬链接。Unity 启动时可能把 cwd 改成 app 的父目录，因此仅允许该精确父目录或 Resources；全身份校验后切回 Resources。失败原样抛出，阻止 Steam 重定向与 SaveManager 访问正式存档。启动诊断以唯一 CreateNew 文件写入固定 `output/runtime/merchant-travel-startup/`，诊断失败不能放行校验。

正常公开接口：

```csharp
Bootstrap.BeforeAbilityComplete(owner);   // 原方法前置；true 继续，false 跳过
Bootstrap.RequestOpen();                 // 辅助预览：仍须当前显形、解锁、全门禁与原 Verify
Bootstrap.RequestTravel("dock.pontoon-gc"); // 必须已开盘，重新检查并提交原显形
Bootstrap.RequestTravel("manifest.blackstone");
Bootstrap.Cancel();
Bootstrap.GetDiagnostics();              // JSON，只读，无自动 fixture 或作弊分支
```

公开 RequestOpen 不伪造或重置原 hold；此路径 `openedByNativeHold=false`，用方向键／Enter／Esc，不把鼠标已经松开当成确认。用户主入口仍是正常原长按。真正 hold 打开的轮盘才在 DoAbility 释放时确认。两条路径提交相同门禁与原成功调用。

诊断包括所有七个目标、几何候选、拒绝原因、原状态、当前选择／中心死区、鼠标激活状态、使用的原资源、原冷却、原目标引用恢复、实际抵达与提交计数。profile 下 `MerchantTravel/runtime.log` 记录重要拒绝、选点与抵达，256 KiB 轮换一份 previous；不记录存档内容。

## 构建与验证状态

```sh
dotnet build src/MerchantTravel/MerchantTravel.csproj -c Release
dotnet run --project src/MerchantTravel/Tests/MerchantTravel.Tests.csproj -c Release
```

必须使用 fresh staging 构建并封装独立 app，不能拿旧 bin 产物覆盖原包。2026-10-18 新版已 fresh 构建为 `output/DREDGE 显形轮盘体验版.app`。54 项运行规则检查覆盖七扇区／中心死区、单次提交票据、首次无 manifest history 的正无穷边界、原冷却计算、全部状态门禁、六个额外固定目标与 DLC 条件、落点规则及隔离路径检查。

修复版 `session-radial02` 已完成七个目的地、原金锚往返取回、原冷却重载、问号锁定、Esc／失焦取消及暂停状态拒绝；每处商人和平台实际航行靠泊无损伤。轮盘七地选择通过 QA 预览入口与真实方向键／Enter。最终包无 Probe，290 个方法语义与受测 runtime 一致；用户已在最终包通过真实“长按—滑动—松开”抵达钢铁钻井，生产日志和画面均确认，随后正常靠泊与原对话完成。退出后原包 355 文件及正式目录 56 文件无变化。手柄与完整剧情未重玩；各类证据边界统一见下方报告。

旧 F9 版本的 QA07 五处真实到达／航行／停靠与 21 项形状证据见 [实现与验证报告](../../research/商人传送实现与验证.md) 和 [session-07](../../output/runtime/merchanttravel-testing/session-07/)。它们属于先前版本，不能算作本轮原生轮盘、钻井平台或共用显形冷却的通过证据。旧 QA 的解锁、起点、时间／理智与放锚含显式合成准备，不代表完整剧情通关。
