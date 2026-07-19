# LyumaShader Extended

基于 [lyuma/LyumaShader](https://github.com/lyuma/LyumaShader) 扩展的 Waifu2d 工具，为官方 lilToon、Poiyomi 及部分第三方 lilToon Custom Shader 提供 2D 转换支持。

2.x 使用 NDMF 在构建副本中完成材质转换、动画生成和网格修复。原材质、原动画控制器以及 lilToon、Poiyomi 的包文件不会被修改。

## 主要功能

- 保留原版 LyumaShader 与 Poiyomi 支持，并新增官方 lilToon 适配。
- 扫描模型当前材质、动画换材质和组件引用的备用材质。
- 为每个材质单独决定是否转换，并可分别设置 2D 强度、朝向、锁定轴和 Z 深度修正。
- 构建时生成 2D 开关、动画、BlendTree 与 Modular Avatar 菜单，并支持把开关安装到指定的 MA 子菜单、Menu Group 或 Menu Installer 目标。
- 构建时修复 Root Bone，并把普通 `MeshRenderer + MeshFilter` 临时转换为单骨骼 `SkinnedMeshRenderer`。
- 支持材质名称和 Shader 名称搜索；大量材质会分页显示。
- 提供一键 1.x → 2.x 迁移，保留逐材质参数、Root Bone 和普通网格修复状态。

## 安装

### VCC

在 VCC 中添加仓库：

`https://zhuozhi233.github.io/vpm-listing/index.json`

然后在项目的“管理项目”页面安装“LyumaShader Extended”。

### 手动安装

从 [Releases](https://github.com/zhuozhi233/LyumaShader-Extended/releases) 下载 VPM ZIP，解压到：

`Packages/com.zhuozhi.lyumashader-extended`

推荐使用 VCC，以便自动安装 NDMF 与 Modular Avatar 等依赖。

## 使用方法

打开：

`Tools > LyumaShader Extended > Waifu2d 配置工具`

选择模型后点击“一键配置”。工具会扫描全部关联材质，并在模型 Root 上保存 NDMF 配置。材质规则、2D 参数、开关菜单位置和构建修复均可在窗口中继续调整。菜单位置可以留空以安装到根菜单，也可以指定模型内的 MA 子菜单、Menu Group 或 Menu Installer。

模型上的 `Lyuma Waifu2d Avatar Config` 组件只保存配置；点击组件中的按钮可以重新打开窗口。

点击“移除配置”只会删除 2.x 的 NDMF 配置。由于转换发生在构建副本中，源材质和原控制器不需要还原。

## 从 1.x 升级到 2.x

2.x 可以直接安装，不要求先在 1.x 执行一键还原。通过 `Tools > LyumaShader Extended > 从 1.x 升级到 2.x` 打开独立迁移工具，选择旧模型后开始升级。工具会：

1. 读取每个已转换材质的启用状态、2D 强度、朝向、锁定轴和 Z 深度修正。
2. 将材质状态写入逐材质 NDMF 规则。
3. 迁移 Root Bone 与普通网格修复状态。
4. 把材质恢复到转换前的 Shader，并移除旧版构建组件和模型上的旧版 MA 开关对象。

迁移工具不会迁移或修改旧版生成的动画、BlendTree 和 Prefab 资源；2D 开关由 NDMF 在构建时根据 2.x 配置重新生成。确认项目中不再引用旧资源后可以自行清理。升级前仍建议备份工程和模型 Prefab。

## 第三方变体着色器

工具默认只启用官方 lilToon 和 Poiyomi。`lilToon Custom Shader`、Motchiri 以及其他第三方变体需要用户明确确认后才能启用。

第三方变体的顶点逻辑和 Shader 结构可能与官方版本不同，即使能够生成组合 Shader，也不保证原功能和 2D 效果完全兼容。出现编译错误、原效果失效或渲染异常时，请停用对应材质规则。

## 使用说明

- 推荐的 2D 强度上限为 `0.99`，Z 深度修正默认值为 `1.0`；不同模型可按网格层次调整。
- 右键材质仍可使用“转换为 2D（Lyuma Waifu2d）”和“恢复为 3D（Lyuma Waifu2d）”。这两个入口会直接修改材质，不属于 NDMF 非破坏流程。
- 1.x 升级会恢复原材质 Shader，并把后续处理切换到 NDMF 构建副本。
- 本包不强制限定 lilToon、Poiyomi 或 Modular Avatar 的具体版本。

## 测试环境

- Unity `2022.3`
- lilToon `2.3.4`
- Poiyomi `9.3.64`
- Modular Avatar `1.12.0`

以上仅为测试环境，不是强制版本要求。

## 致谢与许可证

- [lyuma/LyumaShader](https://github.com/lyuma/LyumaShader)
- [lilxyzw/lilToon](https://github.com/lilxyzw/lilToon)
- [poiyomi/PoiyomiToonShader](https://github.com/poiyomi/PoiyomiToonShader)

项目延续原版 LyumaShader 的 MIT 许可证。lilToon 相关许可说明位于 `Waifu2d/lilToon/LICENSE.lilToon.txt`。
