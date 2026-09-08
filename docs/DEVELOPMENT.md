# NtoNTier 开发文档

> 面向开发者 / 维护者的完整技术文档。使用者入门见根目录 `README.md`。
> 当前版本：v1.4.0 · n2n 3.1.1 r1255 · .NET Framework 4.8

---

## 1. 项目定位

NtoNTier 是一款 **Windows 虚拟局域网联机工具**，解决"不同网络下的电脑如何像在同一个局域网里"的问题：

- 基于开源 n2n 引擎（P2P 打洞 + 服务器中继）组建虚拟局域网
- 内置 HFS 文件服务器，成员间可互相浏览/下载文件
- 内置自研 IDM 式分片多线程下载引擎（非商业 IDM 外壳）
- 内置 P2P 高速文件传输（TCP 直连 + 分片 + 续传）
- 附 Android 客户端（n2n VPN + HFS 浏览/下载）与自建 supernode 服务端

目标用户：需要跨网络联机游戏、共享文件的普通用户（强调"小白也能秒懂"）。

---

## 2. 系统架构

```
┌─────────────────────────────────────────────────────────┐
│                      NtoNTier 三层架构                     │
├───────────────┬──────────────────┬──────────────────────┤
│  Windows 客户端 │  服务端 (可选)     │  Android 客户端 (可选)  │
│  dist/client  │  dist/server      │  android/            │
│               │                  │                      │
│  C# / WinForms │  supernode 控制台  │  Kotlin / VpnService │
│  13 个 .cs     │  supernode.exe    │  OkHttp + Jsoup      │
│  内嵌 3 引擎    │  Linux 一键部署    │  分片下载 8 线程       │
└───────┬───────┴────────┬─────────┴──────────┬───────────┘
        │                │                    │
        └────────────────┴────────────────────┘
                       n2n 虚拟局域网
            （P2P 打洞直连 / supernode 中继）
```

### 运行时组件关系（Windows 客户端）

| 组件 | 来源 | 角色 |
|---|---|---|
| `edge.exe` | n2n 3.1.1 r1255 | 虚拟网卡守护进程，负责打洞/中继/组网 |
| `tap-windows.exe` | TAP-Windows 驱动 | 安装虚拟网卡驱动（首次需管理员） |
| `hfs.exe` | HFS 3.2.2 | 内嵌文件共享服务器（HTTP，默认 8080） |
| WebView2 Runtime | 系统级 | 内置浏览器内核（Chromium） |
| `NtoNTier.exe` | 自研 | 主程序（内嵌上述三引擎为资源） |

---

## 3. 客户端模块设计（`dist/client/src/`）

共 13 个 `.cs` 源文件，`build.bat` 用 `csc.exe` 直接编译（无 SDK 依赖）。

| 文件 | 职责 | 关键点 |
|---|---|---|
| `Program.cs` | 程序入口 | `--shot` / `--capture` 自检截图模式；单实例；启动引导 |
| `MainForm.cs` | 主窗口 / 页面编排 | 7 页导航、成员列表、连接状态机、版本号（侧栏 `v1.4.0`） |
| `Config.cs` | 配置持久化 | `DataContractJsonSerializer` → `config.json`；下载线程数等 |
| `Theme.cs` | 设计系统 | 暗/亮主题色板、字体（Microsoft YaHei UI）、控件绘制样式 |
| `Controls.cs` | 自绘控件库 | 无边框标题栏、FlatButton、输入框、下拉、抽屉、导航项 |
| `Net.cs` | edge 进程与网络 | edge 启动/停止、注册查询、成员扫描（254 并发 + 2.5s 超时）、Ping、端口、防火墙 |
| `ResourceBootstrap.cs` | 引擎资源自举 | 首次运行从嵌入资源解压 edge/hfs/tap；已存在则跳过（文件大小判定） |
| `HfsManager.cs` | HFS 管理 | 启动/停止、就绪轮询（127.0.0.1:port）、`DISABLE_UPDATE=1`、防火墙放行 |
| `DownloadManager.cs` | 下载引擎 | IDM 式分片多线程、断点续传、重试、合并校验、进度节流 |
| `FileTransfer.cs` | P2P 文件传输 | TCP 监听 8081，自定义协议 GET/PUT/SIZE，分片+续传+路径防穿越 |
| `BrowserPage.cs` | 内置浏览器页 | WebView2 控件、地址栏、下载拦截→DownloadTask、下载面板 |
| `ConnectDialog.cs` | 联机配置弹窗 | 节点/网段/IP/密钥/高级参数 |
| `Onboarding.cs` | 引导组件 | 已停用（按用户要求移除），仅保留 `--shot` 截图模式引用 |

