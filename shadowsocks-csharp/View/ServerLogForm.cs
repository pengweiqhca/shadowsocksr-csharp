using Shadowsocks.Controller;
using Shadowsocks.Model;
using Shadowsocks.Properties;
using System.ComponentModel;

namespace Shadowsocks.View;

public partial class ServerLogForm : Form
{
    private class DoubleBufferListView : DataGridView
    {
        public DoubleBufferListView()
        {
            SetStyle(ControlStyles.DoubleBuffer
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.AllPaintingInWmPaint
                , true);
            UpdateStyles();
        }
    }

    private readonly ShadowsocksController controller;

    //private ContextMenuStrip contextMenu1;
    private readonly ToolStripMenuItem topmostItem;
    private readonly ToolStripMenuItem clearItem;
    private readonly List<int> listOrder = new();
    private int lastRefreshIndex;
    private bool firstDispley = true;
    private bool rowChange;
    private int updatePause;
    private int updateTick;
    private int updateSize;
    private int pendingUpdate;
    private readonly string title_perfix = "";
    private ServerSpeedLogShow[] ServerSpeedLogList;
    private Thread workerThread;
    private readonly AutoResetEvent workerEvent = new(false);

    public ServerLogForm(ShadowsocksController controller)
    {
        this.controller = controller;
        try
        {
            Icon = Icon.FromHandle(new Bitmap("icon.png").GetHicon());
            title_perfix = Application.StartupPath;
            if (title_perfix.Length > 20)
                title_perfix = title_perfix[..20];
        }
        catch
        {
            Icon = Icon.FromHandle(Resources.ssw128.GetHicon());
        }
        Font = SystemFonts.MessageBoxFont;
        InitializeComponent();

        Width = 810;
        var dpi_mul = Util.Utils.GetDpiMul();

        var config = controller.GetCurrentConfiguration();
        Height = config.configs.Count switch
        {
            < 8 => 300 * dpi_mul / 4,
            < 20 => (300 + (config.configs.Count - 8) * 16) * dpi_mul / 4,
            _ => 500 * dpi_mul / 4
        };
        UpdateTexts();
        UpdateLog();

        Controls.Add(MainMenuStrip = new MenuStrip());
        MainMenuStrip.Items.AddRange(new ToolStripItem[]
        {
            CreateMenuGroup("&Control",
                CreateMenuItem("&Disconnect direct connections", DisconnectForward_Click),
                CreateMenuItem("Disconnect &All", Disconnect_Click),
                new ToolStripSeparator(),
                CreateMenuItem("Clear &MaxSpeed", ClearMaxSpeed_Click),
                clearItem = CreateMenuItem("&Clear", ClearItem_Click),
                new ToolStripSeparator(),
                CreateMenuItem("Clear &Selected Total", ClearSelectedTotal_Click),
                CreateMenuItem("Clear &Total", ClearTotal_Click)),
            CreateMenuGroup("Port &out",
                CreateMenuItem("Copy current link", copyLinkItem_Click),
                CreateMenuItem("Copy current group links", copyGroupLinkItem_Click),
                CreateMenuItem("Copy all enable links", copyEnableLinksItem_Click),
                CreateMenuItem("Copy all links", copyLinksItem_Click)),
            CreateMenuGroup("&Window",
                CreateMenuItem("Auto &size", autosizeItem_Click),
                topmostItem = CreateMenuItem("Always On &Top", topmostItem_Click)),
        });
        controller.ConfigChanged += UpdateTitle;

        for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
        {
            ServerDataGrid.Columns[i].Width = ServerDataGrid.Columns[i].Width * dpi_mul / 4;
        }

        ServerDataGrid.RowTemplate.Height = 20 * dpi_mul / 4;
        //ServerDataGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        var width = 0;
        for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
        {
            if (!ServerDataGrid.Columns[i].Visible)
                continue;
            width += ServerDataGrid.Columns[i].Width;
        }
        Width = width + SystemInformation.VerticalScrollBarWidth + (Width - ClientSize.Width) + 1;
        ServerDataGrid.AutoResizeColumnHeadersHeight();
    }
    private ToolStripMenuItem CreateMenuGroup(string text, params ToolStripItem[] dropDownItems) => new(I18N.GetString(text), null, dropDownItems);

    private ToolStripMenuItem CreateMenuItem(string text, EventHandler click) => new(I18N.GetString(text), null, click);

