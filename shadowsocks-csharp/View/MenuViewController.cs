using Shadowsocks.Controller;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace Shadowsocks.View
{
    public class EventParams
    {
        public object sender;
        public EventArgs e;

        public EventParams(object sender, EventArgs e)
        {
            this.sender = sender;
            this.e = e;
        }
    }

    public class MenuViewController
    {
        // yes this is just a menu view controller
        // when config form is closed, it moves away from RAM
        // and it should just do anything related to the config form

        private readonly ShadowsocksController controller;
        private readonly UpdateChecker updateChecker;
        private readonly UpdateFreeNode updateFreeNodeChecker;
        private readonly UpdateSubscribeManager updateSubscribeManager;

        private readonly NotifyIcon _notifyIcon;
        private ContextMenuStrip contextMenu1;

        private ToolStripMenuItem noModifyItem;
        private ToolStripMenuItem enableItem;
        private ToolStripMenuItem PACModeItem;
        private ToolStripMenuItem globalModeItem;

        private ToolStripMenuItem ruleBypassLan;
        private ToolStripMenuItem ruleBypassChina;
        private ToolStripMenuItem ruleBypassNotChina;
        private ToolStripMenuItem ruleUser;
        private ToolStripMenuItem ruleDisableBypass;

        private ToolStripItem SeperatorItem;
        private ToolStripMenuItem ServersItem;
        private ToolStripMenuItem SelectRandomItem;
        private ToolStripMenuItem sameHostForSameTargetItem;
        private ToolStripMenuItem UpdateItem;
        private ConfigForm configForm;
        private SettingsForm settingsForm;
        private ServerLogForm serverLogForm;
        private PortSettingsForm portMapForm;
        private SubscribeForm subScribeForm;
        private LogForm logForm;
        private string _urlToOpen;
        private System.Windows.Forms.Timer timerDelayCheckUpdate;

        private bool configfrom_open;
        private int eventList;

        public MenuViewController(ShadowsocksController controller)
        {
            this.controller = controller;

            LoadMenu();

            controller.ToggleModeChanged += controller_ToggleModeChanged;
            controller.ToggleRuleModeChanged += controller_ToggleRuleModeChanged;
            controller.ConfigChanged += controller_ConfigChanged;
            controller.PACFileReadyToOpen += controller_FileReadyToOpen;
            controller.UserRuleFileReadyToOpen += controller_FileReadyToOpen;
            controller.Errored += controller_Errored;
            controller.UpdatePACFromGFWListCompleted += controller_UpdatePACFromGFWListCompleted;
            controller.UpdatePACFromGFWListError += controller_UpdatePACFromGFWListError;
            controller.ShowConfigFormEvent += ShowConfigForm;

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = contextMenu1;
            _notifyIcon.MouseClick += notifyIcon1_Click;
            //_notifyIcon.MouseDoubleClick += notifyIcon1_DoubleClick;

            updateChecker = new UpdateChecker();
            updateChecker.NewVersionFound += updateChecker_NewVersionFound;

            updateFreeNodeChecker = new UpdateFreeNode();
            updateFreeNodeChecker.NewFreeNodeFound += updateFreeNodeChecker_NewFreeNodeFound;

            updateSubscribeManager = new UpdateSubscribeManager();

            timerDelayCheckUpdate = new System.Windows.Forms.Timer() { Interval = 1000 * 10 };
            timerDelayCheckUpdate.Tick += timer_Elapsed;
            timerDelayCheckUpdate.Start();
        }

        private async void timer_Elapsed(object sender, EventArgs e)
        {
            if (timerDelayCheckUpdate != null)
            {
                timerDelayCheckUpdate.Interval = 1000 * 60 * 60 * 6;
            }

            await updateChecker.CheckUpdate(controller.GetCurrentConfiguration());

            var cfg = controller.GetCurrentConfiguration();
            if (cfg.isDefaultConfig() || cfg.nodeFeedAutoUpdate)
            {
                await updateSubscribeManager.CreateTask(controller.GetCurrentConfiguration(), updateFreeNodeChecker, !cfg.isDefaultConfig(), false);
            }
        }

        void controller_Errored(Exception ex)
        {
            MessageBox.Show(ex.ToString(), string.Format(I18N.GetString("Shadowsocks Error: {0}"), ex.Message));
        }

        private void UpdateTrayIcon()
        {
            var dpi = 96;
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                dpi = (int)graphics.DpiX;
            }
            var config = controller.GetCurrentConfiguration();
            var enabled = config.sysProxyMode is not (int)ProxyMode.NoModify and not (int)ProxyMode.Direct;
            var global = config.sysProxyMode == (int)ProxyMode.Global;
            var random = config.random;

            var useDefaultIcon = true;
            try
            {
                var file = Path.Combine(AppContext.BaseDirectory, "icon.png");
                if (File.Exists(file))
                {
                    var icon = new Bitmap("icon.png");
                    var newIcon = Icon.FromHandle(icon.GetHicon());
                    icon.Dispose();
                    _notifyIcon.Icon = newIcon;

                    useDefaultIcon = false;
                }
            }
            catch
            {
            }

            if (useDefaultIcon)
            {
                var icon = dpi switch
                {
                    < 97 => Resources.ss16,
                    < 121 => Resources.ss20,
                    _ => Resources.ss24
                };
                double mul_a = 1.0, mul_r = 1.0, mul_g = 1.0, mul_b = 1.0;
                if (!enabled)
                {
                    mul_g = 0.4;
                }
                else if (!global)
                {
                    mul_b = 0.4;
                    mul_g = 0.8;
                }
                if (!random)
                {
                    mul_r = 0.4;
                }

                var iconCopy = new Bitmap(icon);
                for (var x = 0; x < iconCopy.Width; x++)
                {
                    for (var y = 0; y < iconCopy.Height; y++)
                    {
                        var color = icon.GetPixel(x, y);
                        iconCopy.SetPixel(x, y,

                            Color.FromArgb((byte)(color.A * mul_a),
                                (byte)(color.R * mul_r),
                                (byte)(color.G * mul_g),
                                (byte)(color.B * mul_b)));
                    }
                }
                var newIcon = Icon.FromHandle(iconCopy.GetHicon());
                icon.Dispose();
                iconCopy.Dispose();

                _notifyIcon.Icon = newIcon;
            }

            // we want to show more details but notify icon title is limited to 63 characters
            var text = $"{(enabled ? global ? I18N.GetString("Global") : I18N.GetString("PAC") : I18N.GetString("Disable system proxy"))}\r\n{string.Format(I18N.GetString("Running: Port {0}"), config.localPort)}"// this feedback is very important because they need to know Shadowsocks is running
                    ;
            _notifyIcon.Text = text[..Math.Min(63, text.Length)];
        }

        private ToolStripMenuItem CreateMenuItem(string text, EventHandler click) => new(I18N.GetString(text), null, click);

        private ToolStripMenuItem CreateMenuGroup(string text, params ToolStripItem[] dropDownItems) => new(I18N.GetString(text), null, dropDownItems);

        private void LoadMenu()
        {
            contextMenu1 = new ContextMenuStrip();
            contextMenu1.Items.AddRange(new ToolStripItem[]
            {
                CreateMenuGroup("Mode",
                    enableItem = CreateMenuItem("Disable system proxy", EnableItem_Click),
                    PACModeItem = CreateMenuItem("PAC", PACModeItem_Click),
                    globalModeItem = CreateMenuItem("Global", GlobalModeItem_Click),
                    new ToolStripSeparator(),
                    noModifyItem = CreateMenuItem("No modify system proxy", NoModifyItem_Click)),
                CreateMenuGroup("PAC ",
                    CreateMenuItem("Update local PAC from Lan IP list", UpdatePACFromLanIPListItem_Click),
                    new ToolStripSeparator(),
                    CreateMenuItem("Update local PAC from Chn White list", UpdatePACFromCNWhiteListItem_Click),
                    CreateMenuItem("Update local PAC from Chn IP list", UpdatePACFromCNIPListItem_Click),
                    CreateMenuItem("Update local PAC from GFWList", UpdatePACFromGFWListItem_Click),
                    new ToolStripSeparator(),
                    CreateMenuItem("Update local PAC from Chn Only list", UpdatePACFromCNOnlyListItem_Click),
                    new ToolStripSeparator(),
                    CreateMenuItem("Copy PAC URL", CopyPACURLItem_Click),
                    CreateMenuItem("Edit local PAC file...", EditPACFileItem_Click),
                    CreateMenuItem("Edit user rule for GFWList...", EditUserRuleFileForGFWListItem_Click)),
                CreateMenuGroup("Proxy rule",
                    ruleBypassLan = CreateMenuItem("Bypass LAN", RuleBypassLanItem_Click),
                    ruleBypassChina = CreateMenuItem("Bypass LAN && China", RuleBypassChinaItem_Click),
                    ruleBypassNotChina = CreateMenuItem("Bypass LAN && not China", RuleBypassNotChinaItem_Click),
                    ruleUser = CreateMenuItem("User custom", RuleUserItem_Click),
                    new ToolStripSeparator(),
                    ruleDisableBypass = CreateMenuItem("Disable bypass", RuleBypassDisableItem_Click)),
                new ToolStripSeparator(),
                ServersItem = CreateMenuGroup("Servers",
                    SeperatorItem = new ToolStripSeparator(),
                    CreateMenuItem("Edit servers...", Config_Click),
                    CreateMenuItem("Import servers from file...", Import_Click),
                    new ToolStripSeparator(),
                    sameHostForSameTargetItem = CreateMenuItem("Same host for same address", SelectSameHostForSameTargetItem_Click),
                    new ToolStripSeparator(),
                    CreateMenuItem("Server statistic...", ShowServerLogItem_Click),
                    CreateMenuItem("Disconnect current", DisconnectCurrent_Click)),
                CreateMenuGroup("Servers Subscribe",
                    CreateMenuItem("Subscribe setting...", SubscribeSetting_Click),
                    CreateMenuItem("Update subscribe SSR node", CheckNodeUpdate_Click),
                    CreateMenuItem("Update subscribe SSR node(bypass proxy)", CheckNodeUpdateBypassProxy_Click)),
                SelectRandomItem = CreateMenuItem("Load balance", SelectRandomItem_Click),
                CreateMenuItem("Global settings...", Setting_Click),
                CreateMenuItem("Port settings...", ShowPortMapItem_Click),
                UpdateItem = CreateMenuItem("Update available", UpdateItem_Clicked),
                new ToolStripSeparator(),
                CreateMenuItem("Scan QRCode from screen...", ScanQRCodeItem_Click),
                CreateMenuItem("Import SSR links from clipboard...", CopyAddress_Click),
                new ToolStripSeparator(),
                CreateMenuGroup("Help",
                    CreateMenuItem("Check update", CheckUpdate_Click),
                    CreateMenuItem("Show logs...", ShowLogItem_Click),
                    CreateMenuItem("Open wiki...", OpenWiki_Click),
                    CreateMenuItem("Feedback...", FeedbackItem_Click),
                    new ToolStripSeparator(),
                    CreateMenuItem("Gen custom QRCode...", showURLFromQRCode),
                    CreateMenuItem("Reset password...", ResetPasswordItem_Click),
                    new ToolStripSeparator(),
                    CreateMenuItem("About...", AboutItem_Click),
                    CreateMenuItem("Donate...", DonateItem_Click)),
                CreateMenuItem("Quit", Quit_Click)
            });
            UpdateItem.Visible = false;
        }

        private void controller_ConfigChanged()
        {
            LoadCurrentConfiguration();
            UpdateTrayIcon();
        }

        private void controller_ToggleModeChanged()
        {
            var config = controller.GetCurrentConfiguration();
            UpdateSysProxyMode(config);
        }

        private void controller_ToggleRuleModeChanged()
        {
            var config = controller.GetCurrentConfiguration();
            UpdateProxyRule(config);
        }

        void controller_FileReadyToOpen(string path)
        {
            var argument = $@"/select, {path}";

            Process.Start("explorer.exe", argument);
        }

        void ShowBalloonTip(string title, string content, ToolTipIcon icon, int timeout)
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = content;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(timeout);
        }

        void controller_UpdatePACFromGFWListError(Exception ex)
        {
            ShowBalloonTip(I18N.GetString("Failed to update PAC file"), ex.Message, ToolTipIcon.Error, 5000);
            Logging.LogUsefulException(ex);
        }

        void controller_UpdatePACFromGFWListCompleted(GFWListUpdater updater, bool success)
        {
            var result = success ?
                updater.update_type <= 1 ? I18N.GetString("PAC updated") : I18N.GetString("Domain white list list updated")
                : I18N.GetString("No updates found. Please report to GFWList if you have problems with it.");
            ShowBalloonTip(I18N.GetString("Shadowsocks"), result, ToolTipIcon.Info, 1000);
        }

        void updateFreeNodeChecker_NewFreeNodeFound()
        {
            if (configfrom_open)
            {
                eventList++;
                return;
            }
            string lastGroup = null;
            var count = 0;
            if (!string.IsNullOrEmpty(updateFreeNodeChecker.FreeNodeResult))
            {
                var urls = new List<string>();
                updateFreeNodeChecker.FreeNodeResult = updateFreeNodeChecker.FreeNodeResult.TrimEnd('\r', '\n', ' ');
                var config = controller.GetCurrentConfiguration();
                Server selected_server = null;
                if (config.index >= 0 && config.index < config.configs.Count)
                {
                    selected_server = config.configs[config.index];
                }
                try
                {
                    updateFreeNodeChecker.FreeNodeResult = Util.Base64.DecodeBase64(updateFreeNodeChecker.FreeNodeResult);
                }
                catch
                {
                    updateFreeNodeChecker.FreeNodeResult = "";
                }
                var max_node_num = 0;

                var match_maxnum = Regex.Match(updateFreeNodeChecker.FreeNodeResult, "^MAX=([0-9]+)");
                if (match_maxnum.Success)
                {
                    try
                    {
                        max_node_num = Convert.ToInt32(match_maxnum.Groups[1].Value, 10);
                    }
                    catch
                    {

                    }
                }
                URL_Split(updateFreeNodeChecker.FreeNodeResult, ref urls);
                for (var i = urls.Count - 1; i >= 0; --i)
                {
                    if (!urls[i].StartsWith("ssr"))
                        urls.RemoveAt(i);
                }
                if (urls.Count > 0)
                {
                    var keep_selected_server = false; // set 'false' if import all nodes
                    if (max_node_num <= 0 || max_node_num >= urls.Count)
                    {
                        urls.Reverse();
                    }
                    else
                    {
                        var r = new Random();
                        Util.Utils.Shuffle(urls, r);
                        urls.RemoveRange(max_node_num, urls.Count - max_node_num);
                        if (!config.isDefaultConfig())
                            keep_selected_server = true;
                    }
                    string curGroup = null;
                    foreach (var url in urls)
                    {
                        try // try get group name
                        {
                            var server = new Server(url, null);
                            if (!string.IsNullOrEmpty(server.group))
                            {
                                curGroup = server.group;
                                break;
                            }
                        }
                        catch
                        { }
                    }
                    var subscribeURL = updateSubscribeManager.URL;
                    if (string.IsNullOrEmpty(curGroup))
                    {
                        curGroup = subscribeURL;
                    }
                    foreach (var subscribe in config.serverSubscribes)
                    {
                        if (subscribeURL == subscribe.URL)
                        {
                            lastGroup = subscribe.Group;
                            subscribe.Group = curGroup;
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(lastGroup))
                    {
                        lastGroup = curGroup;
                    }

                    if (keep_selected_server && selected_server.group == curGroup)
                    {
                        var match = false;
                        foreach (var url in urls)
                        {
                            try
                            {
                                var server = new Server(url, null);
                                if (selected_server.isMatchServer(server))
                                {
                                    match = true;
                                    break;
                                }
                            }
                            catch
                            { }
                        }
                        if (!match)
                        {
                            urls.RemoveAt(0);
                            urls.Add(selected_server.GetSSRLinkForServer());
                        }
                    }

                    // import all, find difference
                    var old_servers = new Dictionary<string, Server>();
                    var old_insert_servers = new Dictionary<string, Server>();
                    if (!string.IsNullOrEmpty(lastGroup))
                    {
                        for (var i = config.configs.Count - 1; i >= 0; --i)
                        {
                            if (lastGroup == config.configs[i].group)
                            {
                                old_servers[config.configs[i].id] = config.configs[i];
                            }
                        }
                    }
                    foreach (var url in urls)
                    {
                        try
                        {
                            var server = new Server(url, curGroup);
                            var match = false;
                            if (!match)
                            {
                                foreach (var pair in old_insert_servers)
                                {
                                    if (server.isMatchServer(pair.Value))
                                    {
                                        match = true;
                                        break;
                                    }
                                }
                            }
                            old_insert_servers[server.id] = server;
                            if (!match)
                            {
                                foreach (var pair in old_servers)
                                {
                                    if (server.isMatchServer(pair.Value))
                                    {
                                        match = true;
                                        old_servers.Remove(pair.Key);
                                        pair.Value.CopyServerInfo(server);
                                        ++count;
                                        break;
                                    }
                                }
                            }
                            if (!match)
                            {
                                var insert_index = config.configs.Count;
                                for (var index = config.configs.Count - 1; index >= 0; --index)
                                {
                                    if (config.configs[index].group == curGroup)
                                    {
                                        insert_index = index + 1;
                                        break;
                                    }
                                }
                                config.configs.Insert(insert_index, server);
                                ++count;
                            }
                        }
                        catch
                        {
                        }
                    }
                    foreach (var pair in old_servers)
                    {
                        for (var i = config.configs.Count - 1; i >= 0; --i)
                        {
                            if (config.configs[i].id == pair.Key)
                            {
                                config.configs.RemoveAt(i);
                                break;
                            }
                        }
                    }
                    controller.SaveServersConfig(config);
                    config = controller.GetCurrentConfiguration();
                    if (selected_server != null)
                    {
                        var match = false;
                        for (var i = config.configs.Count - 1; i >= 0; --i)
                        {
                            if (config.configs[i].id == selected_server.id)
                            {
                                config.index = i;
                                match = true;
                                break;
                            }
                            if (config.configs[i].group == selected_server.group && config.configs[i].isMatchServer(selected_server))
                            {
                                config.index = i;
                                match = true;
                                break;
                            }
                        }
                        if (!match)
                        {
                            config.index = config.configs.Count - 1;
                        }
                    }
                    else
                    {
                        config.index = config.configs.Count - 1;
                    }
                    if (count > 0)
                    {
                        foreach (var subscribe in config.serverSubscribes)
                        {
                            if (subscribe.URL == updateFreeNodeChecker.subscribeTask.URL)
                            {
                                subscribe.LastUpdateTime = (ulong)Math.Floor(DateTime.Now.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds);
                            }
                        }
                    }
                    controller.SaveServersConfig(config);
                }
            }

            if (count > 0)
            {
                if (updateFreeNodeChecker.noitify)
                    ShowBalloonTip(I18N.GetString("Success"),
                        string.Format(I18N.GetString("Update subscribe {0} success"), lastGroup), ToolTipIcon.Info, 10000);
            }
            else
            {
                lastGroup ??= updateFreeNodeChecker.subscribeTask.Group;
                ShowBalloonTip(I18N.GetString("Error"),
                    string.Format(I18N.GetString("Update subscribe {0} failure"), lastGroup), ToolTipIcon.Info, 10000);
            }
        }

        void updateChecker_NewVersionFound()
        {
            if (string.IsNullOrEmpty(updateChecker.LatestVersionNumber))
            {
                Logging.Log(LogLevel.Error, "connect to update server error");
            }
            else
            {
                if (!UpdateItem.Visible)
                {
                    ShowBalloonTip(string.Format(I18N.GetString("{0} {1} Update Found"), UpdateChecker.Name, updateChecker.LatestVersionNumber),
                        I18N.GetString("Click menu to download"), ToolTipIcon.Info, 10000);
                    _notifyIcon.BalloonTipClicked += notifyIcon1_BalloonTipClicked;

                    timerDelayCheckUpdate.Tick -= timer_Elapsed;
                    timerDelayCheckUpdate.Stop();
                    timerDelayCheckUpdate = null;
                }
                UpdateItem.Visible = true;
                UpdateItem.Text = string.Format(I18N.GetString("New version {0} {1} available"), UpdateChecker.Name, updateChecker.LatestVersionNumber);
            }
        }

        void UpdateItem_Clicked(object sender, EventArgs e)
        {
            Process.Start(updateChecker.LatestVersionURL);
        }

        void notifyIcon1_BalloonTipClicked(object sender, EventArgs e)
        {
            //System.Diagnostics.Process.Start(updateChecker.LatestVersionURL);
            _notifyIcon.BalloonTipClicked -= notifyIcon1_BalloonTipClicked;
        }

        private void UpdateSysProxyMode(Configuration config)
        {
            noModifyItem.Checked = config.sysProxyMode == (int)ProxyMode.NoModify;
            enableItem.Checked = config.sysProxyMode == (int)ProxyMode.Direct;
            PACModeItem.Checked = config.sysProxyMode == (int)ProxyMode.Pac;
            globalModeItem.Checked = config.sysProxyMode == (int)ProxyMode.Global;
        }

        private void UpdateProxyRule(Configuration config)
        {
            ruleDisableBypass.Checked = config.proxyRuleMode == (int)ProxyRuleMode.Disable;
            ruleBypassLan.Checked = config.proxyRuleMode == (int)ProxyRuleMode.BypassLan;
            ruleBypassChina.Checked = config.proxyRuleMode == (int)ProxyRuleMode.BypassLanAndChina;
            ruleBypassNotChina.Checked = config.proxyRuleMode == (int)ProxyRuleMode.BypassLanAndNotChina;
            ruleUser.Checked = config.proxyRuleMode == (int)ProxyRuleMode.UserCustom;
        }

        private void LoadCurrentConfiguration()
        {
            var config = controller.GetCurrentConfiguration();
            UpdateServersMenu();
            UpdateSysProxyMode(config);

            UpdateProxyRule(config);

            SelectRandomItem.Checked = config.random;
            sameHostForSameTargetItem.Checked = config.sameHostForSameTarget;
        }

        private void UpdateServersMenu()
        {
            while (ServersItem.DropDownItems[0] != SeperatorItem)
            {
                ServersItem.DropDownItems.RemoveAt(0);
            }

            var configuration = controller.GetCurrentConfiguration();
            var group = new SortedDictionary<string, ToolStripMenuItem>();
            const string def_group = "!(no group)";
            var select_group = "";
            for (var i = 0; i < configuration.configs.Count; i++)
            {
                string group_name;
                var server = configuration.configs[i];
                group_name = string.IsNullOrEmpty(server.group) ? def_group : server.group;

                var item = new ToolStripMenuItem(server.FriendlyName()) { Tag = i };
                item.Click += AServerItem_Click;
                if (configuration.index == i)
                {
                    item.Checked = true;
                    select_group = group_name;
                }

                if (group.TryGetValue(group_name, out var value))
                {
                    value.DropDownItems.Add(item);
                }
                else
                {
                    group[group_name] = new ToolStripMenuItem(group_name, null, item);
                }
            }

            var index = 0;
            foreach (var pair in group)
            {
                if (pair.Key == def_group)
                {
                    pair.Value.Text = "(empty group)";
                }
                if (pair.Key == select_group)
                {
                    pair.Value.Text = $"\u25cf {pair.Value.Text}";
                }
                else
                {
                    pair.Value.Text = $"\u3000{pair.Value.Text}";
                }

                ServersItem.DropDownItems.Insert(index++, pair.Value);
            }
        }

        private void ShowConfigForm(bool addNode)
        {
            if (configForm != null)
            {
                configForm.Activate();
                if (addNode)
                {
                    var cfg = controller.GetCurrentConfiguration();
                    configForm.SetServerListSelectedIndex(cfg.index + 1);
                }
            }
            else
            {
                configfrom_open = true;
                configForm = new ConfigForm(controller, updateChecker, addNode ? -1 : -2);
                configForm.Show();
                configForm.Activate();
                configForm.BringToFront();
                configForm.FormClosed += configForm_FormClosed;
            }
        }

        private void ShowConfigForm(int index)
        {
            if (configForm != null)
            {
                configForm.Activate();
            }
            else
            {
                configfrom_open = true;
                configForm = new ConfigForm(controller, updateChecker, index);
                configForm.Show();
                configForm.Activate();
                configForm.BringToFront();
                configForm.FormClosed += configForm_FormClosed;
            }
        }

        private void ShowSettingForm()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
            }
            else
            {
                settingsForm = new SettingsForm(controller);
                settingsForm.Show();
                settingsForm.Activate();
                settingsForm.BringToFront();
                settingsForm.FormClosed += settingsForm_FormClosed;
            }
        }

        private void ShowPortMapForm()
        {
            if (portMapForm != null)
            {
                portMapForm.Activate();
                portMapForm.Update();
                if (portMapForm.WindowState == FormWindowState.Minimized)
                {
                    portMapForm.WindowState = FormWindowState.Normal;
                }
            }
            else
            {
                portMapForm = new PortSettingsForm(controller);
                portMapForm.Show();
                portMapForm.Activate();
                portMapForm.BringToFront();
                portMapForm.FormClosed += portMapForm_FormClosed;
            }
        }

        private void ShowServerLogForm()
        {
            if (serverLogForm != null)
            {
                serverLogForm.Activate();
                serverLogForm.Update();
                if (serverLogForm.WindowState == FormWindowState.Minimized)
                {
                    serverLogForm.WindowState = FormWindowState.Normal;
                }
            }
            else
            {
                serverLogForm = new ServerLogForm(controller);
                serverLogForm.Show();
                serverLogForm.Activate();
                serverLogForm.BringToFront();
                serverLogForm.FormClosed += serverLogForm_FormClosed;
            }
        }

        private void ShowGlobalLogForm()
        {
            if (logForm != null)
            {
                logForm.Activate();
                logForm.Update();
                if (logForm.WindowState == FormWindowState.Minimized)
                {
                    logForm.WindowState = FormWindowState.Normal;
                }
            }
            else
            {
                logForm = new LogForm(controller);
                logForm.Show();
                logForm.Activate();
                logForm.BringToFront();
                logForm.FormClosed += globalLogForm_FormClosed;
            }
        }

        private void ShowSubscribeSettingForm()
        {
            if (subScribeForm != null)
            {
                subScribeForm.Activate();
                subScribeForm.Update();
                if (subScribeForm.WindowState == FormWindowState.Minimized)
                {
                    subScribeForm.WindowState = FormWindowState.Normal;
                }
            }
            else
            {
                subScribeForm = new SubscribeForm(controller);
                subScribeForm.Show();
                subScribeForm.Activate();
                subScribeForm.BringToFront();
                subScribeForm.FormClosed += subScribeForm_FormClosed;
            }
        }

        void configForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            configForm = null;
            configfrom_open = false;
            Util.Utils.ReleaseMemory();
            for (var index = 0; index < eventList; index++)
            {
                updateFreeNodeChecker_NewFreeNodeFound();
            }
        }

        void settingsForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            settingsForm = null;
            Util.Utils.ReleaseMemory();
        }

        void serverLogForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            serverLogForm = null;
            Util.Utils.ReleaseMemory();
        }

        void portMapForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            portMapForm = null;
            Util.Utils.ReleaseMemory();
        }

        void globalLogForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            logForm = null;
            Util.Utils.ReleaseMemory();
        }

        void subScribeForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            subScribeForm = null;
        }

        private void Config_Click(object sender, EventArgs e) => ShowConfigForm(false);

        private void Import_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog();
            dlg.InitialDirectory = Application.StartupPath;
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var name = dlg.FileName;
                var cfg = Configuration.LoadFile(name);
                if (cfg == null || cfg.configs.Count == 1 && cfg.configs[0].server == Configuration.GetDefaultServer().server)
                {
                    MessageBox.Show("Load config file failed", "ShadowsocksR");
                }
                else
                {
                    controller.MergeConfiguration(cfg);
                    LoadCurrentConfiguration();
                }
            }
        }

        private void Setting_Click(object sender, EventArgs e)
        {
            ShowSettingForm();
        }

        private void Quit_Click(object sender, EventArgs e)
        {
            controller.Stop();
            if (configForm != null)
            {
                configForm.Close();
                configForm = null;
            }
            if (serverLogForm != null)
            {
                serverLogForm.Close();
                serverLogForm = null;
            }
            if (timerDelayCheckUpdate != null)
            {
                timerDelayCheckUpdate.Tick -= timer_Elapsed;
                timerDelayCheckUpdate.Stop();
                timerDelayCheckUpdate = null;
            }
            _notifyIcon.Visible = false;
            Application.Exit();
        }

        private void OpenWiki_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/shadowsocksrr/shadowsocks-rss/wiki") { UseShellExecute = true });
        }

        private void FeedbackItem_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/shadowsocksrr/shadowsocksr-csharp/issues/new") { UseShellExecute = true });
        }

        private void ResetPasswordItem_Click(object sender, EventArgs e)
        {
            var dlg = new ResetPassword();
            dlg.Show();
            dlg.Activate();
        }

        private void AboutItem_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://breakwa11.github.io") { UseShellExecute = true });
        }

        private void DonateItem_Click(object sender, EventArgs e)
        {
            ShowBalloonTip(I18N.GetString("Donate"), I18N.GetString("Please contract to breakwa11 to get more infomation"), ToolTipIcon.Info, 10000);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(Keys vKey);

        private void notifyIcon1_Click(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                var SCA_key = GetAsyncKeyState(Keys.ShiftKey) < 0 ? 1 : 0;
                SCA_key |= GetAsyncKeyState(Keys.ControlKey) < 0 ? 2 : 0;
                SCA_key |= GetAsyncKeyState(Keys.Menu) < 0 ? 4 : 0;
                if (SCA_key == 2)
                {
                    ShowServerLogForm();
                }
                else if (SCA_key == 1)
                {
                    ShowSettingForm();
                }
                else if (SCA_key == 4)
                {
                    ShowPortMapForm();
                }
                else
                {
                    ShowConfigForm(false);
                }
            }
            else if (e.Button == MouseButtons.Middle)
            {
                ShowServerLogForm();
            }
        }

        private void NoModifyItem_Click(object sender, EventArgs e)
        {
            controller.ToggleMode(ProxyMode.NoModify);
        }

        private void EnableItem_Click(object sender, EventArgs e)
        {
            controller.ToggleMode(ProxyMode.Direct);
        }

        private void GlobalModeItem_Click(object sender, EventArgs e)
        {
            controller.ToggleMode(ProxyMode.Global);
        }

        private void PACModeItem_Click(object sender, EventArgs e)
        {
            controller.ToggleMode(ProxyMode.Pac);
        }

        private void RuleBypassLanItem_Click(object sender, EventArgs e)
        {
            controller.ToggleRuleMode((int)ProxyRuleMode.BypassLan);
        }

        private void RuleBypassChinaItem_Click(object sender, EventArgs e)
        {
            controller.ToggleRuleMode((int)ProxyRuleMode.BypassLanAndChina);
        }

        private void RuleBypassNotChinaItem_Click(object sender, EventArgs e)
        {
            controller.ToggleRuleMode((int)ProxyRuleMode.BypassLanAndNotChina);
        }

        private void RuleUserItem_Click(object sender, EventArgs e)
        {
            controller.ToggleRuleMode((int)ProxyRuleMode.UserCustom);
        }

        private void RuleBypassDisableItem_Click(object sender, EventArgs e)
        {
            controller.ToggleRuleMode((int)ProxyRuleMode.Disable);
        }

        private void SelectRandomItem_Click(object sender, EventArgs e)
        {
            SelectRandomItem.Checked = !SelectRandomItem.Checked;
            controller.ToggleSelectRandom(SelectRandomItem.Checked);
        }

        private void SelectSameHostForSameTargetItem_Click(object sender, EventArgs e)
        {
            sameHostForSameTargetItem.Checked = !sameHostForSameTargetItem.Checked;
            controller.ToggleSameHostForSameTargetRandom(sameHostForSameTargetItem.Checked);
        }

        private void CopyPACURLItem_Click(object sender, EventArgs e)
        {
            try
            {
                var config = controller.GetCurrentConfiguration();
                var pacUrl = $"http://127.0.0.1:{config.localPort}/pac?auth={config.localAuthPassword}&t={Util.Utils.GetTimestamp(DateTime.Now)}";
                Clipboard.SetText(pacUrl);
            }
            catch
            {

            }
        }

        private void EditPACFileItem_Click(object sender, EventArgs e)
        {
            controller.TouchPACFile();
        }

        private void UpdatePACFromGFWListItem_Click(object sender, EventArgs e)
        {
            controller.UpdatePACFromGFWList();
        }

        private async void UpdatePACFromLanIPListItem_Click(object sender, EventArgs e) =>
            await controller.UpdatePACFromOnlinePac("https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/ss_lanip.pac");

        private async void UpdatePACFromCNWhiteListItem_Click(object sender, EventArgs e) =>
            await controller.UpdatePACFromOnlinePac("https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/ss_white.pac");

        private async void UpdatePACFromCNOnlyListItem_Click(object sender, EventArgs e) =>
            await controller.UpdatePACFromOnlinePac("https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/ss_white_r.pac");

        private async void UpdatePACFromCNIPListItem_Click(object sender, EventArgs e) =>
            await controller.UpdatePACFromOnlinePac("https://raw.githubusercontent.com/shadowsocksrr/breakwa11.github.io/master/ssr/ss_cnip.pac");

        private void EditUserRuleFileForGFWListItem_Click(object sender, EventArgs e)
        {
            controller.TouchUserRuleFile();
        }

        private void AServerItem_Click(object sender, EventArgs e)
        {
            var config = controller.GetCurrentConfiguration();
            Console.WriteLine($"config.checkSwitchAutoCloseAll:{config.checkSwitchAutoCloseAll}");
            if (config.checkSwitchAutoCloseAll)
            {
                controller.DisconnectAllConnections();
            }
            var item = (ToolStripMenuItem)sender;
            controller.SelectServerIndex((int)item.Tag);
        }

        private async void CheckUpdate_Click(object sender, EventArgs e) =>
            await updateChecker.CheckUpdate(controller.GetCurrentConfiguration());

        private async void CheckNodeUpdate_Click(object sender, EventArgs e) =>
            await updateSubscribeManager.CreateTask(controller.GetCurrentConfiguration(), updateFreeNodeChecker, true, true);

        private async void CheckNodeUpdateBypassProxy_Click(object sender, EventArgs e) =>
            await updateSubscribeManager.CreateTask(controller.GetCurrentConfiguration(), updateFreeNodeChecker, false, true);

        private void ShowLogItem_Click(object sender, EventArgs e)
        {
            ShowGlobalLogForm();
        }

        private void ShowPortMapItem_Click(object sender, EventArgs e)
        {
            ShowPortMapForm();
        }

        private void ShowServerLogItem_Click(object sender, EventArgs e)
        {
            ShowServerLogForm();
        }

        private void SubscribeSetting_Click(object sender, EventArgs e)
        {
            ShowSubscribeSettingForm();
        }

        private void DisconnectCurrent_Click(object sender, EventArgs e)
        {
            controller.DisconnectAllConnections();
        }

        private void URL_Split(string text, ref List<string> out_urls)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            var ss_index = text.IndexOf("ss://", 1, StringComparison.OrdinalIgnoreCase);
            var ssr_index = text.IndexOf("ssr://", 1, StringComparison.OrdinalIgnoreCase);
            var index = ss_index;
            if (index == -1 || index > ssr_index && ssr_index != -1) index = ssr_index;
            if (index == -1)
            {
                out_urls.Insert(0, text);
            }
            else
            {
                out_urls.Insert(0, text[..index]);
                URL_Split(text[index..], ref out_urls);
            }
        }

        private void CopyAddress_Click(object sender, EventArgs e)
        {
            try
            {
                var iData = Clipboard.GetDataObject();
                if (iData.GetDataPresent(DataFormats.Text))
                {
                    var urls = new List<string>();
                    URL_Split((string)iData.GetData(DataFormats.Text), ref urls);
                    var count = 0;
                    foreach (var url in urls)
                    {
                        if (controller.AddServerBySSURL(url))
                            ++count;
                    }
                    if (count > 0)
                        ShowConfigForm(true);
                }
            }
            catch
            {

            }
        }

        private bool ScanQRCode(Screen screen, Bitmap fullImage, Rectangle cropRect, out string url, out Rectangle rect)
        {
            using (var target = new Bitmap(cropRect.Width, cropRect.Height))
            {
                using (var g = Graphics.FromImage(target))
                {
                    g.DrawImage(fullImage, new Rectangle(0, 0, cropRect.Width, cropRect.Height),
                                    cropRect,
                                    GraphicsUnit.Pixel);
                }
                var source = new BitmapLuminanceSource(target);
                var bitmap = new BinaryBitmap(new HybridBinarizer(source));
                var reader = new QRCodeReader();
                var result = reader.decode(bitmap);
                if (result != null)
                {
                    url = result.Text;
                    double minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
                    foreach (var point in result.ResultPoints)
                    {
                        minX = Math.Min(minX, point.X);
                        minY = Math.Min(minY, point.Y);
                        maxX = Math.Max(maxX, point.X);
                        maxY = Math.Max(maxY, point.Y);
                    }
                    //rect = new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
                    rect = new Rectangle(cropRect.Left + (int)minX, cropRect.Top + (int)minY, (int)(maxX - minX), (int)(maxY - minY));
                    return true;
                }
            }
            url = "";
            rect = new Rectangle();
            return false;
        }

        private bool ScanQRCodeStretch(Screen screen, Bitmap fullImage, Rectangle cropRect, double mul, out string url, out Rectangle rect)
        {
            using (var target = new Bitmap((int)(cropRect.Width * mul), (int)(cropRect.Height * mul)))
            {
                using (var g = Graphics.FromImage(target))
                {
                    g.DrawImage(fullImage, new Rectangle(0, 0, target.Width, target.Height),
                                    cropRect,
                                    GraphicsUnit.Pixel);
                }
                var source = new BitmapLuminanceSource(target);
                var bitmap = new BinaryBitmap(new HybridBinarizer(source));
                var reader = new QRCodeReader();
                var result = reader.decode(bitmap);
                if (result != null)
                {
                    url = result.Text;
                    double minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
                    foreach (var point in result.ResultPoints)
                    {
                        minX = Math.Min(minX, point.X);
                        minY = Math.Min(minY, point.Y);
                        maxX = Math.Max(maxX, point.X);
                        maxY = Math.Max(maxY, point.Y);
                    }
                    //rect = new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
                    rect = new Rectangle(cropRect.Left + (int)(minX / mul), cropRect.Top + (int)(minY / mul), (int)((maxX - minX) / mul), (int)((maxY - minY) / mul));
                    return true;
                }
            }
            url = "";
            rect = new Rectangle();
            return false;
        }

        private Rectangle GetScanRect(int width, int height, int index, out double stretch)
        {
            stretch = 1;
            if (index < 5)
            {
                const int div = 5;
                var w = width * 3 / div;
                var h = height * 3 / div;
                var pt = new Point[5] {
                    new(1, 1),

                    new(0, 0),
                    new(0, 2),
                    new(2, 0),
                    new(2, 2),
                };
                return new Rectangle(pt[index].X * width / div, pt[index].Y * height / div, w, h);
            }
            {
                const int base_index = 5;
                if (index < base_index + 6)
                {
                    var s = new double[] {
                        1,
                        2,
                        3,
                        4,
                        6,
                        8
                    };
                    stretch = 1 / s[index - base_index];
                    return new Rectangle(0, 0, width, height);
                }
            }
            {
                const int base_index = 11;
                if (index < base_index + 8)
                {
                    const int hdiv = 7;
                    const int vdiv = 5;
                    var w = width * 3 / hdiv;
                    var h = height * 3 / vdiv;
                    var pt = new Point[8] {
                        new(1, 1),
                        new(3, 1),

                        new(0, 0),
                        new(0, 2),

                        new(2, 0),
                        new(2, 2),

                        new(4, 0),
                        new(4, 2),
                    };
                    return new Rectangle(pt[index - base_index].X * width / hdiv, pt[index - base_index].Y * height / vdiv, w, h);
                }
            }
            return new Rectangle(0, 0, 0, 0);
        }

        private void ScanScreenQRCode(bool ss_only)
        {
            Thread.Sleep(100);
            foreach (var screen in Screen.AllScreens)
            {
                var screen_size = Util.Utils.GetScreenPhysicalSize();
                using var fullImage = new Bitmap(screen_size.X,
                    screen_size.Y);
                using (var g = Graphics.FromImage(fullImage))
                {
                    g.CopyFromScreen(screen.Bounds.X,
                        screen.Bounds.Y,
                        0, 0,
                        fullImage.Size,
                        CopyPixelOperation.SourceCopy);
                }
                var decode_fail = false;
                for (var i = 0; i < 100; i++)
                {
                    var cropRect = GetScanRect(fullImage.Width, fullImage.Height, i, out var stretch);
                    if (cropRect.Width == 0)
                        break;

                    if (stretch == 1 ? ScanQRCode(screen, fullImage, cropRect, out var url, out var rect) : ScanQRCodeStretch(screen, fullImage, cropRect, stretch, out url, out rect))
                    {
                        var success = controller.AddServerBySSURL(url);
                        var splash = new QRCodeSplashForm();
                        if (success)
                        {
                            splash.FormClosed += splash_FormClosed;
                        }
                        else if (!ss_only)
                        {
                            _urlToOpen = url;
                            //if (url.StartsWith("http://") || url.StartsWith("https://"))
                            //    splash.FormClosed += openURLFromQRCode;
                            //else
                            splash.FormClosed += showURLFromQRCode;
                        }
                        else
                        {
                            decode_fail = true;
                            continue;
                        }
                        splash.Location = new Point(screen.Bounds.X, screen.Bounds.Y);
                        var dpi = Screen.PrimaryScreen.Bounds.Width / (double)screen_size.X;
                        splash.TargetRect = new Rectangle(
                            (int)(rect.Left * dpi + screen.Bounds.X),
                            (int)(rect.Top * dpi + screen.Bounds.Y),
                            (int)(rect.Width * dpi),
                            (int)(rect.Height * dpi));
                        splash.Size = new Size(fullImage.Width, fullImage.Height);
                        splash.Show();
                        return;
                    }
                }
                if (decode_fail)
                {
                    MessageBox.Show(I18N.GetString("Failed to decode QRCode"));
                    return;
                }
            }
            MessageBox.Show(I18N.GetString("No QRCode found. Try to zoom in or move it to the center of the screen."));
        }

        private void ScanQRCodeItem_Click(object sender, EventArgs e)
        {
            ScanScreenQRCode(false);
        }

        void splash_FormClosed(object sender, FormClosedEventArgs e)
        {
            ShowConfigForm(true);
        }

        void openURLFromQRCode(object sender, FormClosedEventArgs e)
        {
            Process.Start(_urlToOpen);
        }

        void showURLFromQRCode()
        {
            var dlg = new ShowTextForm("QRCode", _urlToOpen);
            dlg.Show();
            dlg.Activate();
            dlg.BringToFront();
        }

        void showURLFromQRCode(object sender, FormClosedEventArgs e)
        {
            showURLFromQRCode();
        }

        void showURLFromQRCode(object sender, EventArgs e)
        {
            showURLFromQRCode();
        }
    }
}
