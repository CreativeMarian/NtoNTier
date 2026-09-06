using System;
using System.IO;

namespace NtoNServerControl
{
    /// <summary>
    /// 服务端控制台配置：UDP 端口 / 管理端口 / 自动守护 / 开机自启 / 主题。
    /// 存于 exe 同目录 server-config.json（JSON 手写序列化，避免额外依赖）。
    /// </summary>
    public class ServerConfig
    {
        public int Port = 3301;
        public int MgmtPort = 0;
        public bool AutoGuard = true;
        public bool AutoStart = false;
        public bool Dark = true;

        public static string ConfigPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server-config.json"); }
        }

        public static ServerConfig Load()
        {
            var c = new ServerConfig();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath, System.Text.Encoding.UTF8);
                    c = Parse(json) ?? c;
                }
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                string json = "{"
                    + "\"Port\":" + Port
                    + ",\"MgmtPort\":" + MgmtPort
                    + ",\"AutoGuard\":" + (AutoGuard ? "true" : "false")
                    + ",\"AutoStart\":" + (AutoStart ? "true" : "false")
                    + ",\"Dark\":" + (Dark ? "true" : "false")
                    + "}";
                File.WriteAllText(ConfigPath, json, new System.Text.UTF8Encoding(false));
            }
            catch { }
        }

        private static ServerConfig Parse(string json)
        {
            var c = new ServerConfig();
            try
            {
                SetInt(c, json, "Port", v => c.Port = v);
                SetInt(c, json, "MgmtPort", v => c.MgmtPort = v);
                SetBool(json, "AutoGuard", v => c.AutoGuard = v);
                SetBool(json, "AutoStart", v => c.AutoStart = v);
                SetBool(json, "Dark", v => c.Dark = v);
            }
            catch { }
            return c;
        }

        private static void SetInt(ServerConfig c, string json, string key, Action<int> set)
        {
            string token = "\"" + key + "\":";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return;
            i += token.Length;
            // 跳过冒号后的空白（兼容 ": 3301" 带空格写法）
            int j = i;
            while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
            int k = j;
            while (k < json.Length && char.IsDigit(json[k])) k++;
            if (k > j) set(int.Parse(json.Substring(j, k - j)));
        }

        private static void SetBool(string json, string key, Action<bool> set)
        {
            string token = "\"" + key + "\":";
            int i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return;
            // 跳过冒号后的空白再判断（兼容 ": true" 带空格写法）
            string rest = json.Substring(i + token.Length).TrimStart();
            set(rest.StartsWith("true", StringComparison.OrdinalIgnoreCase));
        }
    }
}
