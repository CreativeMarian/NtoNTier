# NtoNTier · Linux 服务器部署说明

> 你的情况：**有一台 Linux 服务器**，用它跑 n2n 的 **supernode**（中继/打洞协调服务），
> 朋友通过它互相找到对方、打洞直连。**完全免费，不依赖 playit/Cloudflare。**
>
> 服务器要求：Ubuntu/Debian 或 CentOS/Rocky/Fedora 任一即可；内存 512MB 都绰绰有余。

---

## 一、一句话总结

把 `linux/deploy-supernode.sh` 上传到服务器，执行：

```bash
chmod +x deploy-supernode.sh
sudo ./deploy-supernode.sh
```

跑完就部署好了，服务已后台常驻 + 开机自启 + 防火墙已放行。
客户端里填 **`你的服务器公网IP:3301`** 就能连。

---

## 二、具体步骤（小白版）

> **国内服务器不用怕**：脚本内置了 4 个国内 GitHub 加速镜像
> （ghfast.top / gh-proxy.com / ghproxy.net / gh.llkk.cc），下载会自动走镜像，
> 全部失败才尝试直连 GitHub，下载后还会校验文件完整性，不会装到坏文件。

### 第 1 步：拿到服务器信息
登录你的云服务器控制台，记下 **公网 IP**（例如 `103.xx.xx.xx`）。
系统是 Ubuntu/Debian 还是 CentOS/Rocky 无所谓，脚本自动识别。

### 第 2 步：上传脚本
在你自己的电脑上，用终端（PowerShell / CMD）执行：

```bash
scp deploy-supernode.sh root@你的服务器IP:/root/
```

或用宝塔 / SFTP / 网页控制台的"文件管理"直接上传，放在 `/root/` 下即可。

### 第 3 步：运行脚本
SSH 登录服务器后：

```bash
cd /root
chmod +x deploy-supernode.sh
sudo ./deploy-supernode.sh
```

看到 **`✅ 部署成功！UDP 3301 正在监听`** 就完成了。

> 想换端口（比如 4399）就：`sudo ./deploy-supernode.sh 4399`

### 第 4 步：云厂商安全组放行（关键！）
很多人部署成功但外面连不上，就是漏了这步：
去云服务器控制台 → **安全组 / 防火墙规则** → 添加入站规则：
- 协议：**UDP**
- 端口：**3301**
- 来源：**0.0.0.0/0**（所有 IP）

> 系统内部防火墙脚本已经放行，**安全组是云厂商那层**，必须自己在网页控制台加。

### 第 5 步：客户端填写
你和朋友都打开 NtoNTier.exe：
1. **① 选择服务器** → 切到 **自定义服务器**
2. 填 **`你的服务器公网IP:3301`**（例如 `103.xx.xx.xx:3301`）
3. **② 配置虚拟 IP**：网段所有人填一样（如 `10`），后缀每人不同（`100` / `101`）
4. 点 **【连接】** → 顶部变绿色即成功

### 第 6 步：验证
- 各端 **在线成员** 页能看到对方虚拟 IP
- **网络测试** 页 Ping 对方虚拟 IP，能通就说明虚拟局域网建立成功

---

## 三、日常管理命令（SSH 里执行）

| 想做的事 | 命令 |
|---|---|
| 查看服务状态 | `systemctl status n2n-supernode` |
| 重启服务 | `sudo systemctl restart n2n-supernode` |
| 停止服务 | `sudo systemctl stop n2n-supernode` |
| 查看日志 | `journalctl -u n2n-supernode -n 100` |
| 卸载服务 | `sudo systemctl disable --now n2n-supernode` 后删除 `/etc/systemd/system/n2n-supernode.service` |

---

## 四、常见问题

**Q: 提示"未识别系统"？**
A: 脚本仅支持 Debian/Ubuntu 与 CentOS/Rocky/Fedora。其它系统请告诉我们，换用源码编译。

**Q: 部署成功但客户端连不上？**
A: 99% 是**云厂商安全组**没放行 UDP 3301（见第 4 步）。其次确认客户端填的是**公网 IP 而非内网 IP**。

**Q: 需要多大的服务器？**
A: supernode 只是"介绍人"，数据主要走两端直连，资源占用极低。1 核 512MB 完全够，甚至家庭旧电脑/树莓派都能跑。

**Q: 服务器在境外，客户端打洞慢？**
A: supernode 只负责交换地址，打洞后数据**不经过服务器**（P2P 直连），延迟取决于两端网络。打洞失败时才走服务器中转。

**Q: 服务状态一直显示 `activating (auto-restart)` / 端口没监听？**
A: 这是 rpm/deb 版 supernode 的**守护模式问题**：它默认会 fork 到后台，父进程立即退出，导致 systemd 误判服务失败而无限重启。**用新版脚本重跑一次即可**（脚本已在启动命令里加了 `-f` 强制前台运行，交给 systemd 管理）。若想手动修，把服务文件里的 `ExecStart` 行改成：
```bash
ExecStart=/usr/sbin/supernode -p 3301 -f
```
然后 `sudo systemctl daemon-reload && sudo systemctl restart n2n-supernode`

**Q: 脚本下载 GitHub 慢/失败？**
A: 脚本已内置国内加速镜像并自动轮流尝试，一般能直接成功。若镜像也全失败，可手动下载后重跑脚本（脚本检测到已安装会跳过下载）：
```bash
# 手动下载 deb 包再运行脚本
wget https://ghfast.top/https://github.com/ntop/n2n/releases/download/3.1.1/n2n_3.1.1_amd64.deb
sudo dpkg -i n2n_3.1.1_amd64.deb
```

---

## 五、为什么选这个方案

| 方案 | 费用 | UDP 支持 | 说明 |
|---|---|---|---|
| **自建 Linux supernode** | 免费 | ✅ | 你现在这台服务器，最干净 |
| Cloudflare 免费隧道 | 免费 | ❌ | 只支持 TCP/HTTP，游戏联机不可用 |
| CF Spectrum | $200+/月 | ✅ | 太贵 |
| playit.gg | 免费/高级 $3 | ✅ | 有免费版可用，但自定义域名等要付费，且流量经第三方 |

> 结论：既然你有服务器，直接用 **Linux 自建 supernode** 是最优解，零额外费用、数据可控。