    private void UpdateTitle()
    {
        Text = $@"{title_perfix}{I18N.GetString("ServerLog")}({(controller.GetCurrentConfiguration().shareOverLan ? "any" : "local")}:{controller.GetCurrentConfiguration().localPort}({Model.Server.GetForwardServerRef().GetConnections().Count}) {I18N.GetString("Version")}{UpdateChecker.FullVersion})";
    }
    private void UpdateTexts()
    {
        UpdateTitle();
        for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
        {
            ServerDataGrid.Columns[i].HeaderText = I18N.GetString(ServerDataGrid.Columns[i].HeaderText);
        }
    }

    private string FormatBytes(long bytes)
    {
        const long K = 1024L;
        const long M = K * 1024L;
        const long G = M * 1024L;
        const long T = G * 1024L;
        const long P = T * 1024L;
        const long E = P * 1024L;

        if (bytes >= M * 990)
        {
            return bytes switch
            {
                >= G * 990 => bytes switch
                {
                    >= P * 990 => $"{bytes / (double)E:F3}E",
                    >= T * 990 => $"{bytes / (double)P:F3}P",
                    _ => $"{bytes / (double)T:F3}T"
                },
                >= G * 99 => $"{bytes / (double)G:F2}G",
                >= G * 9 => $"{bytes / (double)G:F3}G",
                _ => $"{bytes / (double)G:F4}G"
            };
        }
        if (bytes >= K * 990)
        {
            if (bytes >= M * 100)
                return $"{bytes / (double)M:F1}M";
            if (bytes > M * 9.9)
                return $"{bytes / (double)M:F2}M";
            return $"{bytes / (double)M:F3}M";
        }
        if (bytes > K * 99)
            return $"{bytes / (double)K:F0}K";
        if (bytes > 900)
            return $"{bytes / (double)K:F1}K";
        return bytes.ToString();
    }

