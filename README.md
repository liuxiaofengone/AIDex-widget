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
- 卡片底部有「查看全部用量」按钮（仅当找到 CodexBar 桌面程序时显示），点一下就拉起 CodexBar 桌面窗口——服务商多、大尺寸也放不下时从这里看全量
- 后台预取（推荐）：打开面板即显示最新数据，不用等取数，见下

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

（推荐）再注册后台预取任务，让打开面板时直接显示最新数据：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\05-register-prefetch-task.ps1
```

## 后台预取（打开即最新）

小组件进程只在面板打开时才运行，所以"打开时现取"注定要等 CLI 启动加逐个服务商请求的时间。预取把取数和打开解耦：

- 计划任务 `CodexBarWidget Prefetch` 每 3 分钟运行一次 `scripts\prefetch-usage.ps1`，跑 `codexbar-cli usage -f json` 并把结果写到 `%LOCALAPPDATA%\CodexBarWidget\latest.json`（临时文件 + 原子替换，读方永远不会读到写一半的文件；取数失败时绝不覆盖旧文件）。任务动作走 `scripts\prefetch-launcher.vbs`（wscript 隐藏启动）：直接跑 powershell.exe 时 `-WindowStyle Hidden` 是窗口建出来之后再隐藏，有些系统上每 3 分钟会闪一下控制台，wscript 以窗口样式 0 启动则从创建起就是隐藏的；
- 小组件打开时先读这个文件，毫秒级就能画出来，同时后台再刷新一次兜底；
- 文件没更新也没关系，显示的就是上一次缓存，超 5 分钟沿用"可能已过期"标注。语义就是跟着 CodexBar 的数据走，不承诺秒级实时。

任务指向 `%LOCALAPPDATA%\CodexBarWidget\` 下的脚本副本，仓库移动 / 删除后任务仍可用；脚本更新后重跑 05 即可同步副本。注销任务：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\05-register-prefetch-task.ps1 -Unregister
```

## 工作原理

一个打包的 C# 应用（MSIX）以进程外 COM 方式实现 `IWidgetProvider`，界面用 Adaptive Cards 渲染。每次刷新时它调用 `codexbar-cli usage -f json` 并解析输出；凭据只留在 CodexBar 内部，小组件进程不接触任何 cookie / token。

## 已知限制

- 面板关闭时小组件进程不运行，数据新鲜度依赖预取任务（3 分钟一档），不承诺秒级实时
- 定时通知不在计划内：CodexBar 自带的用量告警通知已覆盖提醒需求，没必要再做一层

## 图标文件

仓库已包含一套占位图标（`src/CodexBarWidget/Assets`），克隆下来可直接构建。想换成自己的图标，按以下文件名与尺寸替换即可：

- `Square44x44Logo.png`（44×44）
- `Square150x150Logo.png`（150×150）
- `StoreLogo.png`（50×50）
- `ProviderAssets/WidgetIcon.png`（150×150）
- `ProviderAssets/WidgetScreenshot.png`（792×792）

## 致谢

基于 [Win-CodexBar](https://github.com/nesszer/Win-CodexBar)（MIT）构建，仅依赖其 CLI 的输出契约。已在 Windows 11 实测：DeepSeek 余额行、Kimi 双窗口均渲染正确。
