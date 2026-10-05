# 贡献指南

欢迎小范围、可验证的修复与改进。当前涵盖 Bannerlord 单人动态方块、模拟桥接及真实 Minecraft 石头权威；下一步建议为最小背包。扩展 MC 机制、战役存档、Harmony 或新增运行时依赖前请先讨论，避免将原型写成完整玩法适配。

## 开发

1. 按 README 安装你自行选择的合法开发工具并配置本机游戏目录；不要提交该路径。
2. 修改前了解周边代码，保持已有碰撞激活流程和主线程引擎调用。
3. 运行纯逻辑测试和发布审查；涉及宿主 API 时使用自己的游戏 DLL 编译。
4. Pull request 说明复现、变更原因、编译/实机结果及未验证限制。

```powershell
dotnet run --project tests/Diagnostics/Diagnostics.csproj -c Release
dotnet run --project tests/Bridge/Bridge.csproj -c Release
pwsh -File tools/AuditSource.ps1
pwsh -File tools/Build.ps1
```

CI 不具备游戏许可证或游戏程序集，只检查 .NET 纯逻辑/loopback、模拟服务构建、脚本语法和源文件发布风险；绿色 CI 不等于宿主或 MC 实机通过。上述 .NET 测试无需外部测试框架或游戏资源。

Java 修改还需按 `docs/M3.md` 使用锁定 JDK/Fabric 工具链运行 `tools/BuildMinecraft.ps1 -ConfirmDependencyDownload`；先确认依赖下载范围。构建执行 Java 替身检查与 C#/Java 帧兼容检查，不启动真实 MC 世界。涉及引擎行为时另行记录在明确授权测试世界中的实机证据，不能以替身测试代替。

## 安全和许可

- 不修改原版 DLL，不覆盖其他模组，不使用用户主存档测试。
- 不贡献提取的原版模型、贴图、游戏 DLL、游戏文件或未经许可的资源。新自制资源须注明来源及许可。
- 日志、截图与复现资料须脱敏；凭据泄露后应撤销/轮换，删除文件不能撤销泄露。
- 提交贡献表示你有权按本项目 MIT 许可提供这些内容；不要求转让著作权，也不添加未经同意的署名。
- 漏洞报告参见 [SECURITY.md](SECURITY.md)。

请尊重其他贡献者，讨论具体行为与证据。性能结论不能从少量方块外推到无限世界；缺少实测的能力不能写成已支持。
