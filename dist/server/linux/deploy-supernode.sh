#!/usr/bin/env bash
# ============================================================
# NtoNTier · Linux supernode 一键部署脚本
# 支持：Ubuntu/Debian（deb） 与 CentOS/Rocky/Fedora（rpm）
# 自动完成：下载 n2n 3.1.1 → 安装 → systemd 后台常驻 → 放行防火墙 → 验证
#
# 【国内服务器友好】服务器在国内无法访问 GitHub 时，脚本会自动走多个
#   GitHub 加速镜像下载（ghfast.top / gh-proxy.com / ghproxy.net / gh.llkk.cc），
#   全部失败才尝试直连 GitHub，下载后校验文件完整性再安装。
#
# 用法：   sudo ./deploy-supernode.sh [端口]
# 示例：   sudo ./deploy-supernode.sh          # 默认 UDP 3301
#          sudo ./deploy-supernode.sh 4399     # 用 4399 端口
# ============================================================
set -e

PORT="${1:-3301}"
GH_BASE="https://github.com/ntop/n2n/releases/download/3.1.1"
# 国内可用的 GitHub 加速镜像（按序尝试，命中即停）
MIRRORS=(
  "https://ghfast.top"
  "https://gh-proxy.com"
  "https://ghproxy.net"
  "https://gh.llkk.cc"
)

echo "===== NtoNTier · Linux supernode 一键部署 ====="
echo "UDP 端口 : $PORT"
echo "n2n 版本 : 3.1.1（与 Windows 客户端 edge 完全一致）"
echo ""

# ---------- 1. 检测发行版 ----------
if command -v apt-get >/dev/null 2>&1; then
    DIST="deb"
elif command -v dnf >/dev/null 2>&1 || command -v yum >/dev/null 2>&1; then
    DIST="rpm"
else
    echo "❌ 未识别系统：仅支持 Debian/Ubuntu（deb）与 CentOS/Rocky/Fedora（rpm）"
    echo "   其它系统请参考：从源码编译 n2n 或使用 docker 镜像。"
    exit 1
fi
echo "✅ 系统识别: $([ "$DIST" = "deb" ] && echo 'Debian/Ubuntu (deb)' || echo 'CentOS/Rocky/Fedora (rpm)')"

# ---------- 2. 检测架构 ----------
ARCH="$(uname -m)"
case "$ARCH" in
    x86_64)     PKG_ARCH="amd64" ;;
    aarch64)    PKG_ARCH="arm64" ;;
    armv7l|armv6l) PKG_ARCH="armhf" ;;
    i386|i686)  PKG_ARCH="i386" ;;
    *) echo "❌ 不支持的架构: $ARCH"; exit 1 ;;
esac
if [ "$DIST" = "rpm" ] && [ "$ARCH" != "x86_64" ]; then
    echo "❌ rpm 官方包仅提供 x86_64；$ARCH 请换用 Debian 系（.deb）或源码编译。"
    exit 1
fi
echo "✅ 架构识别: $ARCH"

# ---------- 3. 准备下载工具 ----------
if ! command -v wget >/dev/null 2>&1 && ! command -v curl >/dev/null 2>&1; then
    echo "安装下载工具 wget ..."
    if [ "$DIST" = "deb" ]; then
        sudo apt-get update -y && sudo apt-get install -y wget
    else
        sudo dnf install -y wget 2>/dev/null || sudo yum install -y wget
    fi
fi

# ---------- 4. 下载并安装 n2n 3.1.1 ----------
download() { # $1=URL $2=输出文件；成功返回 0
    if command -v wget >/dev/null 2>&1; then
        wget -q --timeout=30 -O "$2" "$1" && [ -s "$2" ]
    else
        curl -fsSL --connect-timeout 15 --max-time 180 -o "$2" "$1"
    fi
}
check_pkg() { # $1=文件；校验是合法的 deb / rpm
    local magic
    magic=$(head -c 8 "$1" 2>/dev/null | od -An -tx1 | tr -d ' \n')
    case "$magic" in
        213c617263683e0a*) return 0 ;; # deb 魔数 !<arch>\n
        edabeedb*)          return 0 ;; # rpm 魔数
        *)                  return 1 ;;
    esac
}

if command -v supernode >/dev/null 2>&1; then
    echo "✅ 已检测到 supernode（跳过安装，直接配置）"
