# LyumaShader 扩展版

基于 [lyuma/LyumaShader](https://github.com/lyuma/LyumaShader) 的 Waifu2d 扩展版本，为 lilToon、lilToon Custom Shader 和 Poiyomi 增加兼容支持，并提供批量处理工具。

本项目不会修改 lilToon 或 Poiyomi 的源代码。转换时使用 LyumaShader 自己的 Shader，或在 `Assets/LyumaShader/Waifu2d/Generated` 中创建生成文件。

## 主要功能

- 保留原版 LyumaShader 和 Waifu2d 功能。
- 支持 lilToon 的普通、Lite、描边、折射、毛发、宝石、覆盖、假阴影和 Multi 等变体。
- 自动识别使用 `.lilcontainer` 的 lilToon Custom Shader，例如 SuperLilToonCyber。
- 支持 Poiyomi Shader。
- 批量转换模型、Prefab、材质和当前多选对象。
- 批量修改 2D 强度、朝向、锁定轴和 Z 深度修正。
- 一键移除 Waifu2d，并恢复转换前的 Shader。
- 为已经转换的材质生成“关闭 2D”和“开启 2D”两个动画。
- 在模型 Root 上一键添加或还原 `MA Mesh Settings`，统一到 Humanoid `Hips` 并自动计算公共 Bounds。

## 1.1.0 更新

- 新增 Root Bone 修复与可逆还原，解决衣服、头发使用不同根骨骼时的二维位置问题。
- 修复旧工程中 `Waifu2d.cginc` 路径查找失败的问题。
- 改善 lilToon FakeShadow、MultiOutline 和 `_OutlineZBias` 的兼容性。
- 默认 Z 深度修正调整为 `0.8`，降低二维状态下的穿模和闪烁。
- 批量工具菜单调整为 `Tools > LyumaShader Extended > Waifu2d 批量工具`。

## 安装

### 使用 VCC 安装

将下面的仓库地址添加到 VCC：

`https://zhuozhi233.github.io/vpm-listing/index.json`

然后在项目的“管理项目”页面安装“LyumaShader 扩展版”。安装后包位于：

`Packages/com.zhuozhi.lyumashader-extended`

### 手动安装

从 [Releases](https://github.com/zhuozhi233/LyumaShader-Extended/releases) 下载 VPM ZIP，并将内容解压到项目的 `Packages/com.zhuozhi.lyumashader-extended`。推荐优先使用 VCC，依赖处理会更完整。

## 使用方法

打开 Unity 菜单：

`Tools > LyumaShader Extended > Waifu2d 批量工具`

在窗口中扫描模型材质或读取当前多选，然后执行转换、批量参数修改、移除、动画生成或 Root Bone 修复。

也可以右键材质：

- `转换为 2D（Lyuma Waifu2d）`
- `恢复为 3D（Lyuma Waifu2d）`

生成动画时，只处理已经转换的材质。未转换材质不会自动转换，也不会生成对应曲线。

## 使用说明

- 首次转换 lilToon Custom Shader 时，需要等待 Unity 生成和导入对应变体。
- 生成文件不会写入第三方插件目录，也不会修改 `Packages/jp.lilxyzw.liltoon`。
- 推荐 Z 深度修正值为 `0.8`；仍可根据模型的网格层次自行调整。
- Root Bone 修复需要 Modular Avatar，VCC 安装时会自动处理该依赖。
- 使用 lilToon 适配前请自行安装 lilToon；本包不通过 VPM 强制限定 lilToon 最低版本。Poiyomi 为可选依赖。

## 兼容环境

- Unity `2022.3`
- Modular Avatar `1.12.0` 或更高版本
- lilToon：以 `2.3.4` 测试，不强制最低版本
- Poiyomi `9.3.64`

旧版本能否正常运行取决于其接口兼容性

## 致谢与许可证

- 原项目：[lyuma/LyumaShader](https://github.com/lyuma/LyumaShader)
- lilToon：[lilxyzw/lilToon](https://github.com/lilxyzw/lilToon)
- Poiyomi：[poiyomi/PoiyomiToonShader](https://github.com/poiyomi/PoiyomiToonShader)

项目延续原版 LyumaShader 的 MIT 许可证。lilToon 相关文件的许可证说明位于 `Waifu2d/lilToon/LICENSE.lilToon.txt`。
