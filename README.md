# win-codexbar-widget

Windows 11 小部件面板里的 AI 用量监控 —— [Win-CodexBar](https://github.com/nesszer/Win-CodexBar) 的小组件扩展，与原项目零代码耦合，只通过它自带的 `codexbar-cli` 读取数据。

> A Windows 11 Widgets board extension for Win-CodexBar: check your AI coding-tool quotas and API balances at a glance (Win+W), powered by `codexbar-cli`. No code coupling with the upstream project.

## 它能做什么

- 按 `Win+W` 打开小部件面板就能看到各家 AI 服务的用量，不必先打开 CodexBar
- 显示哪些服务商，完全由你在 Win-CodexBar 里**启用的列表**决定，顺序与应用内一致
- **窗口型**服务商（Claude / Codex / Kimi 等）：每个窗口一行——已用百分比 + 进度条 + 重置时间；≥70% 警告色、≥90% 红色
- **余额型**服务商（DeepSeek 等纯 API 余额）：直接显示余额（如 ¥29.99）+「已充值 / 赠送」明细，不用无意义的百分比窗口
- 登录过期、获取超时等错误用中文提示，不再抛原始报错
- 手动刷新按钮 + 90 秒缓存，底部带更新时间

## 前置条件

1. Windows 11（x64 或 ARM64 均可）
2. 已安装 **Win-CodexBar**，并在里面登录 / 启用你要看的服务商——小组件展示的是它的数据，自身不保存任何凭据
3. Windows 开发者模式（侧载安装需要；脚本 01 会尝试帮你打开）

## 安装

```powershell
git clone https://github.com/liuxiaofengone/win-codexbar-widget.git
cd win-codexbar-widget
powershell -ExecutionPolicy Bypass -File scripts\00-check-env.ps1
powershell -ExecutionPolicy Bypass -File scripts\01-setup-dev.ps1
```

01 需要管理员权限，且装完要**关掉终端重新打开**。然后：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\02-build.ps1
powershell -ExecutionPolicy Bypass -File scripts\03-install.ps1
```

ARM64 机器（如骁龙笔记本）构建时给 02 加参数：`02-build.ps1 -Rid win-arm64`。

装完按 `Win+W`，在小部件面板里把「AI 用量」固定即可。

## 工作原理

一个打包的 C# 应用（MSIX）以进程外 COM 方式实现 `IWidgetProvider`，界面用 Adaptive Cards 渲染。每次刷新时它调用 `codexbar-cli usage -f json` 并解析输出；凭据只留在 CodexBar 内部，小组件进程不接触任何 cookie / token。

## 已知限制

- 刷新由 Windows 小部件系统托管，不是秒级实时

## 图标文件

仓库已包含一套占位图标（`src/CodexBarWidget/Assets`），克隆下来可直接构建。想换成自己的图标，按以下文件名与尺寸替换即可：

- `Square44x44Logo.png`（44×44）
- `Square150x150Logo.png`（150×150）
- `StoreLogo.png`（50×50）
- `ProviderAssets/WidgetIcon.png`（150×150）
- `ProviderAssets/WidgetScreenshot.png`（792×792）

## 致谢

基于 [Win-CodexBar](https://github.com/nesszer/Win-CodexBar)（MIT）构建，仅依赖其 CLI 的输出契约。已在 Windows 11 实测：DeepSeek 余额行、Kimi 双窗口均渲染正确。
