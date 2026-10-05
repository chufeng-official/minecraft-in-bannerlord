# Bannerlord 动态方块原型

一个在《骑马与砍杀：霸主》（Mount & Blade II: Bannerlord）单人自定义战斗中，添加**可建造、可碰撞、可干净清理**动态方块的概念验证。它是 [`TECHNICAL_PLAN.md`](TECHNICAL_PLAN.md) 的 M0/M1 里程碑：先证明宿主能稳定承载动态方块，再考虑 Minecraft 接入。

本仓库只包含源码。**不是** Minecraft 移植；新增可选 M2 本机模拟桥接，不修改原版游戏文件，也不改动用户存档。

## 当前状态

原型，在一台本机 `v1.4.8.119303` 安装上完成实测。编译通过不等于实机验收通过；哪些项目测过、哪些没测，见 [docs/M0-M1.md](docs/M0-M1.md)。

已实测确认：模块加载、动态显示、玩家/投射物碰撞、选择与删除、离场清理、骑乘与 AI 表现、无碰撞放置预览。

不作承诺：大规模性能、动态寻路导航、在任意原版表面上建造、战役持久化、Minecraft 接入。

M2 模拟服务桥接核心已实测通过：正常建造、断线保护、服务重启后自动同步、恢复后操作及退出清理；未测试的异常路径和性能不作承诺。默认仍为 M1 单机模式。启用方式、协议限制与测试记录见 [docs/M2.md](docs/M2.md)。

M3-A 石头权威核心已实测：真实方块双向同步、宿主删除、两端清理及 MC 正常退出/重新进入后的断线恢复。背包尚未接入，不代表整个 M3 完成；非空快照恢复等未测边界见 [docs/M3.md](docs/M3.md)。

## 功能

- 在 8 米瞄准距离内，向瞄准的地面放置一个一米方块，或沿已有方块的六个面扩建。
- 统一的一米垂直网格，使相邻方块在高低不平的地形上保持对齐。
- 绿色/红色放置预览跟随视线，反映该位置是否允许放置。
- 选中高亮、删除、清空，并带有保守的重叠保护（不与角色或马匹重叠）。
- 每场独立诊断（方块数、tick 耗时、托管内存）与离场清理。

## 环境要求

- Windows，以及你自行合法拥有的《骑马与砍杀：霸主》安装。
- .NET SDK（版本见 `global.json`）；游戏程序集仅作为本机编译引用。
- PowerShell 7（`pwsh`）。

不需要任何 NuGet 包、Minecraft、Harmony 或 Java。

## 构建

游戏目录**从不硬编码**。请用环境变量或脚本参数指向你自己的安装目录：

```powershell
$env:BANNERLORD_GAME_DIR = '<你的 Bannerlord 安装目录>'
pwsh -File tools/Build.ps1
# 或
pwsh -File tools/Build.ps1 -GameDirectory '<你的 Bannerlord 安装目录>'
```

构建产物在 `artifacts/BannerlordBlocks`，只包含本项目 DLL 和 `SubModule.xml`。

### IDE 配置

使用 VSCode / Rider 时，把 `src/BannerlordMod/GameDirectory.local.props.example` 复制为
`GameDirectory.local.props`，填入本机安装根目录。该文件被 `.gitignore` 忽略，不会进入仓库；
它能让 IDE 解析游戏程序集引用，并避免设计时构建报错。命令行脚本也会读取它作为兜底。

## 部署与卸载

这两个脚本会写入游戏安装目录，因此必须显式指定确认参数，且需先关闭游戏。

```powershell
# 只新增 Modules\BannerlordBlocks；已存在同名模组时拒绝覆盖。
pwsh -File tools/Deploy.ps1 -ConfirmDeployment

# 只删除清单中列出、且哈希未变化的文件。
pwsh -File tools/Remove.ps1 -ConfirmRemoval
```

## 游戏内操作

仅在战斗模式生效。注意：瞄准基于角色眼部视线，与第三人称屏幕中心并不完全一致。

| 按键 | 功能 |
| --- | --- |
| Insert | 在瞄准的地面放置，或沿瞄准的方块面扩建 |
| Ctrl + Insert | 开关放置预览 |
| PageUp | 选中方块（黄色高亮） |
| Delete | 删除选中的方块 |
| PageDown | 清空全部方块 |
| Ctrl + PageUp | 显示状态并写入一条诊断记录 |

测试步骤与诊断字段含义见 [docs/TEST_CHECKLIST.md](docs/TEST_CHECKLIST.md)。

## 测试

不依赖游戏的测试可直接运行：

```powershell
dotnet run --project tests/Diagnostics/Diagnostics.csproj -c Release
dotnet run --project tests/Bridge/Bridge.csproj -c Release
pwsh -File tools/AuditSource.ps1
```

CI 只运行这些检查；它没有游戏许可证和程序集，因此**绿色构建不代表模组能加载或物理有效**。

## 诊断

运行日志：`%LOCALAPPDATA%\BannerlordBlocks\prototype.log`。

```powershell
pwsh -File tools/ReadDiagnostics.ps1 -Tail 100
```

日志同目录下的 `prefab.txt` 可覆盖默认的 `editor_cube` 资源名。

## 许可与资源边界

原创源码、脚本与文档按 [MIT 许可](LICENSE) 发布。该许可**不涵盖** TaleWorlds / Bannerlord 或 Mojang / Minecraft 的内容、名称与商标。游戏程序集与原版资源不包含在本仓库内，也不作再分发；模组仅在运行时引用本机游戏文件并加载本机 `editor_cube` / `bo_editor_cube` 资源。详见 [NOTICE.md](NOTICE.md)。本项目与 TaleWorlds、Mojang、Microsoft 无官方关联，也未获背书。

## 参与贡献与安全

- [贡献指南](CONTRIBUTING.md)
- [安全说明](SECURITY.md) —— 漏洞请私下报告，不要开公开 Issue
- [更新日志](CHANGELOG.md)

## English

This repository is a singleplayer Bannerlord prototype for player-built, collidable,
cleanly removable blocks (the M0/M1 milestone of [`TECHNICAL_PLAN.md`](TECHNICAL_PLAN.md)).
Source is MIT-licensed; game assemblies and vanilla assets are never bundled. Full English
documentation is in [README.md](README.md).