    public bool SetBackColor(DataGridViewCell cell, Color newColor)
    {
        if (cell.Style.BackColor != newColor)
        {
            cell.Style.BackColor = newColor;
            rowChange = true;
            return true;
        }
        return false;
    }
    public bool SetCellToolTipText(DataGridViewCell cell, string newString)
    {
        if (cell.ToolTipText != newString)
        {
            cell.ToolTipText = newString;
            rowChange = true;
            return true;
        }
        return false;
    }
    public bool SetCellText(DataGridViewCell cell, string newString)
    {
        if ((string)cell.Value != newString)
        {
            cell.Value = newString;
            rowChange = true;
            return true;
        }
        return false;
    }
    public bool SetCellText(DataGridViewCell cell, long newInteger)
    {
        if ((string)cell.Value != newInteger.ToString())
        {
            cell.Value = newInteger.ToString();
            rowChange = true;
            return true;
        }
        return false;
    }
    private byte ColorMix(byte a, byte b, double alpha) => (byte)(b * alpha + a * (1 - alpha));
    private Color ColorMix(Color a, Color b, double alpha) =>
        Color.FromArgb(ColorMix(a.R, b.R, alpha),
            ColorMix(a.G, b.G, alpha),
            ColorMix(a.B, b.B, alpha));
    public void UpdateLogThread()
    {
        while (workerThread != null)
        {
            var config = controller.GetCurrentConfiguration();
            var _ServerSpeedLogList = new ServerSpeedLogShow[config.configs.Count];
            for (var i = 0; i < config.configs.Count && i < _ServerSpeedLogList.Length; ++i)
            {
                _ServerSpeedLogList[i] = config.configs[i].ServerSpeedLog().Translate();
            }
            ServerSpeedLogList = _ServerSpeedLogList;

            workerEvent.WaitOne();
        }
    }
    public void UpdateLog()
    {
        if (workerThread == null)
        {
            workerThread = new Thread(UpdateLogThread);
            workerThread.Start();
        }
        else
        {
            workerEvent.Set();
        }
    }
    public void RefreshLog()
    {
        if (ServerSpeedLogList == null)
            return;

        var last_rowcount = ServerDataGrid.RowCount;
        var config = controller.GetCurrentConfiguration();
        if (listOrder.Count > config.configs.Count)
        {
            listOrder.RemoveRange(config.configs.Count, listOrder.Count - config.configs.Count);
        }
        while (listOrder.Count < config.configs.Count)
        {
            listOrder.Add(0);
        }
        while (ServerDataGrid.RowCount < config.configs.Count && ServerDataGrid.RowCount < ServerSpeedLogList.Length)
        {
            ServerDataGrid.Rows.Add();
            var id = ServerDataGrid.RowCount - 1;
            ServerDataGrid[0, id].Value = id;
        }
        if (ServerDataGrid.RowCount > config.configs.Count)
        {
            for (var list_index = 0; list_index < ServerDataGrid.RowCount; ++list_index)
            {
                var id_cell = ServerDataGrid[0, list_index];
                var id = (int)id_cell.Value;
                if (id >= config.configs.Count)
                {
                    ServerDataGrid.Rows.RemoveAt(list_index);
                    --list_index;
                }
            }
        }
        var displayBeginIndex = ServerDataGrid.FirstDisplayedScrollingRowIndex;
        var displayEndIndex = displayBeginIndex + ServerDataGrid.DisplayedRowCount(true);
        try
        {
            for (int list_index = lastRefreshIndex >= ServerDataGrid.RowCount ? 0 : lastRefreshIndex, rowChangeCnt = 0;
                 list_index < ServerDataGrid.RowCount && rowChangeCnt <= 100;
                 ++list_index)
            {
                lastRefreshIndex = list_index + 1;

                var id_cell = ServerDataGrid[0, list_index];
                var id = (int)id_cell.Value;
                var server = config.configs[id];
                var serverSpeedLog = ServerSpeedLogList[id];
                listOrder[id] = list_index;
                rowChange = false;
                for (var curcol = 0; curcol < ServerDataGrid.Columns.Count; ++curcol)
                {
                    if (!firstDispley &&
                        (ServerDataGrid.SortedColumn == null || ServerDataGrid.SortedColumn.Index != curcol) &&
                        (list_index < displayBeginIndex || list_index >= displayEndIndex)) continue;

                    var cell = ServerDataGrid[curcol, list_index];
                    switch (ServerDataGrid.Columns[curcol].Name)
                    {
                        case "Server":
                            SetBackColor(cell, config.index == id ? Color.Cyan : Color.White);
                            SetCellText(cell, server.FriendlyName());
                            break;
                        case "Group":
                            SetCellText(cell, server.group);
                            break;
                        case "Enable":
                            SetBackColor(cell, server.isEnable() ? Color.White : Color.Red);
                            break;
                        case "TotalConnect":
                            SetCellText(cell, serverSpeedLog.totalConnectTimes);
                            break;
                        case "Connecting":
                        {
                            var connections = serverSpeedLog.totalConnectTimes - serverSpeedLog.totalDisconnectTimes;
                            var colList = new[]
                            {
                                Color.White, Color.LightGreen, Color.Yellow, Color.Red, Color.Red
                            };
                            var bytesList = new[]
                            {
                                0L, 16, 32, 64, 65536
                            };
                            for (var i = 1; i < colList.Length; ++i)
                            {
                                if (connections < bytesList[i])
                                {
                                    SetBackColor(cell, ColorMix(colList[i - 1], colList[i],
                                        (double)(connections - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])));
                                    break;
                                }
                            }
                            SetCellText(cell, serverSpeedLog.totalConnectTimes - serverSpeedLog.totalDisconnectTimes);
                            break;
                        }
                        case "AvgLatency" when serverSpeedLog.avgConnectTime >= 0:
                            SetCellText(cell, serverSpeedLog.avgConnectTime / 1000);
                            break;
                        case "AvgLatency":
                            SetCellText(cell, "-");
                            break;
                        case "AvgDownSpeed":
                        {
                            var avgBytes = serverSpeedLog.avgDownloadBytes;
                            var valStr = FormatBytes(avgBytes);
                            var colList = new[]
                            {
                                Color.White, Color.LightGreen, Color.Yellow, Color.Pink, Color.Red, Color.Red
                            };
                            var bytesList = new[]
                            {
                                0, 1024 * 64, 1024 * 512, 1024 * 1024 * 4, 1024 * 1024 * 16, 1024L * 1024 * 1024 * 1024
                            };
                            for (var i = 1; i < colList.Length; ++i)
                            {
                                if (avgBytes < bytesList[i])
                                {
                                    SetBackColor(cell, ColorMix(colList[i - 1], colList[i],
                                        (double)(avgBytes - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])));
                                    break;
                                }
                            }
                            SetCellText(cell, valStr);
                            break;
                        }
                        case "MaxDownSpeed":
                        {
                            var maxBytes = serverSpeedLog.maxDownloadBytes;
                            var valStr = FormatBytes(maxBytes);
                            var colList = new[]
                            {
                                Color.White, Color.LightGreen, Color.Yellow, Color.Pink, Color.Red, Color.Red
                            };
                            var bytesList = new[]
                            {
                                0L, 1024 * 64, 1024 * 512, 1024 * 1024 * 4, 1024 * 1024 * 16, 1024 * 1024 * 1024
                            };
                            for (var i = 1; i < colList.Length; ++i)
                            {
                                if (maxBytes < bytesList[i])
                                {
                                    SetBackColor(cell, ColorMix(colList[i - 1], colList[i],
                                        (double)(maxBytes - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])));
                                    break;
                                }
                            }
                            SetCellText(cell, valStr);
                            break;
                        }
                        case "AvgUpSpeed":
                        {
                            var avgBytes = serverSpeedLog.avgUploadBytes;
                            var valStr = FormatBytes(avgBytes);
                            var colList = new[]
                            {
                                Color.White, Color.LightGreen, Color.Yellow, Color.Pink, Color.Red, Color.Red
                            };
                            var bytesList = new[]
                            {
                                0, 1024 * 64, 1024 * 512, 1024 * 1024 * 4, 1024 * 1024 * 16, 1024L * 1024 * 1024 * 1024
                            };
                            for (var i = 1; i < colList.Length; ++i)
                            {
                                if (avgBytes < bytesList[i])
                                {
                                    SetBackColor(cell, ColorMix(colList[i - 1], colList[i],
                                        (double)(avgBytes - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])));
                                    break;
                                }
                            }
                            SetCellText(cell, valStr);
                            break;
                        }
                        case "MaxUpSpeed":
                        {
                            var maxBytes = serverSpeedLog.maxUploadBytes;
                            var valStr = FormatBytes(maxBytes);
                            var colList = new[]
                            {
                                Color.White, Color.LightGreen, Color.Yellow, Color.Pink, Color.Red, Color.Red
                            };
                            var bytesList = new[]
                            {
                                0L, 1024 * 64, 1024 * 512, 1024 * 1024 * 4, 1024 * 1024 * 16, 1024 * 1024 * 1024
                            };
                            for (var i = 1; i < colList.Length; ++i)
                            {
                                if (maxBytes < bytesList[i])
                                {
                                    SetBackColor(cell, ColorMix(colList[i - 1], colList[i],
                                        (double)(maxBytes - bytesList[i - 1]) / (bytesList[i] - bytesList[i - 1])));
                                    break;
                                }
                            }
                            SetCellText(cell, valStr);
                            break;
                        }
                        case "Upload":
                        {
                            var valStr = FormatBytes(serverSpeedLog.totalUploadBytes);
                            var fullVal = serverSpeedLog.totalUploadBytes.ToString();
                            if (cell.ToolTipText != fullVal)
                            {
                                if (fullVal == "0")
                                    SetBackColor(cell, Color.FromArgb(0xf4, 0xff, 0xf4));
                                else
                                {
                                    SetBackColor(cell, Color.LightGreen);
                                    cell.Tag = 8;
                                }
                            }
                            else if (cell.Tag != null)
                            {
                                cell.Tag = (int)cell.Tag - 1;
                                if ((int)cell.Tag == 0) SetBackColor(cell, Color.FromArgb(0xf4, 0xff, 0xf4));
                            }
                            SetCellToolTipText(cell, fullVal);
                            SetCellText(cell, valStr);
                            break;
                        }
                        case "Download":
                        {
                            var valStr = FormatBytes(serverSpeedLog.totalDownloadBytes);
                            var fullVal = serverSpeedLog.totalDownloadBytes.ToString();
                            if (cell.ToolTipText != fullVal)
                            {
                                if (fullVal == "0")
                                    SetBackColor(cell, Color.FromArgb(0xff, 0xf0, 0xf0));
                                else
                                {
                                    SetBackColor(cell, Color.LightGreen);
                                    cell.Tag = 8;
                                }
                            }
                            else if (cell.Tag != null)
                            {
                                cell.Tag = (int)cell.Tag - 1;
                                if ((int)cell.Tag == 0) SetBackColor(cell, Color.FromArgb(0xff, 0xf0, 0xf0));
                            }
                            SetCellToolTipText(cell, fullVal);
                            SetCellText(cell, valStr);
                            break;
                        }
                        case "DownloadRaw":
                        {
                            var valStr = FormatBytes(serverSpeedLog.totalDownloadRawBytes);
                            var fullVal = serverSpeedLog.totalDownloadRawBytes.ToString();
                            if (cell.ToolTipText != fullVal)
                            {
                                if (fullVal == "0")
                                    SetBackColor(cell, Color.FromArgb(0xff, 0x80, 0x80));
                                else
                                {
                                    SetBackColor(cell, Color.LightGreen);
                                    cell.Tag = 8;
                                }
                            }
                            else if (cell.Tag != null)
                            {
                                cell.Tag = (int)cell.Tag - 1;
                                if ((int)cell.Tag == 0)
                                {
                                    SetBackColor(cell, fullVal == "0" ? Color.FromArgb(0xff, 0x80, 0x80) : Color.FromArgb(0xf0, 0xf0, 0xff));
                                }
                                //Color col = cell.Style.BackColor;
                                //SetBackColor(cell, Color.FromArgb(Math.Min(255, col.R + colAdd), Math.Min(255, col.G + colAdd), Math.Min(255, col.B + colAdd)));
                            }
                            SetCellToolTipText(cell, fullVal);
                            SetCellText(cell, valStr);
                            break;
                        }
                        case "ConnectError":
                        {
                            var val = serverSpeedLog.errorConnectTimes + serverSpeedLog.errorDecodeTimes;
                            var col = Color.FromArgb(255, (byte)Math.Max(0, 255 - val * 2.5), (byte)Math.Max(0, 255 - val * 2.5));
                            SetBackColor(cell, col);
                            SetCellText(cell, val);
                            break;
                        }
                        case "ConnectTimeout":
                            SetCellText(cell, serverSpeedLog.errorTimeoutTimes);
                            break;
                        case "ConnectEmpty":
                        {
                            var val = serverSpeedLog.errorEmptyTimes;
                            var col = Color.FromArgb(255, (byte)Math.Max(0, 255 - val * 8), (byte)Math.Max(0, 255 - val * 8));
                            SetBackColor(cell, col);
                            SetCellText(cell, val);
                            break;
                        }
                        case "Continuous":
                        {
                            var val = serverSpeedLog.errorContinurousTimes;
                            var col = Color.FromArgb(255, (byte)Math.Max(0, 255 - val * 8), (byte)Math.Max(0, 255 - val * 8));
                            SetBackColor(cell, col);
                            SetCellText(cell, val);
                            break;
                        }
                        case "ErrorPercent" when serverSpeedLog.errorLogTimes + serverSpeedLog.totalConnectTimes - serverSpeedLog.totalDisconnectTimes > 0:
                        {
                            var percent = (serverSpeedLog.errorConnectTimes
                                           + serverSpeedLog.errorTimeoutTimes
                                           + serverSpeedLog.errorDecodeTimes)
                                          * 100.00
                                          / (serverSpeedLog.errorLogTimes + serverSpeedLog.totalConnectTimes - serverSpeedLog.totalDisconnectTimes);
                            SetBackColor(cell, Color.FromArgb(255, (byte)(255 - percent * 2), (byte)(255 - percent * 2)));
                            SetCellText(cell, $"{percent:F0}%");
                            break;
                        }
                        case "ErrorPercent":
                            SetBackColor(cell, Color.White);
                            SetCellText(cell, "-");
                            break;
                    }
                }

                if (rowChange && list_index >= displayBeginIndex && list_index < displayEndIndex)
                    rowChangeCnt++;
            }
        }
        catch
        {

        }
        UpdateTitle();
        if (ServerDataGrid.SortedColumn != null)
        {
            ServerDataGrid.Sort(ServerDataGrid.SortedColumn, (ListSortDirection)((int)ServerDataGrid.SortOrder - 1));
        }
        if (last_rowcount == 0 && config.index >= 0 && config.index < ServerDataGrid.RowCount)
        {
            ServerDataGrid[0, config.index].Selected = true;
        }
        if (firstDispley)
        {
            ServerDataGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, config.index - ServerDataGrid.DisplayedRowCount(true) / 2);
            firstDispley = false;
        }
    }

    private void autosizeColumns()
    {
        for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
        {
            var name = ServerDataGrid.Columns[i].Name;
            if (name is "AvgLatency" or "AvgDownSpeed" or "MaxDownSpeed" or "AvgUpSpeed" or "MaxUpSpeed" or "Upload" or "Download" or "DownloadRaw" or "Group" or "Connecting" or "ErrorPercent" or "ConnectError" or "ConnectTimeout" or "Continuous" or "ConnectEmpty"
               )
            {
                if (ServerDataGrid.Columns[i].Width <= 2)
                    continue;
                ServerDataGrid.AutoResizeColumn(i, DataGridViewAutoSizeColumnMode.AllCellsExceptHeader);
                if (name is "AvgLatency" or "Connecting" or "AvgDownSpeed" or "MaxDownSpeed" or "AvgUpSpeed" or "MaxUpSpeed"
                   )
                {
                    ServerDataGrid.Columns[i].MinimumWidth = ServerDataGrid.Columns[i].Width;
                }
            }
        }
        var width = 0;
        for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
        {
            if (!ServerDataGrid.Columns[i].Visible)
                continue;
            width += ServerDataGrid.Columns[i].Width;
        }
        Width = width + SystemInformation.VerticalScrollBarWidth + (Width - ClientSize.Width) + 1;
        ServerDataGrid.AutoResizeColumnHeadersHeight();
    }

    private void autosizeItem_Click(object sender, EventArgs e)
    {
        autosizeColumns();
    }

    private void copyLinkItem_Click(object sender, EventArgs e)
    {
        var config = controller.GetCurrentConfiguration();
        if (config.index >= 0 && config.index < config.configs.Count)
        {
            try
            {
                var link = config.configs[config.index].GetSSRLinkForServer();
                Clipboard.SetText(link);
            }
            catch
            {
            }
        }
    }

    private void copyGroupLinkItem_Click(object sender, EventArgs e)
    {
        var config = controller.GetCurrentConfiguration();
        if (config.index >= 0 && config.index < config.configs.Count)
        {
            var group = config.configs[config.index].group;
            var link = config.configs.Where(t => t.group == group).Aggregate("", (current, t) => current + $"{t.GetSSRLinkForServer()}\r\n");
            try
            {
                Clipboard.SetText(link);
            }
            catch
            {
            }
        }
    }

    private void copyEnableLinksItem_Click(object sender, EventArgs e)
    {
        var config = controller.GetCurrentConfiguration();
        var link = config.configs.Where(t => t.enable).Aggregate("", (current, t) => current + $"{t.GetSSRLinkForServer()}\r\n");
        try
        {
            Clipboard.SetText(link);
        }
        catch
        {
        }
    }

    private void copyLinksItem_Click(object sender, EventArgs e)
    {
        var config = controller.GetCurrentConfiguration();
        var link = config.configs.Aggregate("", (current, t) => current + $"{t.GetSSRLinkForServer()}\r\n");
        try
        {
            Clipboard.SetText(link);
        }
        catch
        {
        }
    }

    private void topmostItem_Click(object sender, EventArgs e)
    {
        topmostItem.Checked = !topmostItem.Checked;
        TopMost = topmostItem.Checked;
    }

    private void DisconnectForward_Click(object sender, EventArgs e)
    {
        Model.Server.GetForwardServerRef().GetConnections().CloseAll();
    }

    private void Disconnect_Click(object sender, EventArgs e)
    {
        controller.DisconnectAllConnections();
        Model.Server.GetForwardServerRef().GetConnections().CloseAll();
    }

    private void ClearMaxSpeed_Click(object sender, EventArgs e)
    {
        var config = controller.GetCurrentConfiguration();
        foreach (var server in config.configs)
        {
            server.ServerSpeedLog().ClearMaxSpeed();
        }
    }

    private void ClearSelectedTotal_Click(object sender, EventArgs e)
    {
        var config = controller.GetCurrentConfiguration();
        if (config.index >= 0 && config.index < config.configs.Count)
        {
            try
            {
                controller.ClearTransferTotal(config.configs[config.index].server);
            }
            catch
            {
            }
        }
    }

    private void ClearTotal_Click(object sender, EventArgs e)
    {
        var config = controller.GetCurrentConfiguration();
        foreach (var server in config.configs)
        {
            controller.ClearTransferTotal(server.server);
        }
    }

    private void ClearItem_Click(object sender, EventArgs e)
    {
        var config = controller.GetCurrentConfiguration();
        foreach (var server in config.configs)
        {
            server.ServerSpeedLog().Clear();
        }
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (updatePause > 0)
        {
            updatePause -= 1;
            return;
        }
        if (WindowState == FormWindowState.Minimized)
        {
            if (++pendingUpdate < 40)
            {
                return;
            }
        }
        else
        {
            ++updateTick;
        }
        pendingUpdate = 0;
        RefreshLog();
        UpdateLog();
        if (updateSize > 1) --updateSize;
        if (updateTick == 2 || updateSize == 1)
        {
            updateSize = 0;
            //autosizeColumns();
        }
    }

    private void ServerDataGrid_MouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

        int row_index = -1, col_index = -1;
        if (ServerDataGrid.SelectedCells.Count > 0)
        {
            row_index = ServerDataGrid.SelectedCells[0].RowIndex;
            col_index = ServerDataGrid.SelectedCells[0].ColumnIndex;
        }
        if (row_index >= 0)
        {
            var id = (int)ServerDataGrid[0, row_index].Value;
            switch (ServerDataGrid.Columns[col_index].Name)
            {
                case "Server":
                    controller.SelectServerIndex(id);
                    break;
                case "Group":
                {
                    var config = controller.GetCurrentConfiguration();
                    var cur_server = config.configs[id];
                    var group = cur_server.group;
                    if (!string.IsNullOrEmpty(group))
                    {
                        var enable = !cur_server.enable;
                        foreach (var server in config.configs.Where(server => server.group == group && server.enable != enable))
                        {
                            server.setEnable(enable);
                        }
                        controller.SelectServerIndex(config.index);
                    }
                    break;
                }
                case "Enable":
                {
                    var config = controller.GetCurrentConfiguration();
                    var server = config.configs[id];
                    server.setEnable(!server.isEnable());
                    controller.SelectServerIndex(config.index);
                    break;
                }
            }
            ServerDataGrid[0, row_index].Selected = true;
        }
    }

    private void ServerDataGrid_CellClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var id = (int)ServerDataGrid[0, e.RowIndex].Value;
        switch (ServerDataGrid.Columns[e.ColumnIndex].Name)
        {
            case "Server":
            {
                var config = controller.GetCurrentConfiguration();
                if (config.checkSwitchAutoCloseAll)
                {
                    controller.DisconnectAllConnections();
                }
                controller.SelectServerIndex(id);
                break;
            }
            case "Group":
            {
                var config = controller.GetCurrentConfiguration();
                var cur_server = config.configs[id];
                var group = cur_server.group;
                if (!string.IsNullOrEmpty(group))
                {
                    var enable = !cur_server.enable;
                    foreach (var server in config.configs.Where(server => server.group == group && server.enable != enable))
                    {
                        server.setEnable(enable);
                    }
                    controller.SelectServerIndex(config.index);
                }
                break;
            }
            case "Enable":
            {
                var config = controller.GetCurrentConfiguration();
                var server = config.configs[id];
                server.setEnable(!server.isEnable());
                controller.SelectServerIndex(config.index);
                break;
            }
        }
        ServerDataGrid[0, e.RowIndex].Selected = true;
    }

    private void ServerDataGrid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;

        var id = (int)ServerDataGrid[0, e.RowIndex].Value;
        if (ServerDataGrid.Columns[e.ColumnIndex].Name == "ID")
        {
            controller.ShowConfigForm(id);
        }
        if (ServerDataGrid.Columns[e.ColumnIndex].Name == "Server")
        {
            controller.ShowConfigForm(id);
        }
        if (ServerDataGrid.Columns[e.ColumnIndex].Name == "Connecting")
        {
            var config = controller.GetCurrentConfiguration();
            var server = config.configs[id];
            server.GetConnections().CloseAll();
        }
        switch (ServerDataGrid.Columns[e.ColumnIndex].Name)
        {
            case "MaxDownSpeed" or "MaxUpSpeed":
            {
                var config = controller.GetCurrentConfiguration();
                config.configs[id].ServerSpeedLog().ClearMaxSpeed();
                break;
            }
            case "Upload" or "Download":
            {
                var config = controller.GetCurrentConfiguration();
                config.configs[id].ServerSpeedLog().ClearTrans();
                break;
            }
            case "DownloadRaw":
            {
                var config = controller.GetCurrentConfiguration();
                config.configs[id].ServerSpeedLog().Clear();
                config.configs[id].setEnable(true);
                break;
            }
            case "ConnectError" or "ConnectTimeout" or "ConnectEmpty" or "Continuous":
            {
                var config = controller.GetCurrentConfiguration();
                config.configs[id].ServerSpeedLog().ClearError();
                config.configs[id].setEnable(true);
                break;
            }
        }
        ServerDataGrid[0, e.RowIndex].Selected = true;
    }

    private void ServerLogForm_FormClosed(object sender, FormClosedEventArgs e)
    {
        controller.ConfigChanged -= UpdateTitle;
        var thread = workerThread;
        workerThread = null;
        while (thread.IsAlive)
        {
            workerEvent.Set();
            Thread.Sleep(50);
        }
    }

    private long Str2Long(string str)
    {
        if (str == "-") return -1;
        //if (String.IsNullOrEmpty(str)) return -1;
        if (str.LastIndexOf('K') > 0)
        {
            var ret = Convert.ToDouble(str[..str.LastIndexOf('K')]);
            return (long)(ret * 1024);
        }
        if (str.LastIndexOf('M') > 0)
        {
            var ret = Convert.ToDouble(str[..str.LastIndexOf('M')]);
            return (long)(ret * 1024 * 1024);
        }
        if (str.LastIndexOf('G') > 0)
        {
            var ret = Convert.ToDouble(str[..str.LastIndexOf('G')]);
            return (long)(ret * 1024 * 1024 * 1024);
        }
        if (str.LastIndexOf('T') > 0)
        {
            var ret = Convert.ToDouble(str[..str.LastIndexOf('T')]);
            return (long)(ret * 1024 * 1024 * 1024 * 1024);
        }
        try
        {
            var ret = Convert.ToDouble(str);
            return (long)ret;
        }
        catch
        {
            return -1;
        }
    }

    private void ServerDataGrid_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
    {
        //e.SortResult = 0;
        if (e.Column.Name is "Server" or "Group")
        {
            e.SortResult = string.Compare(Convert.ToString(e.CellValue1), Convert.ToString(e.CellValue2));
            e.Handled = true;
        }
        else if (e.Column.Name is "ID" or "TotalConnect" or "Connecting" or "ConnectError" or "ConnectTimeout" or "Continuous"
                )
        {
            var v1 = Convert.ToInt32(e.CellValue1);
            var v2 = Convert.ToInt32(e.CellValue2);
            e.SortResult = v1 == v2 ? 0 : v1 < v2 ? -1 : 1;
        }
        else if (e.Column.Name == "ErrorPercent")
        {
            var s1 = Convert.ToString(e.CellValue1);
            var s2 = Convert.ToString(e.CellValue2);
            var v1 = s1.Length <= 1 ? 0 : Convert.ToInt32(Convert.ToDouble(s1[..^1]) * 100);
            var v2 = s2.Length <= 1 ? 0 : Convert.ToInt32(Convert.ToDouble(s2[..^1]) * 100);
            e.SortResult = v1 == v2 ? 0 : v1 < v2 ? -1 : 1;
        }
        else if (e.Column.Name is "AvgLatency" or "AvgDownSpeed" or "MaxDownSpeed" or "AvgUpSpeed" or "MaxUpSpeed" or "Upload" or "Download" or "DownloadRaw"
                )
        {
            var s1 = Convert.ToString(e.CellValue1);
            var s2 = Convert.ToString(e.CellValue2);
            var v1 = Str2Long(s1);
            var v2 = Str2Long(s2);
            e.SortResult = v1 == v2 ? 0 : v1 < v2 ? -1 : 1;
        }
        if (e.SortResult == 0)
        {
            var v1 = listOrder[Convert.ToInt32(ServerDataGrid[0, e.RowIndex1].Value)];
            var v2 = listOrder[Convert.ToInt32(ServerDataGrid[0, e.RowIndex2].Value)];
            e.SortResult = v1 == v2 ? 0 : v1 < v2 ? -1 : 1;
            if (e.SortResult != 0 && ServerDataGrid.SortOrder == SortOrder.Descending)
            {
                e.SortResult = -e.SortResult;
            }
        }
        if (e.SortResult != 0)
        {
            e.Handled = true;
        }
    }

    private void ServerLogForm_Move(object sender, EventArgs e)
    {
        updatePause = 0;
    }

    protected override void WndProc(ref Message message)
    {
        const int WM_SIZING = 532;
        //const int WM_SIZE = 533;
        const int WM_MOVING = 534;
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MINIMIZE = 0xF020;
        switch (message.Msg)
        {
            case WM_SIZING:
            case WM_MOVING:
                updatePause = 2;
                break;
            case WM_SYSCOMMAND:
                if ((int)message.WParam == SC_MINIMIZE)
                {
                    Util.Utils.ReleaseMemory();
                }
                break;
        }
        base.WndProc(ref message);
    }

    private void ServerLogForm_ResizeEnd(object sender, EventArgs e)
    {
        updatePause = 0;

        var width = 0;
        for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
        {
            if (!ServerDataGrid.Columns[i].Visible)
                continue;
            width += ServerDataGrid.Columns[i].Width;
        }
        width += SystemInformation.VerticalScrollBarWidth + (Width - ClientSize.Width) + 1;
        ServerDataGrid.Columns[2].Width += Width - width;
    }

    private void ServerDataGrid_ColumnWidthChanged(object sender, DataGridViewColumnEventArgs e)
    {
        var width = 0;
        for (var i = 0; i < ServerDataGrid.Columns.Count; ++i)
        {
            if (!ServerDataGrid.Columns[i].Visible)
                continue;
            width += ServerDataGrid.Columns[i].Width;
        }
        Width = width + SystemInformation.VerticalScrollBarWidth + (Width - ClientSize.Width) + 1;
        ServerDataGrid.AutoResizeColumnHeadersHeight();
    }
}