---

## 4. 核心机制详解

### 4.1 n2n 集成与连接生命周期

```
连接请求
  → Net.StartEdge(): 解压/启动 edge.exe，带参数
       -c <community> -k <key> -l <supernode:port> -a <虚拟IP>
       -r (路由模式) -E (组播) --mgmt-port <5644>
  → 轮询 mgmt 端口 / edge 日志确认 TAP 就绪
  → 状态 = 已连接（绿点）
掉线检测：每 3 轮（约 9 秒，节流）查询 supernode 可达性 → 不可达黄点 → 恢复自动还原
连接方式判定：QueryLinkStatus 区分 🟢 打洞直连 / 🟠 服务器中继
```

- edge 通过 **mgmt 端口（默认 5644）** 暴露状态，`QueryEdgeStatus` / `QuerySupernodeReachable` / `QueryLinkStatus` 用 TCP 文本命令查询。
- 成员扫描：edge peer 表 + 网段 Ping 探测互补；254 个并发 `Ping`，`CancellationToken` 2.5 秒超时；**只手动刷新，不自动轮询**（避免连接后卡顿）。
- 防火墙：`netsh advfirewall` 放行 edge/HFS 端口（`RunNetsh`，提权执行）。
- 公共节点：启动时按延迟排序（bj/sh/gz/cd/融合/美国 6 节点），支持「刷新延迟」重排。
- 重要：NtoNTier manifest 为 `requireAdministrator`（提权运行，才能装 TAP 驱动、写防火墙）。

### 4.2 资源自举（ResourceBootstrap）

- 三大引擎以嵌入资源（`/resource:..\edge.exe,edge.exe` 等）打进单文件。
- `EnsureExtracted()`：解压到工作目录；**文件已存在且大小一致则跳过**（v1.4.0 优化：避免每次启动把 95MB hfs.exe 读进内存）。

### 4.3 HFS 集成（HfsManager）

- 启动 `hfs.exe`，工作目录独立（`%LOCALAPPDATA%\NtoNTier\hfs`）。
- 就绪判定：轮询 `127.0.0.1:<port>` TCP 可达（注意：若该端口被其他进程占用会误判，验收时需甄别）。
- 环境变量 `DISABLE_UPDATE=1`：跳过 HFS 更新检查，国内网络干净启动。
- 启动后弹出凭据弹窗（管理员账号/密码），供虚拟局域网成员登录。

### 4.4 下载引擎（DownloadManager）—— IDM 式自研

**分片探测**（HFS 特殊处理）：
- HFS 的 `HEAD` 请求**不返回** `Accept-Ranges`，直接探测会误判为不支持续传（永远单线程）。
- 改用 `GET + Range: bytes=0-0`：HFS 返回 `206 + Content-Range` → 判定支持分片；同时从 Content-Range 拿到总大小。

**动态分片**：

| 文件大小 | 线程数 |
|---|---|
| < 1MB | 2 |
| < 10MB | 4 |
| < 100MB | 8 |
| ≥ 100MB | 16（`Config.DownloadThreads` 默认，可改） |