else
    if [ "$DIST" = "deb" ]; then
        FILE="n2n_3.1.1_${PKG_ARCH}.deb"
    else
        FILE="n2n-3.1.1-1.x86_64.rpm"
    fi
    GH_URL="$GH_BASE/$FILE"

    DL_OK=0
    echo "下载 $FILE ..."
    for M in "${MIRRORS[@]}"; do
        echo "  尝试镜像: $M ..."
        if download "$M/$GH_URL" "/tmp/$FILE" && check_pkg "/tmp/$FILE"; then
            echo "  ✅ 镜像下载成功: $M"
            DL_OK=1
            break
        fi
    done
    if [ "$DL_OK" -ne 1 ]; then
        echo "  镜像均失败，尝试直连 GitHub ..."
        if ! (download "$GH_URL" "/tmp/$FILE" && check_pkg "/tmp/$FILE"); then
            echo "❌ 下载失败：GitHub 与加速镜像均不可达。"
            echo "   请检查服务器网络，或手动下载安装后重跑本脚本（会自动跳过安装）："
            echo "   wget $GH_URL"
            echo "   sudo dpkg -i $FILE"
            exit 1
        fi
        echo "  ✅ GitHub 直连下载成功"
    fi

    echo "安装中 ..."
    if [ "$DIST" = "deb" ]; then
        sudo dpkg -i "/tmp/$FILE" || { echo "尝试修复依赖..."; sudo apt-get -f install -y; sudo dpkg -i "/tmp/$FILE"; }
    else
        sudo dnf install -y "/tmp/$FILE" 2>/dev/null || sudo yum install -y "/tmp/$FILE"
    fi
    rm -f "/tmp/$FILE"
fi

# ---------- 5. 确认 supernode 可用 ----------
SUPERNODE="$(command -v supernode || echo /usr/sbin/supernode)"
if [ ! -x "$SUPERNODE" ]; then
    SUPERNODE="$(find /usr -name supernode -type f 2>/dev/null | head -n 1)"
fi
if [ -z "$SUPERNODE" ]; then
    echo "❌ 未找到 supernode 可执行文件，请检查安装。"
    exit 1
fi
echo "✅ supernode 路径: $SUPERNODE"
"$SUPERNODE" -h 2>&1 | head -n 1

# ---------- 6. 创建 systemd 服务（后台常驻 + 开机自启） ----------
# 注意：rpm/deb 版 supernode 默认以 daemon(守护) 模式运行，会 fork 到后台、
# 父进程立即 exit(0)，导致 systemd 误判服务失败而无限重启。
# 因此必须加 -f 强制前台运行，交给 systemd 直接管理（务必保留 -f）。
echo "创建 systemd 服务 n2n-supernode ..."
sudo tee /etc/systemd/system/n2n-supernode.service > /dev/null <<EOF
[Unit]
Description=n2n Supernode (NtoNTier)
After=network.target

[Service]
ExecStart=$SUPERNODE -p $PORT -f
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
EOF
sudo systemctl daemon-reload
sudo systemctl enable n2n-supernode
sudo systemctl restart n2n-supernode

# ---------- 7. 放行防火墙 ----------
echo "放行 UDP $PORT ..."
if command -v ufw >/dev/null 2>&1; then
    sudo ufw allow "${PORT}/udp" >/dev/null 2>&1 && echo "  ✅ ufw 已放行"
fi
if command -v firewall-cmd >/dev/null 2>&1; then
    sudo firewall-cmd --permanent --add-port="${PORT}/udp" >/dev/null 2>&1
    sudo firewall-cmd --reload >/dev/null 2>&1 && echo "  ✅ firewalld 已放行"
fi

# ---------- 8. 验证 ----------
echo ""
echo "===== 验证 ====="
sleep 2
sudo systemctl status n2n-supernode --no-pager | head -n 6 || true
if sudo ss -lunp 2>/dev/null | grep -q ":$PORT " || sudo netstat -lunp 2>/dev/null | grep -q ":$PORT "; then
    echo ""
    echo "✅ 部署成功！UDP $PORT 正在监听。"
    echo "   客户端「选择服务器 → 自定义服务器」填入：  你的服务器IP:$PORT"
    echo "   提示：若公网连不上，请到云厂商安全组放行 UDP $PORT。"
else
    echo ""
    echo "⚠️ 未检测到端口监听，请查看日志：sudo journalctl -u n2n-supernode -n 50"
fi
echo "============================================================"
