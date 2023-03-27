using Shadowsocks.Controller;
using Microsoft.Win32;
using Shadowsocks.Model;
using Shadowsocks.View;

namespace Shadowsocks;

internal static class Program
{
    private static ShadowsocksController _controller;
    private static MenuViewController _viewController;

    /// <summary>
    /// 应用程序的主入口点。
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(arg => arg == "--setautorun"))
        {
            if (!AutoStartup.Switch())
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        using var mutex = new Mutex(false, $"Global\\ShadowsocksR_{Application.StartupPath.GetHashCode()}");

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.ApplicationExit += Application_ApplicationExit;
        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        Application.SetCompatibleTextRenderingDefault(false);

        if (!mutex.WaitOne(0, false))
        {
            MessageBox.Show($"{I18N.GetString("Find Shadowsocks icon in your notify tray.")}\n{I18N.GetString("If you want to start multiple Shadowsocks, make a copy in another directory.")}",
                I18N.GetString("ShadowsocksR is already running."));
            return;
        }

        Directory.SetCurrentDirectory(Application.StartupPath);

        var try_times = 0;
        while (Configuration.Load() == null)
        {
            if (try_times >= 5)
                return;
            using (var dlg = new InputPassword())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                    Configuration.SetPassword(dlg.password);
                else
                    return;
            }
            try_times += 1;
        }
        if (try_times > 0)
            Logging.save_to_file = false;

        _controller = new ShadowsocksController();
        HostMap.Instance().LoadHostFile();

        // Logging
        var cfg = _controller.GetConfiguration();
        Logging.save_to_file = cfg.logEnable;

        //#if !DEBUG
        Logging.OpenLogFile();
        //#endif

        _viewController = new MenuViewController(_controller);

        _controller.Start();

        Application.Run();
    }

    private static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Resume:
                if (_controller != null)
                {
                    var timer = new System.Timers.Timer(5 * 1000);
                    timer.Elapsed += Timer_Elapsed;
                    timer.AutoReset = false;
                    timer.Enabled = true;
                    timer.Start();
                }
                break;
            case PowerModes.Suspend:
                _controller?.Stop();
                break;
        }
    }

    private static void Timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            _controller?.Start();
        }
        catch (Exception ex)
        {
            Logging.LogUsefulException(ex);
        }
        finally
        {
            try
            {
                var timer = (System.Timers.Timer)sender;
                timer.Enabled = false;
                timer.Stop();
                timer.Dispose();
            }
            catch (Exception ex)
            {
                Logging.LogUsefulException(ex);
            }
        }
    }

    private static void Application_ApplicationExit(object sender, EventArgs e)
    {
        _controller?.Stop();
        _controller = null;
    }

    private static int exited;
    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (Interlocked.Increment(ref exited) == 1)
        {
            Logging.Log(LogLevel.Error, e.ExceptionObject != null ? e.ExceptionObject.ToString() : "");
            MessageBox.Show(I18N.GetString("Unexpected error, ShadowsocksR will exit.") +
                            Environment.NewLine + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : ""),
                "Shadowsocks Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Exit();
        }
    }
}