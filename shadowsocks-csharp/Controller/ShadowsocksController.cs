﻿using Shadowsocks.Model;
using System.Net.Sockets;

namespace Shadowsocks.Controller;

public enum ProxyMode
{
    NoModify,
    Direct,
    Pac,
    Global,
}

public class ShadowsocksController
{
    // controller:
    // handle user actions
    // manipulates UI
    // interacts with low level logic

    private Listener _listener;
    private List<Listener> _port_map_listener;
    private PACServer _pacServer;
    private Configuration _config;
    private readonly ServerTransferTotal _transfer;
    public IPRangeSet _rangeSet;
    private HttpProxyRunner polipoRunner;
    private GFWListUpdater gfwListUpdater;
    private bool stopped;
    private readonly bool firstRun = true;

    public Action ConfigChanged;
    public Action ToggleModeChanged;
    public Action ToggleRuleModeChanged;
    public Action<int> ShowConfigFormEvent;

    // when user clicked Edit PAC, and PAC file has already created
    public Action<string> PACFileReadyToOpen;
    public Action<string> UserRuleFileReadyToOpen;

    public Action<GFWListUpdater, bool> UpdatePACFromGFWListCompleted;

    public Action<Exception> UpdatePACFromGFWListError;

    public Action<Exception> Errored;

    public ShadowsocksController()
    {
        _config = Configuration.Load();
        _transfer = ServerTransferTotal.Load();

        foreach (var server in _config.configs)
        {
            if (_transfer.servers.ContainsKey(server.server))
            {
                var log = new ServerSpeedLog(((ServerTrans)_transfer.servers[server.server]).totalUploadBytes, ((ServerTrans)_transfer.servers[server.server]).totalDownloadBytes);
                server.SetServerSpeedLog(log);
            }
        }
    }

    public void Start()
    {
        Reload();
    }

    protected void ReportError(Exception e)
    {
        Errored?.Invoke(e);
    }

    public void ReloadIPRange()
    {
        _rangeSet = new IPRangeSet();
        _rangeSet.LoadChn();
        if (_config.proxyRuleMode == (int)ProxyRuleMode.BypassLanAndNotChina)
        {
            _rangeSet.Reverse();
        }
    }

    // always return copy
    public Configuration GetConfiguration() => Configuration.Load();

    public Configuration GetCurrentConfiguration() => _config;

    private int FindFirstMatchServer(Server server, IReadOnlyList<Server> servers)
    {
        for (var i = 0; i < servers.Count; ++i)
        {
            if (server.isMatchServer(servers[i]))
            {
                return i;
            }
        }
        return -1;
    }

    public void AppendConfiguration(Configuration mergeConfig, List<Server> servers)
    {
        if (servers != null)
        {
            for (var j = 0; j < servers.Count; ++j)
            {
                if (FindFirstMatchServer(servers[j], mergeConfig.configs) == -1)
                {
                    mergeConfig.configs.Add(servers[j]);
                }
            }
        }
    }

    public List<Server> MergeConfiguration(Configuration mergeConfig, List<Server> servers)
    {
        var missingServers = new List<Server>();
        if (servers != null)
        {
            for (var j = 0; j < servers.Count; ++j)
            {
                var i = FindFirstMatchServer(servers[j], mergeConfig.configs);
                if (i != -1)
                {
                    var enable = servers[j].enable;
                    servers[j].CopyServer(mergeConfig.configs[i]);
                    servers[j].enable = enable;
                }
            }
        }
        for (var i = 0; i < mergeConfig.configs.Count; ++i)
        {
            var j = FindFirstMatchServer(mergeConfig.configs[i], servers);
            if (j == -1)
            {
                missingServers.Add(mergeConfig.configs[i]);
            }
        }
        return missingServers;
    }

    public Configuration MergeGetConfiguration(Configuration mergeConfig)
    {
        var ret = Configuration.Load();
        if (mergeConfig != null)
        {
            MergeConfiguration(mergeConfig, ret.configs);
        }
        return ret;
    }

    public void MergeConfiguration(Configuration mergeConfig)
    {
        AppendConfiguration(_config, mergeConfig.configs);
        SaveConfig(_config);
    }

