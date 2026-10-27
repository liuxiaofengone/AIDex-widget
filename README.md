# win-codexbar-widget

Windows 11 小部件面板里的 AI 用量监控 —— 数据来自 [Nowdex](https://nowdex.app/zh-hans/windows/)（Microsoft Store 免费原生应用，支持 x64 / Arm64），小组件只读它落在本地的快照文件，不跑 CLI、不联网、不碰任何凭据。

> A Windows 11 Widgets board extension: check your AI coding-tool quotas and API balances at a glance (Win+W), powered by Nowdex's local snapshot files. No CLI, no network, no credentials touched.

（仓库名保留 win-codexbar- 只是历史原因：2026-10-27 起数据源已从 Win-CodexBar 的 `codexbar-cli` 切换为 Nowdex，旧的 CLI 取数与 3 分钟预取任务全部退役。）

## 它能做什么

- 按 `Win+W` 打开小部件面板就能看到各家 AI 服务的用量，不必先打开 Nowdex
- 显示哪些服务商，完全由你在 Nowdex 里**登录 / 启用的列表**决定，顺序与 Nowdex 内一致
- **窗口型**服务商（Codex / Grok / Antigravity / Kimi / Cursor 等）：右边是主窗口剩余百分比 + 重置时间（24 小时内写相对说法，如“45 分钟后重置”，之外写绝对日期，如“10月30日 10:03 重置”），次窗口剩余放左边第二行；剩余按阈值着色，≤10% 红、≤30% 黄、其余蓝；主窗口名让位给了重置时间，分段条去 Nowdex 窗口看
- **余额型**服务商（DeepSeek 等纯 API 余额）：右边直接显示余额（如 ¥29.99），左边第二行显示「已充值 / 赠送」明细
- 卡片为自选股式行块：每家服务商一行浅底块，行数跟随尺寸——小 1 行、中 3 行、大 5 行（按服务商数封顶，多余的服务商点卡片进 Nowdex 窗口看）；底部依次是更新时间 + 刷新与「查看全部用量」链接（居中蓝色文字，仿第一方小组件底部链接样式），保证链接不被裁掉
- 整卡可点（仿第一方小组件）：卡片根节点带 selectAction，点卡片任意位置（刷新按钮除外）都拉起 Nowdex 窗口，底部链接只是视觉提示
- 登录过期、文件缺失等错误用中文提示，不再抛原始报错

## 前置条件

1. Windows 11（x64 或 ARM64 均可）
2. 已安装 **Nowdex**，并在里面登录 / 启用你要看的服务商——小组件展示的是它的数据，自身不保存任何凭据；Nowdex 托盘进程需要保持运行，文件才会被持续刷新
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

如果你之前用的是 CodexBar 版，请注销旧的预取任务（否则它还会每 3 分钟跑一次已经没人读的 CLI 取数）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\05-register-prefetch-task.ps1 -Unregister
```

## 数据源（Nowdex）

小组件每次刷新时直接读 Nowdex 的快照文件，不经过网络：

- `%LOCALAPPDATA%\Packages\RandyLu.Nowdex_*\LocalState\reports.json`（用量快照，含窗口、余额、更新时间）
- 同目录 `reset-deadlines.json` 与 `connections.json` 只用来解析服务商名称（多个同类包时取 reports.json 最新的一份）

几点说明：

- 新鲜度跟着 Nowdex 走：Nowdex 托盘常驻时按它自己的刷新间隔（1 / 5 / 15 分钟）更新文件；小组件不承诺秒级实时，过期标注按 Nowdex 自己的刷新间隔算：至少 5 分钟且至少两倍间隔（1 分钟档就是 5 分钟没新数据标过期，15 分钟档则是 30 分钟）；
- 服务商名称优先按 Nowdex 自身的窗口顺序（DisplayOrder）排列窗口，余额型服务商没有窗口，只有余额行；
- `reports.json` 是 Nowdex 的内部文件、不是公开 API，解析做了容错：将来 Nowdex 改版改了字段，小组件会降级显示「暂无用量数据」/「未命名服务」，不会崩溃；
- 隐私边界：小组件只读上面三个快照文件，`credentials` 文件夹从不打开，任何凭据都不会离开本机。

## 工作原理

一个打包的 C# 应用（MSIX）以进程外 COM 方式实现 `IWidgetProvider`，界面用 Adaptive Cards 渲染。每次更新（创建、激活、点刷新、改尺寸）都重新读一次 Nowdex 快照文件并重算行数；点卡片通过 `shell:AppsFolder\…!App` 拉起 Nowdex 窗口。

## 已知限制

- 面板关闭时小组件进程不运行；Nowdex 托盘彻底退出后文件会冻结，小组件继续显示旧数据并在按刷新间隔算出的阈值后标「可能已过期」
- 定时通知不在计划内：Nowdex 自带的检查已覆盖提醒需求，没必要再做一层
- 旧的 `CodexBarClient.cs`（CLI 取数）与预取脚本（`prefetch-usage.ps1`、`prefetch-launcher.vbs`）已不再被调用，仅留在仓库作历史参考；05 脚本保留，仅用于注销旧任务

## 图标文件

仓库已包含一套占位图标（`src/CodexBarWidget/Assets`），克隆下来可直接构建。想换成自己的图标，按以下文件名与尺寸替换即可：

- `Square44x44Logo.png`（44×44）
- `Square150x150Logo.png`（150×150）
- `StoreLogo.png`（50×50）
- `ProviderAssets/WidgetIcon.png`（150×150）
- `ProviderAssets/WidgetScreenshot.png`（792×792）

## 致谢

数据源为 [Nowdex](https://nowdex.app/zh-hans/windows/)。早期版本基于 [Win-CodexBar](https://github.com/nesszer/Win-CodexBar)（MIT）的 CLI 构建，2026-10-27 起已切换。已在 Windows 11 实测：DeepSeek 余额行、Kimi 双窗口均渲染正确。
