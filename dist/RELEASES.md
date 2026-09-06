# NtoNTier v1.0.0（封版）

> 封版日期：2026-09-04
> 状态：功能已稳定，联机联调通过，正式定版。后续功能开发在 v1.1+ 进行。

## 本版本功能清单

### Windows 客户端（dist\client\）
- **n2n 3.1.1 打洞组网**：内嵌 edge.exe（lucktu 版 3.1.1-71-r1255）+ tap-windows.exe 驱动，单文件分发
- **服务器地址自动清洗**：自动去掉 `http://`/`https://` 前缀（防止 edge 把 http 当主机名解析失败）
- **在线成员**：edge peer 表 + 主动并行 ping 扫描整个虚拟网段（10.10.10.1~254）
  - 扫描带 1.5 秒超时保护（防止个别 ping 卡死导致扫描停摆）
  - 缓存上次扫描结果，刷新不闪没
- **网络测试**：Ping / TCP / UDP 三种探测
- **极简扁平 UI**（参照 FreeToken 风格）：无边框圆角窗口、左侧导航、分段选择器、暗色/亮色主题
- **首次引导**（4 步可跳过）、**抽屉式"更多"菜单**（联机配置/安装TAP/卸载TAP/防火墙/日志/使用指南）
- **本机虚拟 IP**：默认 10.10.10.200（可配置），社区 `tudou`

### 服务端（dist\server\）
- **Linux 自建 supernode**：`linux\deploy-supernode.sh` 一键部署
  - 国内服务器 GitHub 不可达 → 多镜像轮询（ghfast.top/gh-proxy.com/ghproxy.net/gh.llkk.cc）+ 魔数校验
  - systemd 守护 + `-f` 前台模式修复
- **Windows 服务端**：NtoNServerControl.exe（supernode 图形控制）

### 联机联调结论（2026-09-03 实测）
- 三机（主机 10.10.10.200 / 虚拟机 10.10.10.158 / 朋友 10.10.10.102）互通 ✅
- 主机 ping 朋友 49-51ms、ping 虚拟机 2-3ms 全通
- 服务器：43.226.36.135:3301（UDP，community=tudou）

## 已确认的已知限制
- 内置 edge（lucktu 版）peer 表存在"只显示最近活跃 peer"的显示问题 → 已用网段扫描兜底解决
- `mgmt recvfrom failed: 34 - Result too large` 为无害日志噪音

## 备份位置
完整项目快照：`backup\NtoNTier-v1.0.0-20260904.zip`

---

# NtoNTier v1.1.0（文件共享版）

> 版本日期：2026-09-04
> 基于 v1.0.0 封版新增：内嵌 HFS（HTTP File Server）文件共享，适配虚拟局域网使用。

## v1.1.0 新增功能

### 文件共享（HFS 3.2.2 内嵌）
- **hfs.exe 内嵌进 NtoNTier.exe**（单文件分发，运行时解压），与 edge.exe 同一套资源自举机制
- **侧栏新增「文件共享」页**：
  - 一键「启动文件共享」/「停止」，运行状态实时显示（PID / 端口）
  - 端口可配置（默认 8080，避开 80 等常见端口冲突）
  - 共享目录可选择（默认 文档\NtoNTier共享），支持"浏览..."选择文件夹
  - 访问地址自动按虚拟 IP 生成（如 http://10.10.10.200:8080），一键在浏览器打开
- **适配细节**：
  - 独立工作目录 `%LOCALAPPDATA%\NtoNTier\hfs`（config.yaml / 数据 / 日志隔离，不污染用户目录）
  - `--no-central` 跳过 GitHub 更新检查（国内网络）
  - 自动生成 config.yaml 预设端口与共享目录；首次启动生成管理员账号 `admin` + 随机密码（界面弹窗告知）
  - `--consoleFile` 日志输出便于排查
- **使用方式**：本机启动后，虚拟局域网内其他成员浏览器打开 `http://本机虚拟IP:端口` 即可浏览/下载；本机 `http://localhost:端口` 进管理端可上传/管理

### 体积变化
- 客户端 NtoNTier.exe：约 96.8MB（内嵌 hfs.exe 95MB + edge.exe 1MB + 驱动 0.6MB）
- 首次运行会自动解压 hfs.exe 到程序目录（约 1-2 秒，仅一次）

## 已知限制
- HFS 监听所有网卡接口（含虚拟网卡），虚拟局域网内可访问；如需仅内网访问可后续加防火墙规则
- 未加密传输（HFS 默认 HTTP），仅建议在可信虚拟局域网内共享

## 备份位置
v1.1.0 源码改动：`frames\patch_hfs_ui.py` / `HfsManager.cs` / `ResourceBootstrap.cs` / `Config.cs` / `build.bat` / `MainForm.cs`

---

# NtoNTier v1.1.1（连接方式标签版）

> 版本日期：2026-09-04
> 基于 v1.1.0 封版新增：状态栏实时显示当前与成员的连接方式是「打洞直连」还是「服务器中继」，让"到底直连没有"一目了然。

## v1.1.1 新增功能

### 连接方式标签（直连 / 中继）
- **状态栏右侧新增彩色标签**，每 3 秒自动刷新，实时反映当前网络连接方式：
  - 🟢「● 打洞直连」：所有 peer 均为点对点直连（数据不经服务器）
  - 🟠「● 服务器中继」：peer 数据经 supernode 转发（打洞未成功）
  - 🟠「● 直连 + 中继」：部分 peer 直连、部分走中继
  - ⚪「● 暂无流量」：已连接但当前没有 peer 通信
- **判定原理**：连接后查询 edge 管理端口（UDP 5644），其统计输出分两段——`PEER TO PEER`（直连）与 `SUPERNODE FORWARD`（中继），客户端解析两段 peer 数得出标签
- **技术实现**：`Net.cs` 新增 `QueryLinkStatus()`（UDP 查询 + 段解析，异步不阻塞 UI）；`MainForm.cs` 新增 `_lblLinkMode` 标签 + `UpdateLinkMode/ApplyLinkMode`
- 解析逻辑已用真实 mgmt 输出 + 中继/混合场景样本验证（三场景计数正确）

## v1.1.1 修正
- **HFS 更新检查改用 `DISABLE_UPDATE=1` 环境变量**：实测 `--no-central` 对 HFS 3.2.2 无效（启动仍访问 GitHub），`DISABLE_UPDATE=1` 生效，保证国内网络干净启动

## 备份位置
v1.1.1 源码改动：`frames\patch_linkmode.py` / `Net.cs` / `MainForm.cs`
完整发布快照：`backup\NtoNTier-v1.1.1-20260904.zip`
