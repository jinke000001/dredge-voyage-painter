# DREDGE 船与旗帜皮肤：公开源码核查

核查日期：2026-10-16（Asia/Shanghai）。范围：公开的一手发布说明、Winch 源码及发布包清单；没有安装模组、启动游戏、修改原包或读取用户存档。下文的 Winch 结论针对固定提交 `eab943ff5c28ab150581ff42278946c22d1f1b7f`，仍须与本地游戏版本对照。

## 结论

1. 船身/船顶原生涂装是两组颜色参数；旗帜是独立贴图材质。做“船身花纹”不能直接把一张 PNG 填进 Winch 的涂装 JSON。它需要确认原船材质的 UV、颜色遮罩及贴图通道，并在渲染资源层实施。
2. Winch 能在油漆匠菜单中新增颜色和旗帜，但会修改对话、解锁数据和存档，因此不等于完全无侵入的贴图包。最新已发布 Winch 0.6.2 没有经本次确认可用的原生 macOS 加载路径。
3. 对当前 Mac 包，最低风险的可验证路径是先制作独立原创素材和可回滚的资源补丁，只替换已确认的视觉资源，保留模型、碰撞、升级数据、物品/旗帜 ID 与解锁条件。**此路线替换已有槽位，不能表述为在游戏菜单里增加新槽位。** 若要求原皮肤与新皮肤同时出现在菜单，仍需要额外解决 Mac 模组加载与存档兼容。

## 原作体验与边界

