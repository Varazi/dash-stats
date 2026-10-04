using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;

namespace DashStats;

/// <summary>"Version X is ready": update now, remind me later (short or long), or stop asking.</summary>
sealed class UpdateWindow : Window
{
    static readonly TimeSpan Short = TimeSpan.FromHours(4), Long = TimeSpan.FromDays(7);

    readonly Controller _c;
    readonly Updater.Release _r;
    readonly TextBlock _status;
    readonly Button _now;
    readonly UniformGrid _later;
    CancellationTokenSource? _download;
    bool _chosen;

    public UpdateWindow(Controller c, Updater.Release r)
    {
        _c = c;
        _r = r;
        Title = "DashStats update";
        Background = Ui.Solid("#15181D");
        Foreground = Ui.Text;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        SizeToContent = SizeToContent.Height;
        Width = 460;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        UseLayoutRounding = true;

        var whatsNew = new Hyperlink(new Run("See what's new")) { Foreground = Ui.Solid("#4EA8FF") };
        whatsNew.Click += (_, _) => Process.Start(new ProcessStartInfo(r.PageUrl) { UseShellExecute = true });
        var intro = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(0, 0, 0, 18) };
        intro.Inlines.Add($"You have {Updater.CurrentText}. Updating takes about a minute; the widget restarts by itself. ");
        intro.Inlines.Add(whatsNew);

        _now = Flat("Update now");
        _now.Background = _now.BorderBrush = Ui.Text;
        _now.Foreground = Ui.Solid("#0E1116");
        _now.FontWeight = FontWeights.SemiBold;
        _now.HorizontalContentAlignment = HorizontalAlignment.Center;
        _now.Padding = new Thickness(22, 11, 22, 11);
        _now.IsDefault = true;
        _now.Click += async (_, _) => await UpdateNow();

        _later = new UniformGrid { Columns = 3, Margin = new Thickness(-4, 8, -4, 0) };
        _later.Children.Add(Choice("In 4 hours", () => RemindIn(Short)));
        _later.Children.Add(Choice("In a week", () => RemindIn(Long)));
        _later.Children.Add(Choice("Don't ask again", StopAsking));

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Muted, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };

        var body = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        body.Children.Add(new TextBlock { Text = "DASH STATS · UPDATE", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Faint });
        body.Children.Add(new TextBlock { Text = $"Version {r.Version.ToString(3)} is ready", FontSize = 24, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 6) });
        body.Children.Add(intro);
        body.Children.Add(_now);
        body.Children.Add(new TextBlock { Text = "Remind me later", FontSize = 11.5, Foreground = Ui.Faint, Margin = new Thickness(0, 16, 0, 0) });
        body.Children.Add(_later);
        body.Children.Add(_status);
        body.Children.Add(new TextBlock
        {
            Text = "\"Don't ask again\" stops the automatic check. You can still check any time from the tray icon → Check for updates.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Foreground = Ui.Faint, Margin = new Thickness(0, 14, 0, 0),
        });
        Content = body;

        SourceInitialized += (_, _) => Native.DarkTitleBar(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        // Windows won't let a background app steal focus, so start on top once to make sure it's seen.
        Topmost = true;
        ContentRendered += (_, _) => { Topmost = false; Activate(); };
        Closing += (_, _) =>
        {
            _download?.Cancel();
            if (!_chosen) RemindIn(Short, close: false); // closed with X: treat as the short "later"
        };
    }

    static Button Flat(string text) => new()
    {
        Style = (Style)Application.Current.FindResource("Flat"),
        Content = text,
    };

    Button Choice(string text, Action act)
    {
        var b = Flat(text);
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.Margin = new Thickness(4, 0, 4, 0);
        b.Click += (_, _) => act();
        return b;
    }

    void RemindIn(TimeSpan span, bool close = true)
    {
        _chosen = true;
        _c.S.UpdateRemindAfter = DateTime.Now + span;
        _c.S.Save();
        if (close) Close();
    }

    void StopAsking()
    {
        _chosen = true;
        _c.S.CheckForUpdates = false;
        _c.S.UpdateRemindAfter = null;
        _c.S.Save();
        Close();
    }

    async Task UpdateNow()
    {
        _now.IsEnabled = _later.IsEnabled = false;
        _status.Visibility = Visibility.Visible;
        _status.Foreground = Ui.Muted;
        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p => _status.Text = $"Downloading… {p:P0}");
            var file = await Updater.Download(_r, progress, _download.Token);
            _status.Text = "Installing…";
            _chosen = true;
            Updater.Install(_c, file);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Write(e);
            _chosen = false;
            _status.Text = $"Couldn't update: {e.Message}. Try again later, or download it from the website.";
            _status.Foreground = Ui.ForLevel(Level.Bad);
            _now.IsEnabled = _later.IsEnabled = true;
        }
    }
}