    public bool SaveServersConfig(string config)
    {
        var new_cfg = Configuration.Load(config);
        if (new_cfg != null)
        {
            SaveServersConfig(new_cfg);
            return true;
        }
        return false;
    }

    public void SaveServersConfig(Configuration config)
    {
        var missingServers = MergeConfiguration(_config, config.configs);
        _config.CopyFrom(config);
        foreach (var s in missingServers)
        {
            s.GetConnections().CloseAll();
        }
        SelectServerIndex(_config.index);
    }

    public void SaveServersPortMap(Configuration config)
    {
        _config.portMap = config.portMap;
        SelectServerIndex(_config.index);
        _config.FlushPortMapCache();
    }

    public bool AddServerBySSURL(string ssURL, string force_group = null, bool toLast = false)
    {
        if (ssURL.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) || ssURL.StartsWith("ssr://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var server = new Server(ssURL, force_group);
                if (toLast)
                {
                    _config.configs.Add(server);
                }
                else
                {
                    var index = _config.index + 1;
                    if (index < 0 || index > _config.configs.Count)
                        index = _config.configs.Count;
                    _config.configs.Insert(index, server);
                }
                SaveConfig(_config);
                return true;
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                return false;
            }
        }
        return false;
    }

    public void ToggleMode(ProxyMode mode)
    {
        _config.sysProxyMode = (int)mode;
        SaveConfig(_config);
        ToggleModeChanged?.Invoke();
    }

    public void ToggleRuleMode(int mode)
    {
        _config.proxyRuleMode = mode;
        SaveConfig(_config);
        ToggleRuleModeChanged?.Invoke();
    }

    public void ToggleSelectRandom(bool enabled)
    {
        _config.random = enabled;
        SaveConfig(_config);
    }

    public void ToggleSameHostForSameTargetRandom(bool enabled)
    {
        _config.sameHostForSameTarget = enabled;
        SaveConfig(_config);
    }

    public void SelectServerIndex(int index)
    {
        _config.index = index;
        SaveConfig(_config);
    }

    public void Stop()
    {
        if (stopped)
        {
            return;
        }
        stopped = true;

        if (_port_map_listener != null)
        {
            foreach (var l in _port_map_listener)
            {
                l.Stop();
            }
            _port_map_listener = null;
        }
        _listener?.Stop();
            
        polipoRunner?.Stop();
        if (_config.sysProxyMode is not (int)ProxyMode.NoModify and not (int)ProxyMode.Direct)
        {
            SystemProxy.Update(_config, true);
        }
            
        ServerTransferTotal.Save(_transfer);
    }

    public void ClearTransferTotal(string server_addr)
    {
        _transfer.Clear(server_addr);
        foreach (var server in _config.configs)
        {
            if (server.server == server_addr)
            {
                if (_transfer.servers.ContainsKey(server.server))
                {
                    server.ServerSpeedLog().ClearTrans();
                }
            }
        }
    }

    public void TouchPACFile()
    {
        PACFileReadyToOpen?.Invoke(_pacServer.TouchPACFile());
    }

    public void TouchUserRuleFile()
    {
        UserRuleFileReadyToOpen?.Invoke(_pacServer.TouchUserRuleFile());
    }

    public void UpdatePACFromGFWList()
    {
        if (gfwListUpdater != null)
        {
            _ = gfwListUpdater.UpdatePACFromGFWList(_config);
        }
    }

    public Task UpdatePACFromOnlinePac(string url) =>
        gfwListUpdater == null ? Task.CompletedTask : gfwListUpdater.UpdatePACFromGFWList(_config, url);

    protected void Reload()
    {
        if (_port_map_listener != null)
        {
            foreach (var l in _port_map_listener)
            {
                l.Stop();
            }
            _port_map_listener = null;
        }
        // some logic in configuration updated the config when saving, we need to read it again
        _config = MergeGetConfiguration(_config);
        _config.FlushPortMapCache();
        ReloadIPRange();

        var hostMap = new HostMap();
        hostMap.LoadHostFile();
        HostMap.Instance().Clear(hostMap);

        polipoRunner ??= new HttpProxyRunner();
            
        if (_pacServer == null)
        {
            _pacServer = new PACServer();
            _pacServer.PACFileChanged += UpdateSystemProxy;
        }
        _pacServer.UpdateConfiguration(_config);
        if (gfwListUpdater == null)
        {
            gfwListUpdater = new GFWListUpdater();
            gfwListUpdater.UpdateCompleted += pacServer_PACUpdateCompleted;
            gfwListUpdater.Error += pacServer_PACUpdateError;
        }

        // don't put polipoRunner.Start() before pacServer.Stop()
        // or bind will fail when switching bind address from 0.0.0.0 to 127.0.0.1
        // though UseShellExecute is set to true now
        // http://stackoverflow.com/questions/10235093/socket-doesnt-close-after-application-exits-if-a-launched-process-is-open
        var _firstRun = firstRun;
        for (var i = 1; i <= 5; ++i)
        {
            _firstRun = false;
            try
            {
                if (_listener != null && !_listener.isConfigChange(_config))
                {
                    var local = new Local(_config, _transfer, _rangeSet);
                    _listener.GetServices()[0] = local;
                        
                    if (polipoRunner.HasExited())
                    {
                        polipoRunner.Stop();
                        polipoRunner.Start(_config);

                        _listener.GetServices()[3] = new HttpPortForwarder(polipoRunner.RunningPort, _config);
                    }
                }
                else
                {
                    if (_listener != null)
                    {
                        _listener.Stop();
                        _listener = null;
                    }

                    polipoRunner.Stop();
                    polipoRunner.Start(_config);

                    var local = new Local(_config, _transfer, _rangeSet);
                    var services = new List<Listener.Service>
                    {
                        local,
                        _pacServer,
                        new APIServer(this, _config),
                        new HttpPortForwarder(polipoRunner.RunningPort, _config)
                    };
                    _listener = new Listener(services);
                    _listener.Start(_config, 0);
                }
                break;
            }
            catch (Exception e)
            {
                // translate Microsoft language into human language
                // i.e. An attempt was made to access a socket in a way forbidden by its access permissions => Port already in use
                if (e is SocketException { SocketErrorCode: SocketError.AccessDenied } se)
                {
                    e = new Exception($"{I18N.GetString("Port already in use")} {_config.localPort}", se);
                }
                Logging.LogUsefulException(e);
                if (!_firstRun)
                {
                    ReportError(e);
                    break;
                }
            }
        }

        _port_map_listener = new List<Listener>();
        foreach (var pair in _config.GetPortMapCache())
        {
            try
            {
                var local = new Local(_config, _transfer, _rangeSet);
                var services = new List<Listener.Service>
                {
                    local
                };
                var listener = new Listener(services);
                listener.Start(_config, pair.Key);
                _port_map_listener.Add(listener);
            }
            catch (Exception e)
            {
                // translate Microsoft language into human language
                // i.e. An attempt was made to access a socket in a way forbidden by its access permissions => Port already in use
                if (e is SocketException { SocketErrorCode: SocketError.AccessDenied } se)
                {
                    e = new Exception($"{I18N.GetString("Port already in use")} {pair.Key}", se);
                }
                Logging.LogUsefulException(e);
                ReportError(e);
            }
        }

        ConfigChanged?.Invoke();

        UpdateSystemProxy();
        Util.Utils.ReleaseMemory();
    }

    protected void SaveConfig(Configuration newConfig)
    {
        Configuration.Save(newConfig);
        Reload();
    }


    private void UpdateSystemProxy()
    {
        if (_config.sysProxyMode != (int)ProxyMode.NoModify)
        {
            SystemProxy.Update(_config, false);
        }
    }

    private void pacServer_PACUpdateCompleted(bool result)
    {
        UpdatePACFromGFWListCompleted?.Invoke(gfwListUpdater, result);
    }

    private void pacServer_PACUpdateError(Exception ex)
    {
        UpdatePACFromGFWListError?.Invoke(ex);
    }

    public void ShowConfigForm(int index)
    {
        ShowConfigFormEvent?.Invoke(index);
    }

    /// <summary>
    /// Disconnect all connections from the remote host.
    /// </summary>
    public void DisconnectAllConnections()
    {
        foreach (var server in GetCurrentConfiguration().configs)
        {
            server.GetConnections().CloseAll();
        }
    }
}