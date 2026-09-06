using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace NtoNTier
{
    /// <summary>客户端配置（自动保存到 exe 同目录 config.json）</summary>
    [DataContract]
    public class AppConfig
    {
        [DataMember] public bool Dark = true;
        [DataMember] public bool Onboarded = false;   // 是否已完成首次引导
        [DataMember] public string Community = "tudou";
        [DataMember] public string Key = "";
        [DataMember] public int SupernodePort = 3301;
        [DataMember] public string TapName = "n2n_tap";
        [DataMember] public bool AutoIP = false;
        [DataMember] public string ExtraArgs = "";
        [DataMember] public int RegInterval = 20;
        [DataMember] public int LocalPort = 0;        // 0 = 随机（edge 默认）
        [DataMember] public int MgmtPort = 5644;
        [DataMember] public string NetworkPrefix = "10.10"; // 固定 IP 段
        [DataMember] public string Netmask = "24";
        // 官方服务器列表（<名称>=<host:port>，多行）
        // 注意：n2n v3 应使用 10090 端口；10088 是 v2s 端口，v3 edge 连入会协议不兼容
        [DataMember] public string OfficialServers = "北京节点=bj.n2n.aobacore.com:9555\n上海节点=sh.n2n.aobacore.com:9555\n广州节点=gz.n2n.aobacore.com:9555\n成都节点=cd.n2n.aobacore.com:9555\n融合节点=n2n.aobacore.com:9555\n美国节点=supernode2.umrcluster.top:12138";
        // 最近使用的自定义服务器
        [DataMember] public string CustomServer = "";
        // 常用网段/后缀记忆
        [DataMember] public string Segment = "30";
        [DataMember] public string Suffix = "100";
        [DataMember] public int HfsPort = 8080;
        [DataMember] public string HfsShareDir = "";
        [DataMember] public string HfsPassword = "";
        [DataMember] public int DownloadThreads = 16;
        [DataMember] public int TransferPort = 8081;
        [DataMember] public string DownloadDir = "";

        private static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"); }
        }

        public void Save()
        {
            try
            {
                var ser = new DataContractJsonSerializer(typeof(AppConfig));
                using (var ms = new MemoryStream())
                {
                    ser.WriteObject(ms, this);
                    File.WriteAllText(ConfigPath, Encoding.UTF8.GetString(ms.ToArray()), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                // 保存失败时写入同目录 error.log，便于排查（不再静默吞掉）
                try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config-error.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + ex.Message + Environment.NewLine); } catch { }
            }
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new AppConfig();
                var ser = new DataContractJsonSerializer(typeof(AppConfig));
                using (var ms = new MemoryStream(File.ReadAllBytes(ConfigPath)))
                {
                    return (AppConfig)ser.ReadObject(ms);
                }
            }
            catch
            {
                // 配置损坏时回默认值，不崩溃
                return new AppConfig();
            }
        }
    }
}
