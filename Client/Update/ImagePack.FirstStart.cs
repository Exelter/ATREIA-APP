using System.Windows;
using System.Windows.Controls;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Update;

/// <summary>The first start's download window (WPF); the download itself is in ImagePack.cs.</summary>
public static partial class ImagePack
{
    /// <summary>
    /// The first start (no pack yet, and none came with the build): the pictures are downloaded before the meter's window opens, in a small window with
    /// the progress and a Skip button. Offline, or when the website has no pack, the window closes by itself and the app starts without pictures.
    /// </summary>
    public static void FirstStart()
    {
        if (GameArt.Bundled || ReadMarker(Current) is not null)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        // not disposed here: the download's last steps can still look at it after the window closed
        var cancel = new CancellationTokenSource();
        var text = new TextBlock { Text = loc["Images.Fetching"], TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, IsIndeterminate = true, Margin = new Thickness(0, 14, 0, 6) };
        var amount = new TextBlock { FontSize = 12, Text = " " };
        amount.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var skip = new Button
        {
            Content = loc["Images.Skip"], MinWidth = 84, Height = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true,
        };
        var panel = new StackPanel { Width = 380, Margin = new Thickness(18, 16, 18, 16) };
        panel.Children.Add(text);
        panel.Children.Add(bar);
        panel.Children.Add(amount);
        panel.Children.Add(skip);
        var window = new Window
        {
            Title = Brand.AppName, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        ThemedChrome.Apply(window);
        window.ResizeMode = ResizeMode.NoResize;

        bool closed = false;
        void Close()
        {
            if (!closed)
            {
                window.Close();
            }
        }

        window.Closed += (_, _) =>
        {
            closed = true;
            cancel.Cancel();
        };
        skip.Click += (_, _) =>
        {
            Log("skipped by the player");
            Close();
        };
        window.Loaded += async (_, _) =>
        {
            try
            {
                PackIndex? index = await LatestAsync(cancel.Token);
                if (index is not null)
                {
                    bar.IsIndeterminate = false;
                    void Show(long got, long total)
                    {
                        bar.Value = total > 0 ? Math.Min(1, got / (double)total) : 0;
                        amount.Text = string.Format(loc["Images.Progress"], got / 1048576.0, total / 1048576.0);
                    }

                    bool fetched = await FetchAsync(index, (got, total) => window.Dispatcher.InvokeAsync(() => Show(got, total)), cancel.Token);
                    if (fetched)
                    {
                        ApplyWaiting();
                    }
                }
            }
            catch (Exception ex)
            {
                Log("the first download failed: " + ex.Message);
            }

            Close();
        };
        window.ShowDialog();

        // This was the app's first window, so WPF made it the app's main window: the meter's window, created next, has to take that place itself (the
        // other windows take their styles from it, and closing it ends the app).
        try
        {
            if (Application.Current is { } app && ReferenceEquals(app.MainWindow, window))
            {
                app.MainWindow = null;
            }
        }
        catch (InvalidOperationException)
        {
            // Program sets the meter's window as the main window right after creating it anyway
        }
    }
}
