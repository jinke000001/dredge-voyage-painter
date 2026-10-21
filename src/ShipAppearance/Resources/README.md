# 原创 Boat5 资源

`cthulhu-v1.bin` 与 `cyberpunk-v1.bin` 是本项目原创 Blender 船体的受控导出，不包含原游戏网格、贴图、Shader、存档或挂件。来源与 SHA-256 见 `provenance.json`；运行时另用 `Boat5AuthoredAssets` 常量锁定完整内容。`.blend`、原始调查证据与渲染保留本机 `output/blender/`，不进入运行包。

二进制合同为小端：ASCII `JBSKIN01`，Int32 宽/高（固定 64×64），Albedo 与 Emission 各一份连续 RGBA32 字节（左下为起点、sRGB、无 mip、Point/Clamp），Int32 态数 4。随后按 Sturdy/Ok/Poor/Critical 各写 Int32 顶点数、每顶点八个 Single（位置 xyz、作者法线 xyz、UV xy），再写两组 Int32 索引数及对应 Int32 索引。槽 0 用原船壳/船顶遮罩 UV 保留船漆；槽 1 用配对小图集承载其余细节、暖窗和彩色发光。Emission 已按原灯强度 4 归一；运行时代码不覆盖 `_LightStrength`。

生产导出入口为 `tools/blender/boat5_runtime_export.py`，只接受其中锁定的已批准四态工程 SHA，只输出新目录。它烘焙 modifiers 与作者角法线，将 Blender `(x,y,z)` 转为 Unity `(x,z,y)` 并校正绕序。克苏鲁非平面平面着色多边形的少量反向子三角只修绕序，保留位置和角法线；开放表面补反向面与反法线，以配合原 Shader 的 Cull Back。R3 逐三角及逐对象记录在私有 `runtime-04/export-manifest.json`；此前 `runtime-03` 和已接受源工程继续保留。

`tools/blender/boat5_runtime_preview.py` 将实际二进制反导回 Blender，用 Eevee 原生背面剔除核对表面完整性。它使用近似船漆和本地预览 Shader，不能替代游戏中 Metal/原灯/雾/波浪遮罩的验证。第一次 Cycles 透明背面模拟不适用于共面正反面，错误图保留但不作为有效渲染证据。

维护时先导出至新目录，检查四态、两槽、角法线、UV、边界和单面渲染，再更新资源、来源记录及 SHA 常量。实际 app 必须使用 fresh 构建器；不能手动覆盖已有 app 内 DLL。接口依据：[Blender Mesh API](https://docs.blender.org/api/5.2/bpy.types.Mesh.html)、[Unity Mesh 索引与绕序](https://docs.unity.cn/6000.0/Documentation/Manual/mesh-index-data.html)、[按材质槽设置 PropertyBlock](https://docs.unity.cn/2021.3/Documentation/ScriptReference/Renderer.SetPropertyBlock.html)。

R3 灯座修补只增加原创支架：克苏鲁青铜/骨饰托架和赛博装甲灯台，分别承接普通前灯及额外的高级上下灯；四态使用相同支架。`tools/blender/boat5_forelamp_mount.py` 对照只读原资源的真实脚底顶点与船舱表面配准，独立验证原主体/挂件参考保持不变。私有 `.blend` 中的 `NATIVE_ForeLamp_REFERENCE_NOT_EXPORT` 含真实原灯和冰刀的本机配准参考，绝不打入资源；导出器只遍历原创主体集合，并显式拒绝参考标记和参考 mesh 数据，导出清单记录排除 5 份原网格。`provenance.json` 的原创/不含原资产声明针对最终二进制。
