# NtoNTier

Windows 虚拟局域网联机工具 —— 基于开源 n2n 引擎，**小白也能秒懂**的组网软件。

把不同网络下的电脑（宿舍、家里、公司）组成一个虚拟局域网，互相 Ping 通、共享文件、局域网联机游戏。

---

## 功能

### 联机（n2n 打洞组网）
- 一键连接公共 supernode 节点（启动时自动测延迟排序，支持手动刷新）
- 或连接自建 / 好友的 supernode（`IP:端口` 即可）
- 连接方式实时显示：🟢 打洞直连 / 🟠 服务器中继
- supernode 掉线自动检测（状态点变黄提示，恢复自动还原）
- 虚拟 IP 手动配置（网段相同 + 后缀唯一）或 DHCP 自动获取
- 密钥（community）隔离，固定/随机 UDP 端口，TAP 驱动一键安装，防火墙一键放行

### 在线成员
- 连接后自动扫描虚拟局域网成员（edge peer 表 + 网段 Ping 探测互补）
- 手动「刷新」按钮（不自动轮询，避免网络与 UI 开销）
- 双击成员 → 自动探测其文件共享端口（8080 优先，8000-8099/8888/9000 兜底）→ 内置浏览器跳转浏览
- 右键成员 → **发送文件（P2P 高速直传）**

### 文件共享（HFS 内嵌）
- 内置 HFS 3.2.2 文件服务器，一键启动/停止，端口可配置（默认 8080）
- 独立工作目录、自动放行防火墙、跳过更新检查（国内网络干净启动）
- 虚拟局域网内其他成员浏览器访问 `http://虚拟IP:8080` 即可浏览下载

### 文件浏览（内置浏览器）
- WebView2 内核（Chromium），地址栏直达虚拟 IP / 任意网址
- HFS 文件链接自动拦截 → 交给内置 **IDM 式分片多线程下载引擎**

### 下载引擎（IDM 式，自研）
- GET+Range 探测服务器是否支持断点续传（HFS 的 HEAD 不返回 Accept-Ranges，已针对性处理）
- 按文件大小动态分片：<1MB 2线程 / <10MB 4线程 / <100MB 8线程 / 更大满线程（默认16）
- 每分片独立线程 + Range 并行下载，Keep-Alive 连接复用
- 失败自动重试 3 次（指数退避），滑动窗口速度采样（5 点平均）
- 断点续传（.part 保留可恢复），完成后先校验总大小（容忍 1% 或 4KB）再合并
- 下载面板实时显示：百分比 / 分片进度 / 速度 / 线程数；右键暂停·继续·重新下载·删除·打开

### 文件传输（P2P 高速直传）
- 右键在线成员「发送文件」→ TCP 虚拟 IP 直连（打洞成功时不经过服务器）
- 自定义轻量协议，多线程分片并行传输 + 断点续传 + 校验

### 网络测试
- Ping / TCP 端口 / UDP 探测，后台线程执行不卡 UI

### 界面
- 无边框圆角现代窗口，暗色 / 亮色双主题一键切换
- 左侧导航：联机功能 / 在线成员 / 网络测试 / 文件共享 / 文件浏览 / 使用指南 / 更多
- 全自绘控件（无系统控件外观依赖），全局字体 Microsoft YaHei UI

---

## 技术栈

| 层 | 技术 |
|---|---|
| 语言 / 框架 | C# 5 / .NET Framework 4.8（`csc.exe` 编译，免 SDK） |
| 组网引擎 | n2n 3.1.1 r1255（edge.exe 内嵌） |
| 文件共享 | HFS 3.2.2（hfs.exe 内嵌） |
| 内置浏览器 | WebView2（Chromium，随系统运行时） |
| 下载引擎 | 自研 IDM 式分片多线程（参考 bezzad/Downloader 思路，非内嵌商业软件） |
| 打包 | Inno Setup 6（单文件安装包，内嵌三大引擎） |

> 依赖的 n2n / HFS / WebView2 均为开源或免费组件。n2n 为 GPL-3.0，本项目遵循相应许可并保留来源标注。

---

## 构建

### 准备引擎文件
把以下文件放到 `dist/client/`（构建脚本会自动内嵌为资源）：
- `edge.exe`（n2n 3.x Windows 客户端）
- `tap-windows.exe`（TAP-Windows 驱动安装器）
- `hfs.exe`（HFS 3.x 文件服务器）

> 这些二进制体积较大（hfs.exe 约 95MB），故不入库，请从各自官方渠道获取。

### 编译客户端
```bat
cd dist\client\src
build.bat
```
产物：`dist/client/src/NtoNTier.exe`（单文件，内嵌全部引擎），随后自动复制 WebView2 DLL。

### 打包安装包（可选）
```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\NtoNTier-setup.iss
```
产物：`dist/NtoNTier-vX.Y.Z-Setup.exe`（约 291MB，含引擎）。

### 自建 supernode（服务端）
见 `dist/server/`（Linux 一键部署脚本 + Windows 图形控制台）。

---

## 目录结构

```
dist/client/src/          客户端全部源码（13 个 .cs）
  Theme.cs                设计系统 / 主题（暗亮色）
  Controls.cs             自绘控件库（标题栏/按钮/输入框/下拉/抽屉/导航）
  Config.cs               配置持久化（config.json）
  Net.cs                  edge 进程管理 + 网络工具（查询/扫描/测试/防火墙）
  ResourceBootstrap.cs    引擎资源自举（edge/hfs/tap 解压）
  HfsManager.cs           HFS 文件共享引擎管理
  DownloadManager.cs      IDM 式分片多线程下载引擎
  FileTransfer.cs         P2P 高速文件传输（TCP 直连 + 分片）
  BrowserPage.cs          内置浏览器页（WebView2 + 下载面板）
  MainForm.cs             主窗口 / 页面 / 逻辑编排
  ConnectDialog.cs        联机配置弹窗
  Onboarding.cs           引导组件（已停用，保留截图工具引用）
  Program.cs              入口（--shot 自检截图模式）
installer/                Inno Setup 脚本
dist/server/              supernode 服务端部署
```

---

## 使用（三分钟上手）

1. 安装运行 NtoNTier，左侧「联机功能」
2. 选官方节点（或填自建/好友 `IP:端口`），**同一服务器、同一网段、不同后缀**（如 10.10.30.100 / 10.10.30.101）
3. 点「连接」，状态变绿即组网成功
4. 「在线成员」查看对方；双击成员浏览其共享文件，右键发送文件
5. 「文件共享」启动后，对方浏览器访问 `http://你的虚拟IP:8080`

> 首次使用需安装 TAP 虚拟网卡驱动（程序内一键安装，需管理员权限）。

---

## 已知限制
- 内置 edge peer 表只显示最近活跃 peer，已用网段 Ping 扫描兜底
- HFS 为明文 HTTP，仅建议在可信虚拟局域网内共享
- 公共 supernode 节点可能失效，建议联机前「刷新延迟」确认，长期使用建议自建

## License
- 项目代码：见 LICENSE
- 依赖引擎：n2n (GPL-3.0) / HFS / WebView2 Runtime 版权归各自作者
