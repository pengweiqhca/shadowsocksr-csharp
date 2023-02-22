using Shadowsocks.Model;
using System.Net;

namespace Shadowsocks.Controller
{
    public class UpdateFreeNode
    {
        private const string UpdateURL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/free/freenodeplain.txt";

        public event EventHandler NewFreeNodeFound;
        public string FreeNodeResult;
        public ServerSubscribe subscribeTask;
        public bool noitify;

        public const string Name = "ShadowsocksR";

        public async void CheckUpdate(Configuration config, ServerSubscribe subscribeTask, bool use_proxy, bool noitify)
        {
            FreeNodeResult = null;
            this.noitify = noitify;
            try
            {
                HttpClient http;
                if (use_proxy)
                {
                    var proxy = new WebProxy(IPAddress.Loopback.ToString(), config.localPort);
                    if (!string.IsNullOrEmpty(config.authPass))
                    {
                        proxy.Credentials = new NetworkCredential(config.authUser, config.authPass);
                    }

                    http = new HttpClient(new HttpClientHandler
                    {
                        Proxy = proxy,
                        UseProxy = true,
                    });
                }
                else http = new HttpClient();

                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    string.IsNullOrEmpty(config.proxyUserAgent) ?
                    "Mozilla/5.0 (Windows NT 5.1) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/35.0.3319.102 Safari/537.36"
                    : config.proxyUserAgent);


                this.subscribeTask = subscribeTask;
                var URL = subscribeTask.URL ?? UpdateURL;
                if (!URL.Contains('?')) URL += '?';
                if (!URL.EndsWith("?") && !URL.EndsWith("&")) URL += '&';

                URL += $"rnd={Util.Utils.RandUInt32()}";

                using (http)
                    await http_DownloadStringCompleted(http.GetStringAsync(URL));
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
            }
        }

        private async Task http_DownloadStringCompleted(Task<string> task)
        {
            try
            {
                FreeNodeResult = await task;

                NewFreeNodeFound?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logging.Debug(ex.ToString());
                NewFreeNodeFound?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public class UpdateSubscribeManager
    {
        private Configuration _config;
        private List<ServerSubscribe> _serverSubscribes;
        private UpdateFreeNode _updater;
        private string _URL;
        private bool _use_proxy;
        public bool _noitify;

        public void CreateTask(Configuration config, UpdateFreeNode updater, int index, bool use_proxy, bool noitify)
        {
            if (_config == null)
            {
                _config = config;
                _updater = updater;
                _use_proxy = use_proxy;
                _noitify = noitify;
                if (index < 0)
                {
                    _serverSubscribes = new List<ServerSubscribe>();
                    for (var i = 0; i < config.serverSubscribes.Count; ++i)
                    {
                        _serverSubscribes.Add(config.serverSubscribes[i]);
                    }
                }
                else if (index < _config.serverSubscribes.Count)
                {
                    _serverSubscribes = new List<ServerSubscribe>
                    {
                        config.serverSubscribes[index]
                    };
                }
                Next();
            }
        }

        public bool Next()
        {
            if (_serverSubscribes.Count == 0)
            {
                _config = null;
                return false;
            }
            _URL = _serverSubscribes[0].URL;
            _updater.CheckUpdate(_config, _serverSubscribes[0], _use_proxy, _noitify);
            _serverSubscribes.RemoveAt(0);
            return true;
        }

        public string URL
        {
            get => _URL;
        }
    }
}
