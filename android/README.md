# NtoNTier 安卓用户端

基于 n2n 的 P2P 文件共享工具安卓端，对应 Windows 端 NtoNTier v1.2.2。

## 功能说明

### 1. n2n VPN 连接
- 基于 Android VpnService 实现，无需 root
- 支持配置 Supernode 地址、社区名、虚拟 IP、子网掩码、加密 Key、MTU
- 支持开机自动连接
- 前台服务保活，带连接状态通知
- n2n edge 二进制集成：支持从 `jniLibs` 加载交叉编译的 edge 二进制（armeabi-v7a/arm64-v8a）
- 未集成二进制时运行于框架模式（VPN 接口可建立，但 n2n 隧道需二进制支持）

### 2. 在线成员扫描
- 通过 n2n 虚拟网段 Ping 扫描发现同社区成员
- 显示成员 IP、主机名、在线状态、HFS 端口
- 自动探测 HFS 端口（默认 8080，备选 8000-8009/8888/9000）
- 支持下拉刷新、增量更新

### 3. HFS 文件浏览
- 点击成员直接进入其 HFS 文件服务
- 支持手动输入 IP 和端口浏览
- 解析 HFS 页面展示文件列表（文件夹/文件、大小、修改时间）
- 支持文件夹导航、返回上级目录

### 4. 分片多线程下载
- 对应 Windows 端 IDM 式下载
- 默认 8 线程分片下载，支持 Range 请求
- 带进度、速度、状态显示
- 支持暂停/继续/取消
- 多任务并发，前台服务通知
- 不支持 Range 的服务器自动降级为单线程下载

### 5. 网络测试
- Ping 测试（4 次，统计成功率/延迟）
- 常用端口扫描（80/8080/8000/8888/9000/22/445/3389）

### 6. 文件共享（受限）
- 当前版本暂不支持安卓端作为 HFS 服务端
- 仅支持浏览和下载 Windows 端共享的文件
- 后续版本计划集成简单 HTTP 文件服务

### 7. UI
- 暗色主题，与 Windows 端风格一致
- 侧栏导航：联机功能 / 在线成员 / 网络测试 / 文件共享 / 文件浏览 / 下载管理 / 使用指南 / 更多

## 环境要求

- JDK 17 或更高
- Android SDK（compileSdk 34，minSdk 24，targetSdk 34）
- Gradle 8.5（通过 Gradle Wrapper 自动下载）

## 构建命令

```bash
# 设置环境变量
export JAVA_HOME=/path/to/jdk17
export ANDROID_HOME=/path/to/android-sdk

# 构建 debug APK
./gradlew assembleDebug

# 产物位置
# app/build/outputs/apk/debug/app-debug.apk
```

Windows 下使用 `gradlew.bat`。

## 项目结构

```
android/
├── app/
│   └── src/main/
│       ├── java/com/ntontier/client/
│       │   ├── NtoNApplication.kt          # Application 类，通知渠道
│       │   ├── vpn/
│       │   │   ├── N2NVpnService.kt        # VPN 服务核心
│       │   │   └── BootReceiver.kt         # 开机自启接收器
│       │   ├── network/
│       │   │   └── NetworkUtil.kt          # 网络工具（Ping/端口/子网计算）
│       │   ├── scan/
│       │   │   └── MemberScanner.kt        # 成员扫描器
│       │   ├── hfs/
│       │   │   └── HfsClient.kt            # HFS 客户端（页面解析）
│       │   ├── download/
│       │   │   ├── DownloadService.kt      # 下载服务
│       │   │   └── SegmentDownloader.kt    # 分片多线程下载器
│       │   ├── ui/
│       │   │   ├── MainActivity.kt         # 主界面（侧栏导航）
│       │   │   ├── connect/                # 联机功能页
│       │   │   ├── members/                # 在线成员页
│       │   │   ├── networktest/            # 网络测试页
│       │   │   ├── share/                  # 文件共享页
│       │   │   ├── browse/                 # 文件浏览页
│       │   │   ├── downloads/              # 下载管理页
│       │   │   ├── guide/                  # 使用指南页
│       │   │   └── more/                   # 更多设置页
│       │   └── util/
│       │       ├── Prefs.kt                # 配置存储
│       │       └── FormatUtil.kt           # 格式化工具
│       ├── res/                            # 资源文件（布局/主题/菜单/图标）
│       └── AndroidManifest.xml
├── build.gradle.kts
├── settings.gradle.kts
├── gradle.properties
└── local.properties                        # SDK 路径（需自行配置）
```

## n2n 二进制集成说明

n2n 是 C 项目，安卓端需交叉编译 edge 二进制：

1. 使用 Android NDK 交叉编译 n2n edge（支持 armeabi-v7a / arm64-v8a）
2. 将编译产物命名为 `libedge.so` 放入 `app/src/main/jniLibs/<abi>/` 目录
3. 应用启动时自动从 `nativeLibraryDir` 加载并执行
4. 命令行参数：`edge -c <社区> -l <supernode> -a <虚拟IP> -s <掩码> -k <密钥> -d ntontier0`

如未集成二进制，VPN Service 框架仍可正常运行（建立 TUN 接口、配置路由），但无法实际建立 n2n 隧道。成员扫描、HFS 浏览、下载等功能依赖 VPN 连通性。

## 权限说明

- `BIND_VPN_SERVICE`：VPN 服务必需
- `INTERNET` / `ACCESS_NETWORK_STATE` / `ACCESS_WIFI_STATE`：网络访问
- `FOREGROUND_SERVICE` / `FOREGROUND_SERVICE_SPECIAL_USE`：前台服务保活
- `WAKE_LOCK`：防止下载时休眠
- `RECEIVE_BOOT_COMPLETED`：开机自动连接
- `POST_NOTIFICATIONS`：Android 13+ 通知权限
- `MANAGE_EXTERNAL_STORAGE`：存储访问（下载文件保存）

## 与 Windows 端对应关系

| 功能 | Windows 端 | 安卓端 |
|------|-----------|--------|
| n2n 连接 | n2n edge 进程 | VpnService + edge 二进制 |
| 在线成员 | ARP/Ping 扫描 | Ping 扫描 + HFS 端口探测 |
| 文件浏览 | HFS 页面解析 | HFS 页面解析（Jsoup） |
| 文件下载 | IDM 式多线程 | 分片多线程下载（8线程） |
| 文件共享 | HFS 服务端 | 暂不支持（计划中） |
| 网络测试 | Ping/Tracert | Ping + 端口扫描 |

## 已知限制

1. n2n edge 二进制需自行交叉编译，当前 APK 未内置
2. 安卓端暂不支持作为 HFS 服务端被其他成员浏览
3. 暂停下载时，已建立的网络连接可能需等待超时才会真正停止
4. Ping 扫描在部分安卓设备上可能受 ICMP 限制，可改用端口扫描辅助判断
5. 下载文件默认保存于应用专属目录，需通过系统文件管理器访问
