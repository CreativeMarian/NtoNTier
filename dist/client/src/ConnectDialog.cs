using System;
using System.Drawing;
using System.Windows.Forms;

namespace NtoNTier
{
    /// <summary>联机配置弹窗（极简扁平风格：分区标题 + 圆角输入行 + 复选框 + 底部操作条）</summary>
    public class ConnectDialog : ChromeForm
    {
        private AppConfig _cfg;
        private RoundedTextBox _txtCommunity, _txtKey, _txtPort, _txtTap, _txtMgmt, _txtExtra;
        private RoundedTextBox _txtLocalPort;
        private ThemedCheckBox _chkAutoIP, _chkFixedUDP;
        private Panel _localRow;

        public ConnectDialog(AppConfig cfg)
        {
            _cfg = cfg;
            Text = "联机配置";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 680);
            MinimumSize = new Size(500, 600);
            Build();
            SetupChrome("联机配置", false);
            LoadValues();
        }

        private void Build()
        {
            // 注意添加顺序：scroll(Fill) 先、bottom(Bottom) 后 —— 停靠时 bottom 先占底部，
            // scroll 填充剩余，避免内容区覆盖底部操作条
            var scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            Controls.Add(scroll);

            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 74;
            Controls.Add(bottom);

            var btnSave = new GButton();
            btnSave.Text = "保存配置";
            btnSave.Size = new Size(128, 42);
            btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSave.Location = new Point(bottom.Width - 148, 16);
            btnSave.Click += (s, e) => { Save(); DialogResult = DialogResult.OK; Close(); };
            bottom.Controls.Add(btnSave);

            var btnCancel = new FlatButton();
            btnCancel.Text = "取消";
            btnCancel.Kind = FlatButton.BtnKind.Ghost;
            btnCancel.Size = new Size(96, 42);
            btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCancel.Location = new Point(bottom.Width - 256, 16);
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            bottom.Controls.Add(btnCancel);

            var btnReset = new FlatButton();
            btnReset.Text = "恢复默认";
            btnReset.Kind = FlatButton.BtnKind.Ghost;
            btnReset.Size = new Size(110, 42);
            btnReset.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnReset.Location = new Point(bottom.Width - 378, 16);
            btnReset.Click += (s, e) => ResetDefaults();
            bottom.Controls.Add(btnReset);

            int y = 14;
            y = Section(scroll, y, "连接参数");
            y = Field(scroll, y, "房间 / 社区", out _txtCommunity);
            y = Field(scroll, y, "密钥（可选）", out _txtKey);
            y = Field(scroll, y, "服务器端口", out _txtPort);
            y = Field(scroll, y, "虚拟网卡名称", out _txtTap);

            y += 6;
            y = Section(scroll, y, "高级选项");
            _chkAutoIP = new ThemedCheckBox();
            _chkAutoIP.Text = "自动获取虚拟 IP（由服务器分配）";
            _chkAutoIP.Location = new Point(14, y);
            _chkAutoIP.Width = Math.Max(300, scroll.Width - 28);
            scroll.Controls.Add(_chkAutoIP);
            y += 38;
            _chkFixedUDP = new ThemedCheckBox();
            _chkFixedUDP.Text = "固定本机 UDP 端口（利于打洞稳定，多开时需不同）";
            _chkFixedUDP.Location = new Point(14, y);
            _chkFixedUDP.Width = Math.Max(300, scroll.Width - 28);
            _chkFixedUDP.CheckedChanged += (s, e) => _localRow.Visible = _chkFixedUDP.Checked;
            scroll.Controls.Add(_chkFixedUDP);
            y += 38;

            _localRow = new Panel();
            _localRow.Location = new Point(14, y);
            _localRow.Width = Math.Max(300, scroll.Width - 28);
            _localRow.Height = 40;
            scroll.Controls.Add(_localRow);
            var lL = new Label();
            lL.Text = "本机端口";
            lL.Font = new Font("Microsoft YaHei UI", 9f);
            lL.ForeColor = Theme.Current.TextDim;
            lL.Location = new Point(2, 9);
            lL.AutoSize = true;
            _localRow.Controls.Add(lL);
            _txtLocalPort = new RoundedTextBox();
            _txtLocalPort.Location = new Point(110, 0);
            _txtLocalPort.Size = new Size(120, 34);
            _localRow.Controls.Add(_txtLocalPort);
            y += 50;

            y = Field(scroll, y, "管理端口", out _txtMgmt);
            y = Field(scroll, y, "额外参数", out _txtExtra);

            y += 6;
            y = Section(scroll, y, "系统工具");
            var btnInstall = new FlatButton();
            btnInstall.Text = "安装 TAP 驱动";
            btnInstall.Kind = FlatButton.BtnKind.Soft;
            btnInstall.Location = new Point(14, y);
            btnInstall.Size = new Size(150, 40);
            btnInstall.Click += (s, e) => MessageBox.Show(this, Net.InstallTap(), "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            scroll.Controls.Add(btnInstall);

            var btnUninstall = new FlatButton();
            btnUninstall.Text = "卸载 TAP 驱动";
            btnUninstall.Kind = FlatButton.BtnKind.Soft;
            btnUninstall.Location = new Point(176, y);
            btnUninstall.Size = new Size(150, 40);
            btnUninstall.Click += (s, e) => MessageBox.Show(this, Net.UninstallTap(), "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            scroll.Controls.Add(btnUninstall);

            var btnFirewall = new FlatButton();
            btnFirewall.Text = "配置防火墙";
            btnFirewall.Kind = FlatButton.BtnKind.Soft;
            btnFirewall.Location = new Point(338, y);
            btnFirewall.Size = new Size(150, 40);
            btnFirewall.Click += (s, e) => MessageBox.Show(this, Net.SetFirewall(_cfg.SupernodePort, _cfg.LocalPort), "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            scroll.Controls.Add(btnFirewall);
            y += 60;
        }

        private int Section(Panel scroll, int y, string text)
        {
            var l = new Label();
            l.Text = text;
            l.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            l.ForeColor = Theme.Current.Accent1;
            l.Location = new Point(12, y);
            l.AutoSize = true;
            scroll.Controls.Add(l);
            return y + 30;
        }

        private int Field(Panel scroll, int y, string label, out RoundedTextBox box)
        {
            var l = new Label();
            l.Text = label;
            l.Font = new Font("Microsoft YaHei UI", 9f);
            l.ForeColor = Theme.Current.TextDim;
            l.Location = new Point(14, y + 9);
            l.AutoSize = true;
            scroll.Controls.Add(l);

            box = new RoundedTextBox();
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.Location = new Point(130, y);
            box.Width = Math.Max(220, scroll.Width - 148);
            box.Height = 36;
            scroll.Controls.Add(box);
            return y + 50;
        }

        private void LoadValues()
        {
            _txtCommunity.Text = _cfg.Community;
            _txtKey.Text = _cfg.Key;
            _txtPort.Text = _cfg.SupernodePort.ToString();
            _txtTap.Text = _cfg.TapName;
            _chkAutoIP.Checked = _cfg.AutoIP;
            _chkFixedUDP.Checked = _cfg.LocalPort > 0;
            _txtLocalPort.Text = _cfg.LocalPort > 0 ? _cfg.LocalPort.ToString() : "";
            _txtMgmt.Text = _cfg.MgmtPort.ToString();
            _txtExtra.Text = _cfg.ExtraArgs;
            _localRow.Visible = _cfg.LocalPort > 0;
        }

        private void Save()
        {
            _cfg.Community = _txtCommunity.Text.Trim();
            _cfg.Key = _txtKey.Text.Trim();
            int port;
            if (int.TryParse(_txtPort.Text.Trim(), out port) && port > 0 && port < 65536) _cfg.SupernodePort = port;
            if (!string.IsNullOrEmpty(_txtTap.Text.Trim())) _cfg.TapName = _txtTap.Text.Trim();
            _cfg.AutoIP = _chkAutoIP.Checked;
            if (_chkFixedUDP.Checked)
            {
                int lp;
                if (int.TryParse(_txtLocalPort.Text.Trim(), out lp) && lp > 0 && lp < 65536) _cfg.LocalPort = lp;
            }
            else _cfg.LocalPort = 0;
            int mp;
            if (int.TryParse(_txtMgmt.Text.Trim(), out mp) && mp > 0 && mp < 65536) _cfg.MgmtPort = mp;
            _cfg.ExtraArgs = _txtExtra.Text.Trim();
        }

        private void ResetDefaults()
        {
            var d = new AppConfig();
            _txtCommunity.Text = d.Community;
            _txtKey.Text = d.Key;
            _txtPort.Text = d.SupernodePort.ToString();
            _txtTap.Text = d.TapName;
            _chkAutoIP.Checked = d.AutoIP;
            _chkFixedUDP.Checked = false;
            _txtLocalPort.Text = "";
            _txtMgmt.Text = d.MgmtPort.ToString();
            _txtExtra.Text = d.ExtraArgs;
            _localRow.Visible = false;
        }

        /// <summary>MainForm 调用（本弹窗直接修改传入的 cfg，无需再复制）</summary>
        public void ApplyTo(AppConfig cfg) { }
    }
}
