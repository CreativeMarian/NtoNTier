# NtoNTier 安卓端代码审查报告

## 审查概述

对 `android/` 目录下全部源码进行了多轮代码审查，覆盖功能完整性、崩溃风险、权限处理、内存泄漏、错误处理、UI 响应性、n2n 连接稳定性、下载并发安全等维度。共进行 2 轮审查，发现并修复 12 项问题，最终构建通过且无编译警告。

---

## 第一轮审查

### 发现的问题

#### 1. 非公开 Drawable 资源引用（阻断性）
- **文件**: `res/layout/nav_header.xml`, `vpn/N2NVpnService.kt`
- **问题**: 使用了 `@android:drawable/stat_sys_vpnspeech`，该资源不是 Android 公开资源，导致资源链接失败
- **修复**: 替换为 `@android:drawable/ic_menu_compass`
- **状态**: 已修复

#### 2. Suspend 函数在非协程上下文调用（阻断性）
- **文件**: `ui/browse/FileBrowseActivity.kt:89`, `ui/browse/FileBrowseFragment.kt:92`
- **问题**: `HfsClient.listFiles()` 是 suspend 函数，但在 `Thread { }` 中调用，编译错误
- **修复**: 改用 `lifecycleScope.launch` / `viewLifecycleOwner.lifecycleScope.launch`，移除 Handler 和 Thread
- **状态**: 已修复

#### 3. 接口定义位置导致引用失败（阻断性）
- **文件**: `vpn/N2NVpnService.kt`, `ui/connect/ConnectFragment.kt`
- **问题**: `ConnectionListener` 接口定义在 `companion object` 内部，外部引用为 `N2NVpnService.Companion.ConnectionListener`，与 `N2NVpnService.ConnectionListener` 不匹配
- **修复**: 将接口移至类内部、companion object 外部，作为嵌套接口
- **状态**: 已修复

#### 4. HFS 文件排序语法错误（阻断性）
- **文件**: `ui/browse/HfsFileAdapter.kt:45`
- **问题**: `files.sortedBy { !it.isDirectory }.thenBy { ... }` 中 `thenBy` 是 Comparator 的扩展函数，不能直接链式调用在 List 上
- **修复**: 改为 `files.sortedWith(compareBy({ !it.isDirectory }, { it.name.lowercase() }))`
- **状态**: 已修复

#### 5. 缺少 View 导入（阻断性）
- **文件**: `ui/browse/HfsFileAdapter.kt:36`
- **问题**: 使用了 `View.GONE` / `View.VISIBLE` 但未导入 `android.view.View`
- **修复**: 添加导入
- **状态**: 已修复

#### 6. 反射调用检查 VPN 状态（代码质量）
- **文件**: `ui/members/MembersFragment.kt:54-62`
- **问题**: 使用 `Class.forName().getField().getBoolean()` 反射检查 `N2NVpnService.isRunning`，不必要且脆弱
- **修复**: 直接导入 `N2NVpnService` 并访问 `isRunning` 属性
- **状态**: 已修复

#### 7. URL 解析逻辑缺陷（功能缺陷）
- **文件**: `hfs/HfsClient.kt:98-105`
- **问题**: `resolveUrl` 使用 `.replace("//", "/")` 会破坏 `http://` 协议前缀，虽然后续 `.replace(":/", "://")` 尝试修复但逻辑脆弱
- **修复**: 改用 `java.net.URI.resolve()` 进行标准 URL 解析，带异常回退
- **状态**: 已修复

#### 8. 协程中使用 Thread.sleep（性能/响应性）
- **文件**: `vpn/N2NVpnService.kt:177`
- **问题**: 在 `serviceScope.launch` 协程中调用 `Thread.sleep(1500)`，阻塞 IO 线程
- **修复**: 改为 `delay(1500)`，并将 `startN2nEdge()` 标记为 suspend 函数
- **状态**: 已修复

