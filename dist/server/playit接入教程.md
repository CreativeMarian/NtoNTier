# NtoNTier · playit.gg 中继接入教程（家庭宽带 / 无公网IP 专用）

> **为什么需要它**：家里宽带通常没有公网 IP（运营商是 CGNAT），朋友从外面连不进你的
> supernode。playit.gg 是一个免费隧道服务，把你的 supernode 端口"映射"到公网，
> 朋友填它给的地址就能连进来。**不需要公网IP、不需要路由器端口转发。**

> 说明：Cloudflare 免费隧道不支持 UDP（NtoNTier 走的是 UDP），所以选用 playit.gg。

---

## 第一步：注册并下载

1. 打开 https://playit.gg 注册一个免费账号（邮箱即可）。
2. 下载 Windows 版客户端（agent）：https://playit.gg/download
3. 得到一个 `playit.exe`（约几 MB）。

## 第二步：登录绑定

1. 双击运行 `playit.exe`，第一次会打开一个本地页面（或显示 6 位数字的验证码）。
2. 按提示打开 https://playit.gg/claim 输入验证码，完成绑定。
3. 之后 playit.exe 保持运行即可（可以设置开机自启）。

## 第三步：启动你的 supernode

在 `server` 文件夹双击 **启动服务端.bat**，确认显示"supernode 已启动"。
（默认端口 3301，NtoNTier 客户端默认连 3301，不用改。）

再双击 **放行防火墙.bat**。

## 第四步：在 playit.gg 后台创建隧道

1. 打开 https://playit.gg/account/tunnels
2. 点 **New Tunnel（新建隧道）**：
   - **类型选 UDP**（NtoNTier 打洞/中继全是 UDP）
   - **本地地址填 `127.0.0.1`，本地端口填 `3301`**
   - 提交后会分配一个公网地址，形如：`xxxxxx-udp.at.ply.gg:16500`
3. 免费版通常可以创建多个 UDP 隧道（免费额度以官网当前说明为准，一般 4 个足够）。

## 第五步：让朋友填这个地址

1. 把 `client` 文件夹发给朋友。
2. 朋友打开 NtoNTier，在 **① 选择服务器 → 自定义服务器** 里填：
   `xxxxxx-udp.at.ply.gg:16500`（就是 playit 分配的那一串，端口带上）。
3. 网段填一样、后缀不重复，点 **连接**。

你（房主）自己这边也一样：填同一个 playit 地址连接即可（本机直连也行，但统一填 playit 地址最稳）。

---

## 验证是否通

- 双方都连上后，在 **网络测试** 里 Ping 对方的虚拟 IP（如 10.10.30.101）。
- Ping 通 = 虚拟局域网建立成功。
- 之后进游戏选"局域网联机"即可。

## 常见问题

**Q: playit.exe 要不要一直开着？**
A: 要。playit.exe 和 supernode 都要保持运行，隧道才有效。建议都设置开机自启。

**Q: 朋友连不上？**
A: 检查：① playit.exe 是否在运行；② 隧道本地端口是不是 3301；
   ③ 地址是否完整（含冒号端口）；④ 防火墙是否放行。

**Q: playit 免费够用吗？**
A: 对 NtoNTier 来说，每个 supernode 只占 1 个 UDP 隧道，免费额度足够个人/小团队使用。