**可靠性**：
- 每分片独立线程 + `Range` 并行下载，`Keep-Alive` 连接复用
- 失败自动重试 3 次，指数退避
- 断点续传：`.part` 文件按分片写入；重新下载时先统计已下分片大小，跳过已完成区间
- 完成校验：先汇总 `.part` 总大小与目标大小比对，容忍 `Max(TotalSize/100, 4096)`（1% 或 4KB），通过后合并为最终文件；失败保留 `.part` 可重试
- 速度统计：滑动窗口 5 个采样点平均

**UI 节流**（v1.4.0 流畅度优化）：
- `Fire()` 进度事件 120ms 节流（每 256KB 一次原始触发被合并为 ≤8 次/秒）
- 状态变化（Completed/Failed/Paused/Canceled）强制立即刷新
- 下载面板实时显示：百分比 / 分片进度 / 速度 / 线程数；右键暂停·继续·重新下载·删除·打开

### 4.5 P2P 文件传输（FileTransfer，8081）

自定义轻量文本协议（TCP）：

| 请求 | 格式 | 响应 |
|---|---|---|
| 拉取 | `GET <filename> <offset> <length>\n` | 数据流（长度=length） |
| 推送 | `PUT <filename> <size>\n` + size 字节数据 | 8 字节 `0`（成功） |
| 查大小 | `SIZE <filename>\n` | 8 字节大端 int64 |

- 文件名经 `Uri.EscapeDataString` 编码；**所有路径经 `SafeResolve()` 做共享目录边界校验**（防 `../` 路径穿越，v1.4.0 安全修复）。
- GET 的 offset/length 有解析与范围检查；PUT 的 size 有 1TB 上限。
- 传输同样分片 + 断点续传 + 校验；进度对话框实时显示。
- 入口：在线成员右键「发送文件（高速直传）」。

### 4.6 内置浏览器（BrowserPage）

- WebView2（`Microsoft.Web.WebView2.WinForms`），地址栏直达虚拟 IP / 任意网址。
- **关键环境变量**：`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--no-proxy-server`——否则本机 Clash 等代理会劫持 10.x 虚拟内网导致白屏（v1.2.7 修复）。
- 下载拦截：`CoreWebView2.DownloadStarting` → 取消 WebView 默认下载 → 构造 `DownloadTask` 交给 `DownloadManager`。
- 下载面板：ListView 五列（文件/大小/进度/速度/状态 + 线程/分片列）。

### 4.7 UI 自绘体系与 GDI 管理

- 无边框圆角窗口，全自绘控件（不依赖系统控件外观）：标题栏（拖拽/最小化/关闭/主题切换）、FlatButton、输入框、下拉、抽屉菜单、导航项、RoundedPanel。
- 暗色 / 亮色双主题：`Theme.cs` 统一色板。
- **GDI 资源管理**（v1.4.0 卡顿修复）：所有 `SolidBrush`/`Font` 在 `OnPaint` 中创建后必须 `using` 释放或静态缓存——此前标题栏/导航/ListView 绘制中每帧泄漏 GDI 对象，长时间运行导致卡顿与重绘异常。
- 页面切换增量重绘：`SetNavActive` 只刷新状态变化的导航项，不再整排 Invalidate。

---

## 5. 关键数据流

### 5.1 联机 → 浏览 → 下载

```
用户点击连接
  → Net.StartEdge (edge.exe + TAP)
  → 连接成功，虚拟 IP 生效
  → 在线成员页点「刷新」
      → edge peer 表 + 网段 Ping 扫描 → 成员列表
  → 双击成员
      → 探测其文件共享端口（8080 优先，8000-8099/8888/9000 兜底）
      → 切换到文件浏览页，WebView2 打开 http://<虚拟IP>:<端口>
  → 点击 HFS 文件链接
      → DownloadStarting 拦截 → DownloadManager
      → Range 探测 → 分片并行下载 → 合并 → 完成
```

### 5.2 文件传输

```
成员右键「发送文件」
  → 选择文件 → FileTransfer 客户端连 <虚拟IP>:8081
  → SIZE 查大小 → 分片 GET 并行拉取（或 PUT 推送）
  → 进度对话框 → 完成校验 → 落盘到下载目录
```

