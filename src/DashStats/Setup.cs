using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace DashStats;

/// <summary>First-run install, start-with-Windows, bundled tools and uninstall.</summary>
static class Setup
{
    public const string TaskName = "DashStats";

    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DashStats");
    static readonly string LocalDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DashStats");
    public static readonly string InstallDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "DashStats");
    static readonly string Shortcut =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DashStats.lnk");

    public static string InstalledExe => Path.Combine(InstallDir, "DashStats.exe");
    public static string ExePath => Environment.ProcessPath!;
    public static bool IsInstalledCopy =>
        string.Equals(Path.GetFullPath(ExePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public enum Result { Continue, Relaunch, Quit }

    public static bool PawnIoInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        return key is not null;
    }

    /// <summary>Writes a bundled tool next to our data (once per version) and returns its path.</summary>
    public static string Extract(string name)
    {
        var dir = Path.Combine(LocalDir, "bin");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                        ?? throw new FileNotFoundException("Missing bundled " + name);
        if (File.Exists(path) && new FileInfo(path).Length == res.Length) return path;
        using var f = File.Create(path);
        res.CopyTo(f);
        return path;
    }

    static bool InstallPawnIo()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Extract("PawnIO_setup.exe"), "-install -silent")
                { UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit(120_000);
        }
        catch (Exception e) { Log.Write(e); }
        return PawnIoInstalled();
    }

    public static Result FirstRun(Settings s)
    {
        var answer = MessageBox.Show(
            "Welcome to DashStats!\n\n" +
            "Set it up on this PC? This will:\n" +
            "  •  install the PawnIO sensor driver (needed for CPU temperatures)\n" +
            "  •  install DashStats to your user Programs folder\n" +
            "  •  start it automatically when you sign in\n" +
            "  •  add a Start menu shortcut\n\n" +
            "Yes = set up     No = just run it from here     Cancel = quit",
            "DashStats setup", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel) return Result.Quit;

        if (answer == MessageBoxResult.No)
        {
            if (!PawnIoInstalled() && MessageBox.Show(
                    "Install the PawnIO sensor driver? Without it CPU temperature and power can't be read.",
                    "DashStats", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                InstallPawnIo();
            s.FirstRunDone = true;
            s.Save();
            return Result.Continue;
        }

        if (!PawnIoInstalled() && !InstallPawnIo())
            MessageBox.Show("The PawnIO driver didn't install, so CPU temperatures may be missing. Everything else will work.",
                "DashStats", MessageBoxButton.OK, MessageBoxImage.Warning);

        bool relaunch = CopyToInstallDir();
        CreateShortcut();
        s.StartWithWindows = SetStartup(true, InstalledExe);
        s.FirstRunDone = true;
        s.Save();
        return relaunch ? Result.Relaunch : Result.Continue;
    }

    /// <summary>Run from somewhere else while an installed copy exists: offer to replace it with this build.</summary>
    public static Result OfferUpdate()
    {
        if (IsInstalledCopy || !File.Exists(InstalledExe)) return Result.Continue;
        var mine = FileVersionInfo.GetVersionInfo(ExePath);
        var theirs = FileVersionInfo.GetVersionInfo(InstalledExe);
        if (new FileInfo(ExePath).Length == new FileInfo(InstalledExe).Length && mine.FileVersion == theirs.FileVersion)
            return Result.Continue;

        var answer = MessageBox.Show(
            $"DashStats {theirs.FileVersion} is installed on this PC.\n\nReplace it with this copy ({mine.FileVersion})?",
            "DashStats", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return Result.Continue;
        return CopyToInstallDir() ? Result.Relaunch : Result.Continue;
    }

    static bool CopyToInstallDir()
    {
        if (IsInstalledCopy) return false;
        Directory.CreateDirectory(InstallDir);
        File.Copy(ExePath, InstalledExe, overwrite: true);
        return true;
    }

    static void CreateShortcut()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic lnk = shell.CreateShortcut(Shortcut);
            lnk.TargetPath = InstalledExe;
            lnk.WorkingDirectory = InstallDir;
            lnk.Description = "DashStats system monitor";
            lnk.Save();
        }
        catch (Exception e) { Log.Write(e); }
    }

    public static bool StartupRegistered() => Run("schtasks", $"/Query /TN {TaskName}") == 0;

    /// <summary>
    /// A scheduled task (not the Startup folder) so it can start elevated without a UAC prompt at every sign-in.
    /// Returns whether start-with-Windows is now enabled.
    /// </summary>
    public static bool SetStartup(bool on, string? exe = null)
    {
        if (!on)
        {
            Run("schtasks", $"/Delete /TN {TaskName} /F");
            return false;
        }

        exe ??= ExePath;
        string user = WindowsIdentity.GetCurrent().Name;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>DashStats system monitor</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId><Delay>PT5S</Delay></LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  <WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
        var tmp = Path.Combine(Path.GetTempPath(), "DashStats-task.xml");
        File.WriteAllText(tmp, xml, Encoding.Unicode);
        int code = Run("schtasks", $"/Create /TN {TaskName} /XML \"{tmp}\" /F");
        try { File.Delete(tmp); } catch { }
        if (code != 0) Log.Write($"schtasks /Create failed: {code}");
        return code == 0;
    }

    public static bool Uninstall()
    {
        if (MessageBox.Show(
                "Remove DashStats from this PC?\n\n" +
                "This removes the startup task, Start menu shortcut, installed copy and settings.\n" +
                "The PawnIO driver stays (other hardware tools use it); remove it from Settings › Apps if you like.",
                "Uninstall DashStats", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return false;

        SetStartup(false);
        try { File.Delete(Shortcut); } catch { }
        // We may be running from the install folder; delete once we've exited.
        string cmd = $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{InstallDir}\" & rmdir /s /q \"{LocalDir}\" & rmdir /s /q \"{DataDir}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = false, CreateNoWindow = true });
        return true;
    }

    static int Run(string exe, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            return p.ExitCode;
        }
        catch (Exception e) { Log.Write(e); return -1; }
    }
}
