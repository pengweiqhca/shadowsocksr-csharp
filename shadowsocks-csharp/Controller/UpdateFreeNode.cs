using Shadowsocks.Model;
using System.Net;

namespace Shadowsocks.Controller;

public class UpdateFreeNode
{
    private const string UpdateURL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/free/freenodeplain.txt";

    public Action NewFreeNodeFound;
    public string FreeNodeResult;
    public ServerSubscribe subscribeTask;
    public bool noitify;

    public const string Name = "ShadowsocksR";

    public async Task CheckUpdate(Configuration config, ServerSubscribe subscribeTask, bool use_proxy, bool noitify)
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
        }
        catch (Exception ex)
        {
            Logging.Debug(ex.ToString());
        }

        NewFreeNodeFound?.Invoke();
    }
}

public class UpdateSubscribeManager
{
    private string _URL;

    public async Task CreateTask(Configuration config, UpdateFreeNode updater, bool use_proxy, bool noitify)
    {
        var serverSubscribes = new List<ServerSubscribe>();
        serverSubscribes.AddRange(config.serverSubscribes);

        foreach (var subscribe in serverSubscribes)
        {
            _URL = subscribe.URL;

            await updater.CheckUpdate(config, subscribe, use_proxy, noitify);
        }
    }

    public string URL
    {
        get => _URL;
    }
}