using Shadowsocks.Model;
using Shadowsocks.Util;
using System.Net;
using System.Text;

namespace Shadowsocks.Controller
{
    public class GFWListUpdater
    {
        private const string GFWLIST_URL = "https://raw.githubusercontent.com/gfwlist/gfwlist/master/gfwlist.txt";

        private const string GFWLIST_BACKUP_URL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/gfwlist.txt";

        private const string GFWLIST_TEMPLATE_URL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/ss_gfw.pac";

        private static readonly string PAC_FILE = PACServer.PAC_FILE;

        private static readonly string USER_RULE_FILE = PACServer.USER_RULE_FILE;

        private static readonly string USER_ABP_FILE = PACServer.USER_ABP_FILE;

        private static string gfwlist_template;

        private Configuration lastConfig;

        public int update_type;

        public Action<bool> UpdateCompleted;

        public Action<Exception> Error;

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
                    Error(new Exception("Download ERROR"));
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
            }
        }

        private async Task http_DownloadStringCompleted(HttpClient http, Task<string> task, bool isBackupRequest)
        {
            try
            {
                var result = await task;

                var lines = ParseResult(result);
                if (lines.Count == 0)
                {
                    throw new Exception("Empty GFWList");
                }
                if (File.Exists(USER_RULE_FILE))
                {
                    var local = await File.ReadAllTextAsync(USER_RULE_FILE, Encoding.UTF8);
                    var rules = local.Split(new[]
                    {
                        '\r', '\n'
                    }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var rule in rules)
                    {
                        if (rule.StartsWith("!") || rule.StartsWith("["))
                            continue;
                        lines.Add(rule);
                    }
                }
                var abpContent = gfwlist_template;
                if (File.Exists(USER_ABP_FILE))
                {
                    abpContent = await File.ReadAllTextAsync(USER_ABP_FILE, Encoding.UTF8);
                }
                else
                {
                    abpContent = gfwlist_template;
                }
                abpContent = abpContent.Replace("__RULES__", SimpleJson.SimpleJson.SerializeObject(lines));
                if (File.Exists(PAC_FILE))
                {
                    var original = await File.ReadAllTextAsync(PAC_FILE, Encoding.UTF8);
                    if (original == abpContent)
                    {
                        update_type = 0;
                        UpdateCompleted(false);
                        return;
                    }
                }
                
                await File.WriteAllTextAsync(PAC_FILE, abpContent, Encoding.UTF8);
                
                if (UpdateCompleted != null)
                {
                    update_type = 0;
                    UpdateCompleted(true);
                }
            }
            catch (Exception ex)
            {
                if (Error != null)
                {
                    if (!isBackupRequest)
                    {
                        await http_DownloadStringCompleted(http, http.GetStringAsync($"{GFWLIST_BACKUP_URL}?rnd={Utils.RandUInt32()}"), true);
                    }
                    else
                    {
                        Error(ex);
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
                    var original = await File.ReadAllTextAsync(PAC_FILE, Encoding.UTF8);
                    
                    if (original == content)
                    {
                        update_type = 1;
                        UpdateCompleted(false);
                        return;
                    }
                }
                
                await File.WriteAllTextAsync(PAC_FILE, content, Encoding.UTF8);
                
                if (UpdateCompleted != null)
                {
                    update_type = 1;
                    UpdateCompleted(true);
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
            }

        }

        public async Task UpdatePACFromGFWList(Configuration config)
        {
            if (gfwlist_template == null)
            {
                lastConfig = config;

                var proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
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
                    string.IsNullOrEmpty(config.proxyUserAgent) ?
                    "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                    : config.proxyUserAgent);

                await http_DownloadGFWTemplateCompleted(http.GetStringAsync($"{GFWLIST_TEMPLATE_URL}?rnd={Utils.RandUInt32()}"));
            }
            else
            {
                var proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
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
                    string.IsNullOrEmpty(config.proxyUserAgent) ?
                    "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                    : config.proxyUserAgent);

                await http_DownloadStringCompleted(http, http.GetStringAsync($"{GFWLIST_URL}?rnd={Utils.RandUInt32()}"), false);
            }
        }

        public async Task UpdatePACFromGFWList(Configuration config, string url)
        {
            var proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
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
            string.IsNullOrEmpty(config.proxyUserAgent) ?
                "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                : config.proxyUserAgent);

            await http_DownloadPACCompleted(http.GetStringAsync($"{url}?rnd={Utils.RandUInt32()}"));
        }

        public List<string> ParseResult(string response)
        {
            var bytes = Convert.FromBase64String(response);
            var content = Encoding.ASCII.GetString(bytes);
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var valid_lines = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                if (line.StartsWith("!") || line.StartsWith("["))
                    continue;
                valid_lines.Add(line);
            }
            return valid_lines;
        }
    }
}