#### 9. 下载暂停/取消不停止实际下载（功能缺陷）
- **文件**: `download/DownloadService.kt`
- **问题**: `pauseTask` 仅修改状态，不取消正在运行的协程；`resumeTask` 可能启动重复下载；`cancelTask` 不终止运行中的下载
- **修复**: 新增 `activeJobs: ConcurrentHashMap<String, Job>` 跟踪活跃下载协程，暂停/取消时调用 `job.cancel()`，恢复时检查状态避免重复
- **状态**: 已修复

#### 10. 监听器重复注册（内存泄漏风险）
- **文件**: `ui/members/MembersFragment.kt`
- **问题**: `onViewCreated` 和 `onResume` 都调用 `scanner.addListener(this)`，可能重复注册
- **修复**: 移除 `onViewCreated` 中的注册，仅在 `onResume` 注册，`onPause` 注销
- **状态**: 已修复

#### 11. Handler 回调未清理（内存泄漏风险）
- **文件**: `ui/connect/ConnectFragment.kt`, `ui/members/MembersFragment.kt`
- **问题**: `Handler` 在 `onDestroyView` 中未调用 `removeCallbacksAndMessages(null)`，可能导致延迟回调持有已销毁的 View
- **修复**: 在 `onDestroyView` 中添加 `handler.removeCallbacksAndMessages(null)`
- **状态**: 已修复

#### 12. 异步回调中访问已销毁 View（崩溃风险）
- **文件**: `ui/members/MembersFragment.kt`
- **问题**: 扫描回调通过 `handler.post` 更新 UI，但 Fragment 可能已销毁，`_binding` 为 null 时访问 `binding` 会崩溃
- **修复**: 所有 handler.post 回调中添加 `if (_binding != null)` 检查
- **状态**: 已修复

---

## 第二轮审查

对第一轮修复后的代码进行复审，并检查是否引入新问题。

### 发现的问题

#### 13. 未使用变量（代码清洁度）
- **文件**: `hfs/HfsClient.kt:62`
- **问题**: `val rows = doc.select(...)` 变量定义后从未使用
- **修复**: 删除未使用的变量
- **状态**: 已修复

#### 14. 未使用参数（代码清洁度）
- **文件**: `vpn/N2NVpnService.kt:226`
- **问题**: `handleOutgoingPacket(buffer, output)` 中 `output` 参数从未使用
- **修复**: 移除 `output` 参数及相关的 `FileOutputStream` 变量
- **状态**: 已修复

### 复审确认项

以下方面经复审确认无问题：

- **权限处理**: VPN 权限通过 `VpnService.prepare()` 标准流程申请；前台服务类型 `specialUse` 已配置；存储权限已声明
- **生命周期管理**: 所有 Fragment 在 `onDestroyView` 中清理 binding、handler、监听器；Service 在 `onDestroy` 中取消协程作用域
- **线程安全**: 下载任务使用 `ConcurrentHashMap`；进度更新通过 `AtomicLong` + `synchronized`；UI 更新均在主线程
- **错误处理**: 网络请求均有 try-catch；HFS 解析有回退逻辑；下载有重试机制（最多5次，指数退避）
- **UI 响应性**: 所有网络/IO 操作均在后台线程/协程；主线程不做耗时操作
- **n2n 稳定性**: VPN Service 使用 `START_STICKY`；`onRevoke` 正确处理；连接失败有清理逻辑
- **下载并发安全**: 多线程写入不同文件偏移量，使用 `RandomAccessFile` 线程安全；进度统计原子化

---

## 最终结论

- **构建状态**: ✅ BUILD SUCCESSFUL，无编译错误，无编译警告
- **功能覆盖**: ✅ VPN 框架 / 成员扫描 / HFS 浏览 / 分片下载 / 网络测试 / 侧栏导航 全部实现
- **阻断性问题**: ✅ 全部修复
- **已知限制**: n2n edge 二进制需自行交叉编译（README 已说明）；安卓端 HFS 服务端暂不支持
- **交付状态**: 可安装运行的 debug APK 已生成，源码结构完整

**审查通过，可交付。**
