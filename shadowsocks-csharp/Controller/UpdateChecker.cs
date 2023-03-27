using Shadowsocks.Model;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace Shadowsocks.Controller;

public class UpdateChecker
{
    private const string UpdateURL = "https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/update/ssr-win-4.0.xml";

    public string LatestVersionNumber;
    public string LatestVersionURL;
    public Action NewVersionFound;

    public const string Name = "ShadowsocksR";
    public const string Copyright = "Copyright © Akkariiin 2019 & BreakWa11 2017. Fork from Shadowsocks by clowwindy";
    public const string Version = "4.9.2";
#if DEBUG
    public const string FullVersion = Version + " Debug";
#else
        public const string FullVersion = Version;
#endif

    private static readonly bool UseProxy = true;

    public async Task CheckUpdate(Configuration config)
    {
        try
        {
            HttpClient http;
            if (UseProxy)
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

            using (http)
                await http_DownloadStringCompleted(http.GetStringAsync($"{UpdateURL}?rnd={Util.Utils.RandUInt32()}"));
        }
        catch (Exception e)
        {
            Logging.LogUsefulException(e);
        }
    }

    public static int CompareVersion(string l, string r)
    {
        var ls = l.Split('.');
        var rs = r.Split('.');
        for (var i = 0; i < Math.Max(ls.Length, rs.Length); i++)
        {
            var lp = i < ls.Length ? int.Parse(ls[i]) : 0;
            var rp = i < rs.Length ? int.Parse(rs[i]) : 0;
            if (lp != rp)
            {
                return lp - rp;
            }
        }
        return 0;
    }

    public class VersionComparer : IComparer<string>
    {
        // Calls CaseInsensitiveComparer.Compare with the parameters reversed.
        public int Compare(string x, string y) => CompareVersion(ParseVersionFromURL(x), ParseVersionFromURL(y));

    }

    private static string ParseVersionFromURL(string url)
    {
        var match = Regex.Match(url, $@".*{Name}-win.*?-([\d\.]+)\.\w+", RegexOptions.IgnoreCase);
        if (match is { Success: true, Groups.Count: 2 })
        {
            return match.Groups[1].Value;
        }
        return null;
    }

    private void SortVersions(List<string> versions)
    {
        versions.Sort(new VersionComparer());
    }

    private bool IsNewVersion(string url)
    {
        if (url.Contains("prerelease"))
        {
            return false;
        }
        // check dotnet 4.0
        var references = Assembly.GetExecutingAssembly().GetReferencedAssemblies();
        var dotNetVersion = Environment.Version;
        foreach (var reference in references)
        {
            if (reference.Name == "mscorlib")
            {
                dotNetVersion = reference.Version;
            }
        }
        if (dotNetVersion.Major >= 4)
        {
            if (!url.Contains("dotnet4.0"))
            {
                return false;
            }
        }
        else
        {
            if (url.Contains("dotnet4.0"))
            {
                return false;
            }
        }
        var version = ParseVersionFromURL(url);
        if (version == null)
        {
            return false;
        }
        var currentVersion = Version;

        if (url.IndexOf("banned") > 0 && CompareVersion(version, currentVersion) == 0
            || url.IndexOf("deprecated") > 0 && CompareVersion(version, currentVersion) > 0)
        {
            Application.Exit();
            return false;
        }
        return CompareVersion(version, currentVersion) > 0;
    }

    private async Task http_DownloadStringCompleted(Task<string> task)
    {
        try
        {
            var response = await task;

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(response);
            var elements = xmlDoc.GetElementsByTagName("media:content");
            var versions = new List<string>();
            foreach (XmlNode el in elements)
            {
                foreach (XmlAttribute attr in el.Attributes)
                {
                    if (attr.Name == "url")
                    {
                        if (IsNewVersion(attr.Value))
                        {
                            versions.Add(attr.Value);
                        }
                    }
                }
            }
            if (versions.Count == 0)
            {
                return;
            }
            // sort versions
            SortVersions(versions);
            LatestVersionURL = versions[^1];
            LatestVersionNumber = ParseVersionFromURL(LatestVersionURL);
            NewVersionFound?.Invoke();
        }
        catch (Exception ex)
        {
            Logging.Debug(ex.ToString());
            NewVersionFound?.Invoke();
        }
    }
}