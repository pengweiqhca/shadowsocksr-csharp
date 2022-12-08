using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.IO;
using Shadowsocks.Properties;
using SimpleJson;
using Shadowsocks.Util;
using Shadowsocks.Model;

namespace Shadowsocks.Controller
{
    public class GFWListUpdater
    {
        private const string GFWLIST_URL = "https://raw.githubusercontent.com/gfwlist/gfwlist/master/gfwlist.txt";

        private const string GFWLIST_BACKUP_URL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/gfwlist.txt";

        private const string GFWLIST_TEMPLATE_URL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/ss_gfw.pac";

        private static string PAC_FILE = PACServer.PAC_FILE;

        private static string USER_RULE_FILE = PACServer.USER_RULE_FILE;

        private static string USER_ABP_FILE = PACServer.USER_ABP_FILE;

        private static string gfwlist_template = null;

        private Configuration lastConfig;

        public int update_type;

        public event EventHandler<ResultEventArgs> UpdateCompleted;

        public event ErrorEventHandler Error;

        public class ResultEventArgs : EventArgs
        {
            public bool Success;

            public ResultEventArgs(bool success)
            {
                this.Success = success;
            }
        }

        private async Task http_DownloadGFWTemplateCompleted(Task<string> task)
        {
            try
            {
                var result = await task;

                if (result.IndexOf("__RULES__") > 0 && result.IndexOf("FindProxyForURL") > 0)
                {
                    gfwlist_template = result;
                    if (lastConfig != null)
                    {
                        await UpdatePACFromGFWList(lastConfig);
                    }
                    lastConfig = null;
                }
                else
                {
                    Error(this, new ErrorEventArgs(new Exception("Download ERROR")));
                }
            }
            catch (Exception ex)
            {
                if (Error != null)
                {
                    Error(this, new ErrorEventArgs(ex));
                }
            }
        }

        private async Task http_DownloadStringCompleted(HttpClient http, Task<string> task, bool isBackupRequest)
        {
            try
            {
                var result = await task;

                List<string> lines = ParseResult(result);
                if (lines.Count == 0)
                {
                    throw new Exception("Empty GFWList");
                }
                if (File.Exists(USER_RULE_FILE))
                {
                    string local = File.ReadAllText(USER_RULE_FILE, Encoding.UTF8);
                    string[] rules = local.Split(new char[]
                    {
                        '\r', '\n'
                    }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string rule in rules)
                    {
                        if (rule.StartsWith("!") || rule.StartsWith("["))
                            continue;
                        lines.Add(rule);
                    }
                }
                string abpContent = gfwlist_template;
                if (File.Exists(USER_ABP_FILE))
                {
                    abpContent = File.ReadAllText(USER_ABP_FILE, Encoding.UTF8);
                }
                else
                {
                    abpContent = gfwlist_template;
                }
                abpContent = abpContent.Replace("__RULES__", SimpleJson.SimpleJson.SerializeObject(lines));
                if (File.Exists(PAC_FILE))
                {
                    string original = File.ReadAllText(PAC_FILE, Encoding.UTF8);
                    if (original == abpContent)
                    {
                        update_type = 0;
                        UpdateCompleted(this, new ResultEventArgs(false));
                        return;
                    }
                }
                File.WriteAllText(PAC_FILE, abpContent, Encoding.UTF8);
                if (UpdateCompleted != null)
                {
                    update_type = 0;
                    UpdateCompleted(this, new ResultEventArgs(true));
                }
            }
            catch (Exception ex)
            {
                if (Error != null)
                {
                    if (!isBackupRequest)
                    {
                        await http_DownloadStringCompleted(http, http.GetStringAsync(GFWLIST_BACKUP_URL + "?rnd=" + Utils.RandUInt32()), true);
                    }
                    else
                    {
                        Error(this, new ErrorEventArgs(ex));
                    }
                }
            }
        }

        private async Task http_DownloadPACCompleted(Task<string> task)
        {
            try
            {
                var content = await task;

                if (File.Exists(PAC_FILE))
                {
                    string original = File.ReadAllText(PAC_FILE, Encoding.UTF8);
                    if (original == content)
                    {
                        update_type = 1;
                        UpdateCompleted(this, new ResultEventArgs(false));
                        return;
                    }
                }
                File.WriteAllText(PAC_FILE, content, Encoding.UTF8);
                if (UpdateCompleted != null)
                {
                    update_type = 1;
                    UpdateCompleted(this, new ResultEventArgs(true));
                }
            }
            catch (Exception ex)
            {
                if (Error != null)
                {
                    Error(this, new ErrorEventArgs(ex));
                }
            }

        }

        public async Task UpdatePACFromGFWList(Configuration config)
        {
            if (gfwlist_template == null)
            {
                lastConfig = config;

                WebProxy proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
                if (!string.IsNullOrEmpty(config.authPass))
                {
                    proxy.Credentials = new NetworkCredential(config.authUser, config.authPass);
                }
                using var http = new HttpClient(new HttpClientHandler
                {
                    Proxy = proxy,
                    UseProxy = true,
                });
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    String.IsNullOrEmpty(config.proxyUserAgent) ?
                    "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                    : config.proxyUserAgent);

                await http_DownloadGFWTemplateCompleted(http.GetStringAsync(GFWLIST_TEMPLATE_URL + "?rnd=" + Util.Utils.RandUInt32()));
            }
            else
            {
                WebProxy proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
                if (!string.IsNullOrEmpty(config.authPass))
                {
                    proxy.Credentials = new NetworkCredential(config.authUser, config.authPass);
                }
                using var http = new HttpClient(new HttpClientHandler
                {
                    Proxy = proxy,
                    UseProxy = true,
                });
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    String.IsNullOrEmpty(config.proxyUserAgent) ?
                    "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                    : config.proxyUserAgent);

                await http_DownloadStringCompleted(http, http.GetStringAsync(GFWLIST_URL + "?rnd=" + Utils.RandUInt32()), false);
            }
        }

        public async void UpdatePACFromGFWList(Configuration config, string url)
        {
            WebProxy proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
            if (!string.IsNullOrEmpty(config.authPass))
            {
                proxy.Credentials = new NetworkCredential(config.authUser, config.authPass);
            }
            using var http = new HttpClient(new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true,
            });
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            String.IsNullOrEmpty(config.proxyUserAgent) ?
                "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                : config.proxyUserAgent);

            await http_DownloadPACCompleted(http.GetStringAsync(url + "?rnd=" + Util.Utils.RandUInt32()));
        }

        public List<string> ParseResult(string response)
        {
            byte[] bytes = Convert.FromBase64String(response);
            string content = Encoding.ASCII.GetString(bytes);
            string[] lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> valid_lines = new List<string>(lines.Length);
            foreach (string line in lines)
            {
                if (line.StartsWith("!") || line.StartsWith("["))
                    continue;
                valid_lines.Add(line);
            }
            return valid_lines;
        }
    }
}