官方 Update 1.3 将定制放在油漆匠处：选择旗帜、船漆，通过探索收集与螃蟹材料解锁更多外观。原有的寻找旗帜、交付旗帜、交付颜料材料，属于应保留的体验。来源：[Team17：DREDGE Update 1.3](https://www.team17.com/news/dredge-update-1-3-out-now)。

船的外观也不能与船体升级混为一谈。官方 The Iron Rig 含更高船体等级；The Pale Reach 有破冰装置。外观验证应覆盖已有船体等级和可见装置，不能用改变升级等级或替换整个船 prefab 来实现“皮肤”。来源：[The Iron Rig 发布说明](https://www.team17.com/news/dredge-the-iron-rig-dlc-is-out-now)、[The Pale Reach 发布说明](https://www.team17.com/news/dredge-reels-in-debut-expansion-the-pale-reach-today)。

## 已确认的数据接口

| 视觉对象 | Winch 数据/渲染接口 | 实施含义 |
|---|---|---|
| 船顶 | `BoatPaintData.color` → 材质 `_Roof_Color` | 单色参数；同色也应用到 bunting 渲染器。|
| 船身 | `BoatPaintData.color` → 材质 `_Hull_Color` | 单色参数；没有内建的 `hullTexture` 字段。|
| 旗帜 | `BoatFlagData.flagTexture` → `FlagMaterial` | `Texture2D` 生成 LitCutout 材质，赋给原旗帜 `SkinnedMeshRenderer`，保留原骨骼/动画。|
| 油漆解锁 | `questGridConfig` 与 `has-paint-{id}` | 颜料任务格完成后解锁。|
| 旗帜解锁 | `flagItem` 与 `has-flag-{id}` | 交付会移除背包/仓库中的对应物品，随后解锁。|

来源定位：

- [BoatPaintData.cs L7–20](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Data/Boat/BoatPaintData.cs#L7-L20)
- [BoatFlagData.cs L8–22](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Data/Boat/BoatFlagData.cs#L8-L22)
- [PlayerColorCustomizerPatcher.cs L14–55](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Patches/API/PlayerColorCustomizerPatcher.cs#L14-L55)
- [BoatUtil.cs L277–328、367–437](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Util/BoatUtil.cs#L277-L328)
- [PainterOverride.yarn L35–64](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Assets/Dialogues/PainterOverride.yarn#L35-L64)

`VanillaBoatFlagData` 将原索引映射到 `flag-{index}` 和 `Flag{index}_Mat`，并从材质的 `Albedo` 描述取得贴图。这为本地资产搜索提供名称线索；并不保证 Mac 每个版本的名称完全一致。[VanillaBoatFlagData.cs L11–35](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Data/Boat/VanillaBoatFlagData.cs#L11-L35)

## 加载方式与素材清单

Winch 从已启用模组的 `Assets` 目录加载：先 `Textures`，再 `Boat/Paints` 和 `Boat/Flags`；后两者为 JSON 数据。纹理以文件名（不含扩展名）作为键，加载成 RGBA32、不开 mipmap，U/V 使用 Clamp。对应来源：[AssetLoader.cs L43–120](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Core/AssetLoader.cs#L43-L120)、[TextureUtil.cs L71–96](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Util/TextureUtil.cs#L71-L96)。

若将来使用 Winch 新增内容，需要原创旗帜渲染贴图、物品图标、旗帜数据/物品数据、显示名本地化，以及单独确认的获取方式；仅给出 `flagTexture` 不会自动获得已解锁的新旗帜。新增油漆需要颜色值、本地化与颜料任务格，或另行定义的解锁方式。任务/世界 POI 会触及机制，不能无声加入。

公开示例的旗帜材质图和模板均为 **1416 × 1416 RGBA PNG**，物品图标为 **128 × 128 RGBA PNG**（本次读取 PNG IHDR 验证）。这只是该示例的尺寸，不能取代本地游戏贴图的尺寸、alpha 和 UV 核查。[旗帜示例数据](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch.Examples/ExampleItems/Assets/Boat/Flags/exampleitems.flag.json)、[示例材质 PNG](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch.Examples/ExampleItems/Assets/Textures/exampleitems.flagmaterial.png)、[示例物品图标 PNG](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch.Examples/ExampleItems/Assets/Textures/exampleitems.flag.png)。

## 存档与卸载风险

Winch 的 `ChangeBoatColor` / `ChangeBoatFlag` 对自定义外观把原生数值索引设为 `-1`，把字符串 ID 存入扩展数据。扩展数据另写 JSON，但解锁布尔值仍写游戏 `SaveData`，颜色切换也设置 `HasChangedBoatColors`。因此“另存 JSON”不代表原存档未受影响。[BoatUtil.cs L391–438](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Util/BoatUtil.cs#L391-L438)、[ExtendedSaveData.cs L30–64](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Data/ExtendedSaveData.cs#L30-L64)。

缺失模组时能否正确回退需要本地运行与存档往返验证，不能仅从这套接口保证。`PlayerColorCustomizerPatcher` 在找不到自定义颜色时会按原生索引访问数组；若该索引仍为 `-1`，存在越界风险（此处是源码推导，尚未在本地复现）。

此外，Winch 会替换油漆匠的多个命令/函数，并补丁化保存过程，不只是挂载贴图。Winch 历史发行说明还修复过原旗帜交付和选择问题，故测试必须包含原旗帜与原颜料流程。[BoatUtil.cs L256–274](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Util/BoatUtil.cs#L256-L274)、[SaveManagerPatcher.cs](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/Winch/Patches/SaveManagerPatcher.cs)、[0.6.2 发布说明](https://github.com/DREDGE-Mods/Winch/releases/tag/v0.6.2)。

## macOS 支持核查

| 证据 | 本次观察 | 可以得出的结论 |
|---|---|---|
| Winch 最新发布 | 0.6.2，2026-05-02 | 已发布版本号；不是 Mac 兼容承诺。|
| `Winch.zip` 清单 | 6,004,914 bytes，109 entries；包含 `Release/WinchLauncher.exe`、`Release/WinchConsole.exe`；未见 `.dylib`、`.so`、`.sh` | 该发布包没有 Unix/Mac 加载二进制与脚本。|
| Launcher 源码 | 写死 `DREDGE_Data/Managed/Assembly-CSharp.dll` 和 `DREDGE.exe` | 现成 launcher 不直接适配 `.app/Contents`。|
| 管理器最新 0.1.8 发布 | 2026-05-01；Windows 与 Linux 资产，没有 macOS 资产 | 不能让用户安装现成 Mac 管理器。|
| Winch issue #51 | Apple Silicon 支持请求仍 open，0 评论，2026-06-11 创建 | 有失败报告，尚无该 issue 中的修复确认。|

来源：[Winch 0.6.2](https://github.com/DREDGE-Mods/Winch/releases/tag/v0.6.2)、[Launcher.cs L26–33](https://github.com/DREDGE-Mods/Winch/blob/eab943ff5c28ab150581ff42278946c22d1f1b7f/WinchLauncher/Launcher.cs#L26-L33)、[Mod Manager 0.1.8](https://github.com/DREDGE-Mods/DredgeModManager/releases/tag/v0.1.8)、[macOS 支持 issue #51](https://github.com/DREDGE-Mods/Winch/issues/51)。

Issue 作者将失败归因于 SIP / hardened runtime，并建议替代注入方案；这些是作者的报告，**本次未验证根因，不据此建议关闭 SIP 或其他系统保护**。DREDGE 本身支持 Mac 与 Winch 支持 Mac 是两件事。[官方 Mac 发布说明](https://www.team17.com/news/dredge-out-now-on-mac-for-steam-and-gog)

## 最小实施路径及验收

建议先以本地资产证据确定所有候选纹理、材质和引用范围，再生成独立原创素材；纹理不要从公开游戏完整包获取。船身若共享大图集，不能直接全图换色；需保留与船无关的图集区域。旗帜需保持 alpha 形状和原动画所需边界。尺寸、纹理压缩、mipmap、alpha、UV 安全区由本地分析决定，公开 Winch 示例只做交叉参照。

可回滚的资源补丁应限定资源路径与原 SHA-256，输出到副本，给出每个修改对象的白名单；复读输出资源，确认仅预期视觉字段改变。保留原资源/压缩格式或经过验证的兼容格式，不改船体等级、网格容量、速度、捕获、价格、时间、碰撞、剧情或解锁字段。

运行验收尚未完成：应在隔离的测试存档环境检查昼夜、靠岸/出航、原皮肤切换、旗帜交付、船体各等级、破冰/强化装置、读档重进及还原。静态资源差异检查不能代替这些 Mac 原生运行结果。

如果最终交付采用替换槽位方案，清楚列出原槽位与新主题映射、原解锁要求、还原方式，以及“新主题通过资源包选择，游戏内槽位数量未增加”。若用户所指“增加”要求同时保留原主题的新菜单选项，则本次公开研究不足以完成该部分，须继续解决 Mac 加载器和菜单/存档适配。
