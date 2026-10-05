# 许可与第三方资源边界

本仓库原创源码、脚本及文档按 [MIT License](LICENSE) 提供。该许可**不授予**以下内容的使用或分发权：

- TaleWorlds / Bannerlord 的程序、程序集、模型、贴图、物理资源及其他原版文件。
- Mojang / Microsoft 的 Minecraft 程序、资源、名称或商标。
- 未来引入的任何第三方内容，其许可仍须单独遵守。

模组仅在玩家本机引用游戏程序集，并按名称加载本机 Native 资源 `editor_cube` / `bo_editor_cube`。这些资源及游戏 DLL 不包含在本仓库或构建 staging 中，MIT 许可不覆盖它们。预览模型也是运行时复制本机资源，不是仓库分发资源。

README 中的 MIT 描述只针对本仓库内容；不代表获得原版资产再分发许可。本项目与 TaleWorlds、Mojang、Microsoft 没有官方关联或背书。使用及发布编译模组还应遵守游戏适用条款。

当前没有 vendored 第三方代码、Java 模组、Minecraft 文件或外部 NuGet 依赖。使用 .NET SDK/Framework 和游戏编译引用不改变这些组件各自的许可。
