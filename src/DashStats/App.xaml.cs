using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DashStats.Collectors;

namespace DashStats;

public partial class App : Application
{
    Mutex? _mutex;
    bool _ownsMutex;
    Collector? _collector;
    Tray? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => { Log.Write(ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write(ex.ExceptionObject);

        // One instance at a time. Wait briefly so a relaunch (install/update) can take over cleanly.
        _mutex = new Mutex(false, @"Global\DashStats.Instance");
        try { _ownsMutex = _mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            MessageBox.Show("DashStats is already running — look for its icon in the system tray.", "DashStats");
            Shutdown();
            return;
        }

        var s = Settings.Load();
        var result = s.FirstRunDone ? Setup.OfferUpdate(s) : Setup.FirstRun(s);
        if (result != Setup.Result.Continue)
        {
            if (result == Setup.Result.Relaunch)
            {
                ReleaseMutex();
                Process.Start(new ProcessStartInfo(Setup.InstalledExe) { UseShellExecute = true, WorkingDirectory = Setup.InstallDir });
            }
            Shutdown();
            return;
        }
        if (s.StartWithWindows && Setup.IsInstalledCopy && !Setup.StartupRegistered()) Setup.SetStartup(true);

        using (var me = Process.GetCurrentProcess()) me.PriorityClass = ProcessPriorityClass.BelowNormal;
        // A once-a-second text panel doesn't need the GPU. Software rendering keeps the graphics driver
        // (tens of MB) out of our process and never wakes a sleeping laptop dGPU.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        var ctl = new Controller(s);
        var win = new MainWindow(ctl);
        ctl.Win = win;
        win.Show();
        _tray = new Tray(ctl);

        _collector = new Collector(s);
        _collector.Updated += (rows, metrics) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ctl.OnTick(rows, metrics));
        _collector.Start();
        Updater.Start(ctl);

        // Nothing picked yet (fresh install, or upgrading from before presets): ask what to monitor.
        // Dev hook: an "open-setup.flag" file in the settings folder opens setup once, since the elevated app
        // can't be clicked by non-admin automation (screenshots, tests).
        var flag = Path.Combine(Setup.DataDir, "open-setup.flag");
        bool forced = File.Exists(flag);
        if (forced) try { File.Delete(flag); } catch { }
        if (s.Uses.Count == 0 || forced) ctl.OpenSetup();
    }

    /// <summary>Hands over to another copy of the exe (after an update): it waits on our mutex, so release that first.</summary>
    public void Restart(string exe)
    {
        _collector?.Dispose();
        _collector = null;
        ReleaseMutex();
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        Shutdown();
    }

    void ReleaseMutex()
    {
        if (!_ownsMutex) return;
        _mutex?.ReleaseMutex();
        _ownsMutex = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _collector?.Dispose();
        _tray?.Dispose();
        ReleaseMutex();
        base.OnExit(e);
    }
}