---

## 6. 配置模型（Config.cs）

`config.json`（DataContractJsonSerializer，UTF-8）：

| 字段 | 默认 | 说明 |
|---|---|---|
| `Community` | tudou | n2n community（密钥隔离） |
| `Key` | 空 | 加密 key |
| `SupernodePort` | 3301 | supernode 端口 |
| `OfficialServers` | 6 节点 | 公共节点列表（`名称=host:port` 换行分隔） |
| `NetworkPrefix` / `Netmask` | 10.10 / 24 | 固定 IP 段 |
| `Segment` / `Suffix` | 30 / 100 | 虚拟 IP 组成（10.10.30.100） |
| `HfsPort` | 8080 | 文件共享端口 |
| `HfsShareDir` / `HfsPassword` | 空 | 共享目录 / 密码 |
| `DownloadThreads` | 16 | 默认下载线程数 |
| `DownloadDir` | 空 | 下载保存目录 |
| `TransferPort` | 8081 | P2P 传输监听端口 |
| `Dark` | true | 主题 |

---

## 7. 构建与发布

### 7.1 编译客户端

```bat
cd dist\client\src
build.bat
```

- `csc.exe`（.NET Framework 4.8，`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）
- `/optimize+`；`/target:winexe`；内嵌 edge/tap/hfs 三引擎资源
- 产物 `dist/client/src/NtoNTier.exe` → 复制 WebView2 DLL 到 `dist/client/`
- 前提：`dist/client/` 下存在 `edge.exe` / `tap-windows.exe` / `hfs.exe`（体积大不入库，需自行准备）

### 7.2 打包安装包

```bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\NtoNTier-setup.iss
```

产物：`dist/NtoNTier-vX.Y.Z-Setup.exe`（约 291MB，含引擎），惯例再复制到桌面。

### 7.3 版本号发布流程

1. `MainForm.cs` 侧栏版本字符串：`NtoNTier vX.Y.Z · n2n 3.1`
2. `installer\NtoNTier-setup.iss`：`AppVersion` / `AppVerName` / `OutputBaseFilename` 三处
3. 编译 → 打包 → 复制桌面 → 冒烟验证

> 写脚本批量改版本号时，文件读写必须 `encoding='utf-8-sig'`（.cs 与 .iss 都是 UTF-8 BOM）。

---

## 8. 编码与工程规范（硬约束）

| 约束 | 原因 |
|---|---|
| 所有 `.cs` 必须 **UTF-8 BOM + CRLF** | PowerShell 5.1 会把无 BOM 的 UTF-8 按 ANSI/GBK 读取，中文变成乱码、破坏词法解析（编译或脚本报错） |
| 源文件换行 CRLF | 同上；git 已配 `core.autocrlf` 兜底 |
| NtoNTier 为提权进程 | GUI 自动化/杀进程必须在提权 PowerShell 内执行，普通进程操作会被 UIPI 静默拦截 |
| PowerShell 踩坑 | 结构体 `$move.mi.dx = X` 修改的是副本（SendInput 注入空事件）；`-f` 内联两个方法调用会被解析为逗号参数；Add-Type 含 System.Drawing 可能编译失败——分开赋值、拆行写 |
| 下载引擎扩展 | 新增下载逻辑必须保留：分片/续传/重试/校验/节流 五要素 |

---

## 9. 测试与验收

### 9.1 冒烟自检（`--shot`）

```bat
dist\client\src\NtoNTier.exe --shot
```

无 UI 交互自动截屏到 `frames\accept\`（主界面/暗亮主题/各页面），用于渲染与版本号验证。

### 9.2 GUI 验收框架（`frames\accept\`）

提权 PowerShell 脚本族（历史 5 项验收，v1.2.0 起）：

| # | 验收项 | 判据 |
|---|---|---|
| ① | 管理员启动 + 主界面 | 侧栏 7 项导航齐全、顺序正确 |
| ② | 启动文件共享 | `hfs.exe` 进程 + `:::8080` 监听（IPv6 any = 0.0.0.0） |
| ③ | 打开文件浏览器 | WebView2 宿主/渲染器在窗口内，地址栏显示虚拟 IP |
| ④ | 下载面板 | 五列结构（文件/大小/进度/速度/状态） |
| ⑤ | 关窗无残留 | WM_CLOSE 后 NtoNTier/hfs/WebView2 组全部退出，8080 关闭 |

关键经验：
- 点击自绘 FlatButton 需 `SendInput`（SetCursorPos 不产生 hover 事件）
- 本机 UAC 为静默自动批准（`ConsentPromptBehaviorAdmin=0`），提权可自动化
- 自动化点击后窗口可能被移出屏幕，用 `SetWindowPos` 恢复 (40,40) 1375×900

### 9.3 下载引擎验证

- 本机验证：`http://127.0.0.1:8080`（HFS 共享目录含 `test_5mb.bin`）
- `GET + Range: bytes=0-0` 应返回 `206 + Content-Range`（HFS 特性）
- 断点续传验证：下载中杀进程 → 重启 → 任务应跳过已完成分片

