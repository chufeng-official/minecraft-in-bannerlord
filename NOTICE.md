# 许可与第三方资源边界

本仓库原创源码、脚本及文档按 [MIT License](LICENSE) 提供。该许可**不授予**以下内容的使用或分发权：

- TaleWorlds / Bannerlord 的程序、程序集、模型、贴图、物理资源及其他原版文件。
- Mojang / Microsoft 的 Minecraft 程序、资源、名称或商标。
- 未来引入的任何第三方内容，其许可仍须单独遵守。

Bannerlord 模组在玩家本机引用游戏程序集，并按名称加载本机 Native 资源 `editor_cube` / `bo_editor_cube`；Java Mod 在玩家合法安装的 Minecraft/Fabric 运行时访问方块状态。这些游戏资源及游戏 DLL/JAR 不包含在源码仓库或本项目模组分发包中，MIT 许可不覆盖它们。预览模型也是运行时复制本机资源，不是仓库分发资源。

README 中的 MIT 描述只针对本仓库内容；不代表获得原版资产再分发许可。本项目与 TaleWorlds、Mojang、Microsoft 没有官方关联或背书。使用及发布编译模组还应遵守游戏适用条款。

当前包含本项目原创 Java Mod 源码，没有 vendored 第三方代码、原版 Minecraft 文件或外部 NuGet 依赖。开发脚本经确认会下载 Temurin、Gradle、Fabric 及游戏构建依赖，保存在忽略提交的本地目录；测试实例中的 Fabric API 也不进入本项目分发包。这些组件各自的许可须单独遵守，MIT 不覆盖它们。使用 .NET SDK/Framework 和本机游戏编译引用同样不改变其许可。