---

## 10. Android 客户端（`android/`）

- Kotlin + Jetpack（DataBinding、Navigation），单 Activity + Fragments
- 23 个 Kotlin 源文件 + 95 个 XML（布局/资源）
- 功能：
  - **n2n VPN**：`VpnService` + 前台服务，加载 edge 二进制建隧道（需自行 NDK 交叉编译放进 `jniLibs`，APK 未内置）
  - **成员扫描**：虚拟网段 Ping + HFS 端口探测，下拉刷新
  - **文件浏览**：OkHttp + Jsoup 解析 HFS 目录页
  - **下载**：8 线程分片 + Range + 暂停/继续/取消 + 通知进度
- 构建：`android/gradlew.bat assembleDebug` → `android/app/build/outputs/apk/debug/app-debug.apk`
- 已知限制：不能作为 HFS 服务端；edge 二进制需用户自行编译注入
- 审查记录见 `android/REVIEW.md`（2 轮审查修复 14 项，BUILD SUCCESSFUL 零警告）

---

## 11. 服务端（`dist/server/`）

- `supernode.exe`：n2n 官方超节点（约 295KB）
- `NtoNServerControl.exe`：Windows 图形控制台（自研，`dist/server/src/` 5 个 .cs 编译）
- `linux/deploy-supernode.sh`：Linux 一键部署
- 配置 `server-config.json`：`{"Port":3301, "MgmtPort":0, "AutoGuard":true, ...}`
- 自建节点用法：两端统一连 `你的IP:3301`，community 相同即可

---

## 12. 已知限制与后续路线

| 限制 | 说明 / 方向 |
|---|---|
| HFS 明文 HTTP | 仅建议可信虚拟局域网内使用；后续可加 HTTPS/鉴权增强 |
| edge peer 表不完整 | 已用网段 Ping 扫描兜底 |
| 公共 supernode 可能失效 | 启动延迟排序 + 刷新；长期用建议自建 |
| 安卓端无 edge 二进制 | 需 NDK 交叉编译注入；后续可提供 CI 构建 |
| 下载引擎为自研实现 | 与商业 IDM 非同一代码，但功能对齐（分片/续传/多线程） |
| 版本号曾出现未同步 | 发布时务必同步 MainForm 侧栏与 .iss 三处 |

---

## 附：工程约定速查

```text
项目根       C:\...\hole-punch-project
客户端源码   dist/client/src/（13 .cs + build.bat）
引擎         dist/client/{edge,tap-windows,hfs}.exe（不入库）
WebView2     dist/client/src/libs/*.dll（构建时复制）
安装脚本     installer/NtoNTier-setup.iss
服务端       dist/server/
安卓         android/
测试截图     frames/accept/
文档         README.md / docs/
```
