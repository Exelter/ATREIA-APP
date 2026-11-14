using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AionDPS.Upload;

namespace AionDPS.Ui;

/// <summary>
/// App menu → Atreia: signs the meter in to the player's Discord on atreia.app (see <see cref="Account"/>), shows who it is
/// signed in as, and signs it out. While a sign-in waits, the code is shown here and on the page the browser opened, and the window asks
/// the site every few seconds whether it was approved; closing the window stops asking. Signed in, it also says how the meter's check of the character
/// played went (<see cref="CharacterVerify"/>: "Quake (Nemon) verified"), or how that check works before the first one.
/// Below, "Your legion's own site" links the app to a copy of the website that a legion runs itself (<see cref="OwnSite"/>; the address typed, approved
/// there with Discord the same way), which then gets the character's check and the legion's member list as well (<see cref="OwnSiteShare"/>).
/// </summary>
public sealed class AccountWindow : Window
{
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), FontSize = 13 };
    private readonly TextBlock _code = new() { FontFamily = new FontFamily("Consolas"), FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), FontSize = 12 };
    private readonly TextBlock _verify = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly CheckBox _shareLegion = new() { Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
    private readonly CheckBox _shareProfile = new() { Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };
    private readonly TextBlock _legion = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 0, 0, 14), FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly Button _signIn = new() { Content = "Sign in with Discord", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _openAgain = new() { Content = "Open the page again", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly Button _signOut = new() { Content = "Sign out", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _myFights = new() { Content = "My fights", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly DispatcherTimer _timer = new();
    private Account.LinkStart? _pending;
    private int _failures;

    // a legion's own site (OwnSite), at the bottom: apart from the atreia.app sign-in above, with its own address, code and buttons
    private readonly TextBlock _ownTitle = new() { Text = "Your legion's own site", FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _ownStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), FontSize = 12 };
    private readonly TextBlock _ownCode = new() { FontFamily = new FontFamily("Consolas"), FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    private readonly TextBox _ownAddress = new() { Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13, MaxLength = 200 };
    private readonly TextBlock _ownAddressHint = new() { Text = "Its address, like mylegion.gg", FontSize = 12, IsHitTestVisible = false, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _ownAddressBox = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _ownHint = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontSize = 12 };
    private readonly Button _ownLink = new() { Content = "Link", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _ownOpenAgain = new() { Content = "Open the page again", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly Button _ownOpen = new() { Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly Button _ownCancel = new() { Content = "Cancel", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly Button _ownUnlink = new() { Content = "Unlink", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _ownTimer = new();
    private OwnSite.LinkStart? _ownPending;
    private string? _ownMessage;
    private int _ownFailures;

    public AccountWindow()
    {
        Title = Brand.AppName + " account";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Window");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _verify.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _code.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");

        var buttons = new WrapPanel();
        buttons.Children.Add(_signIn);
        buttons.Children.Add(_openAgain);
        buttons.Children.Add(_myFights);
        buttons.Children.Add(_signOut);
        var root = new StackPanel { Margin = new Thickness(18, 16, 18, 18) };
        root.Children.Add(_status);
        root.Children.Add(_code);
        root.Children.Add(_hint);
        root.Children.Add(_verify);
        root.Children.Add(_shareLegion);
        root.Children.Add(_legion);
        root.Children.Add(_shareProfile);
        root.Children.Add(buttons);
        var rule = new Border { Height = 1, Margin = new Thickness(0, 16, 0, 12) };
        rule.SetResourceReference(Border.BackgroundProperty, "Brush.Border");
        root.Children.Add(rule);
        root.Children.Add(_ownTitle);
        root.Children.Add(_ownStatus);
        root.Children.Add(_ownCode);
        _ownAddressBox.Children.Add(_ownAddress);
        _ownAddressBox.Children.Add(_ownAddressHint);
        root.Children.Add(_ownAddressBox);
        root.Children.Add(_ownHint);
        var ownButtons = new WrapPanel();
        ownButtons.Children.Add(_ownLink);
        ownButtons.Children.Add(_ownOpenAgain);
        ownButtons.Children.Add(_ownOpen);
        ownButtons.Children.Add(_ownCancel);
        ownButtons.Children.Add(_ownUnlink);
        root.Children.Add(ownButtons);
        Content = root;
        ThemedChrome.Apply(this);
        _ownTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _ownStatus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        _ownHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _ownCode.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
        // the address box in the theme's colours, its hint inside while empty (as the Legion page's search)
        _ownAddress.SetResourceReference(Control.BackgroundProperty, "Brush.Control");
        _ownAddress.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        _ownAddress.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        _ownAddress.SetResourceReference(TextBox.CaretBrushProperty, "Brush.Text");
        _ownAddressHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSubtle");
        _ownAddress.TextChanged += (_, _) => _ownAddressHint.Visibility = _ownAddress.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ownAddress.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && _ownLink.IsEnabled)
            {
                e.Handled = true;
                await StartOwnAsync();
            }
        };

        _signIn.Click += async (_, _) => await StartAsync();
        _openAgain.Click += (_, _) => Open(_pending?.Url);
        _myFights.Click += (_, _) => Open(Brand.Site + "/tools/discord#meter");
        _signOut.Click += async (_, _) =>
        {
            _signOut.IsEnabled = false;
            await Account.SignOutAsync();
            _signOut.IsEnabled = true;
            Draw(null);
        };
        _timer.Tick += async (_, _) => await PollAsync();
        _ownLink.Click += async (_, _) => await StartOwnAsync();
        _ownOpenAgain.Click += (_, _) => OpenOwn(_ownPending?.Url, _ownPending?.Site);
        _ownOpen.Click += (_, _) => OpenOwn(OwnSite.Site + "/", OwnSite.Site);
        _ownCancel.Click += (_, _) =>
        {
            if (_ownPending is { } p)
            {
                OwnSite.Cancel(p);
            }

            _ownPending = null;
            _ownTimer.Stop();
            _ownMessage = null;
            DrawOwnSite();
        };
        _ownUnlink.Click += async (_, _) =>
        {
            _ownUnlink.IsEnabled = false;
            await OwnSite.UnlinkAsync();
            _ownUnlink.IsEnabled = true;
            _ownMessage = null;
            DrawOwnSite();
        };
        _ownTimer.Tick += async (_, _) => await PollOwnAsync();
        Closed += (_, _) =>
        {
            _ownTimer.Stop();
            // still waiting for the approval on the legion's site: the app goes on asking without this window
            if (_ownPending is { } waiting && !OwnSite.Linked)
            {
                OwnSite.WaitInBackground(waiting);
            }
        };
        if (!OwnSite.Linked && OwnSite.TakeWaiting() is { } earlierOwn)
        {
            _ownPending = earlierOwn;
            _ownTimer.Interval = TimeSpan.FromSeconds(earlierOwn.Interval);
            _ownTimer.Start();
        }
        Closed += (_, _) =>
        {
            _timer.Stop();
            // still waiting for the approval in the browser: the app goes on asking without this window (11 November 2026)
            if (_pending is { } waiting && !Account.SignedIn)
            {
                Account.WaitInBackground(waiting);
            }
        };
        // a sign-in started earlier and still waiting (this window was closed meanwhile): shown again, with its code
        if (!Account.SignedIn && Account.TakeWaiting() is { } earlier)
        {
            _pending = earlier;
            _timer.Interval = TimeSpan.FromSeconds(earlier.Interval);
            _timer.Start();
        }

        Account.Changed += OnAccountChanged;
        Closed += (_, _) => Account.Changed -= OnAccountChanged;
        CharacterVerify.Changed += OnAccountChanged;
        Closed += (_, _) => CharacterVerify.Changed -= OnAccountChanged;
        LegionShare.Changed += OnAccountChanged;
        Closed += (_, _) => LegionShare.Changed -= OnAccountChanged;
        OwnSite.Changed += OnAccountChanged;
        Closed += (_, _) => OwnSite.Changed -= OnAccountChanged;
        OwnSiteShare.Changed += OnAccountChanged;
        Closed += (_, _) => OwnSiteShare.Changed -= OnAccountChanged;
        _legion.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _shareLegion.Content = Wrapped(LocalizationManager.Instance["Legion.Share"]);
        _shareLegion.IsChecked = MeterSettings.Load().ShareLegionList;
        _shareLegion.Click += (_, _) =>
        {
            MeterSettings settings = MeterSettings.Load();
            settings.ShareLegionList = _shareLegion.IsChecked == true;
            settings.Save();
        };
        _shareProfile.Content = Wrapped(LocalizationManager.Instance["Profile.Share"]);
        _shareProfile.IsChecked = MeterSettings.Load().ShareProfile;
        _shareProfile.Click += async (_, _) =>
        {
            MeterSettings settings = MeterSettings.Load();
            settings.ShareProfile = _shareProfile.IsChecked == true;
            settings.Save();
            if (!settings.ShareProfile)
            {
                await ProfileShare.ForgetAsync(); // switched off: the website forgets what was sent
            }
            else
            {
                ProfileShare.Resend(); // on again: sent at once, not only after the next login
            }
        };
        Draw(null);
        Loaded += async (_, _) =>
        {
            await Account.RefreshAsync();
            await OwnSite.RefreshAsync();
            Draw(null);
        };
    }

    /// <summary>
    /// A check box's words. The check box style puts them next to the box in a row without a width, so a long text never wrapped and was cut off at the
    /// window's edge, in the system's dim text colour (9 November 2026, the owner: "i cant read" the account window). They wrap now, in the text colour.
    /// </summary>
    private static TextBlock Wrapped(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 410, FontSize = 12.5, LineHeight = 18 };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        return block;
    }

    /// <summary>The window as a signed-in player sees it, for the pictures of the checks (render-tool account).</summary>
    internal static AccountWindow Sample()
    {
        var window = new AccountWindow();
        window.DrawSignedIn("Exelt");
        return window;
    }

    private void OnAccountChanged() => Dispatcher.BeginInvoke(new Action(() => Draw(null)));

    /// <summary>Draws the state: signed in, waiting for the approval, or signed out (with an optional message).</summary>
    private void Draw(string? message)
    {
        DrawOwnSite();
        if (Account.SignedIn)
        {
            DrawSignedIn(Account.Name);
            return;
        }

        _verify.Visibility = Visibility.Collapsed;
        // the legion list's switch counts for the legion's own site too
        _shareLegion.Visibility = OwnSite.Linked ? Visibility.Visible : Visibility.Collapsed;
        _shareProfile.Visibility = Visibility.Collapsed;
        _legion.Visibility = Visibility.Collapsed;
        _signOut.Visibility = Visibility.Collapsed;
        _myFights.Visibility = Visibility.Collapsed;
        if (_pending is { } p)
        {
            _status.Text = "Approve the sign-in in your browser. The page shows this code:";
            _code.Text = p.Code;
            _code.Visibility = Visibility.Visible;
            _hint.Text = message ?? "In the browser: Connect Discord (Discord asks for your password there if the browser isn’t signed in to Discord, even when the Discord app is), then Authorize, then Approve on the page that comes back. The app notices it by itself, also if you close this window.";
            _signIn.Visibility = Visibility.Collapsed;
            _openAgain.Visibility = Visibility.Visible;
            return;
        }

        _status.Text = "Not signed in.";
        _hint.Text = message ?? "Sign in with your Discord to upload fights to atreia.app. Every fight you upload is yours: you decide who can see it, and you can delete it.";
        _code.Visibility = Visibility.Collapsed;
        _signIn.Visibility = Visibility.Visible;
        _signIn.IsEnabled = true;
        _openAgain.Visibility = Visibility.Collapsed;
    }

    /// <summary>The signed-in state: the account's name, the character check, the legion list and the profile switches.</summary>
    private void DrawSignedIn(string? name)
    {
        _pending = null;
        _timer.Stop();
        _status.Text = "Signed in" + (name is { Length: > 0 } ? " as " + name : "") + ".";
        _hint.Text = "Fights you upload go to your Atreia account. You choose who sees each one on the site: only you, anyone with the link, or everyone. New fights get the choice set on the Discord page (Meter).";
        _code.Visibility = Visibility.Collapsed;
        _signIn.Visibility = Visibility.Collapsed;
        _openAgain.Visibility = Visibility.Collapsed;
        _signOut.Visibility = Visibility.Visible;
        _myFights.Visibility = Visibility.Visible;
        _verify.Text = VerifyText(CharacterVerify.Last);
        _verify.Visibility = Visibility.Visible;
        _shareLegion.Visibility = Visibility.Visible;
        _shareProfile.Visibility = Visibility.Visible;
        _legion.Text = LegionText(LegionShare.Last);
        _legion.Visibility = Visibility.Visible;
    }

    /// <summary>The meter's check of the character played, in the window's words: how the last one went, or how it works before the first one.</summary>
    private static string VerifyText(VerifyResult? last)
    {
        var loc = LocalizationManager.Instance;
        if (last is null)
        {
            return loc["Verify.Hint"];
        }

        string key = last.Kind switch
        {
            "verified" => "Verify.Done",
            "mismatch" => "Verify.Mismatch",
            "taken" => "Verify.Taken",
            "full" => "Verify.TooMany",
            _ => "Verify.Later",
        };
        return string.Format(loc[key], last.Name, last.Server);
    }

    /// <summary>The legion list's last send, in the window's words (how it works before the first one).</summary>
    internal static string LegionText(LegionShareResult? last)
    {
        var loc = LocalizationManager.Instance;
        return last switch
        {
            null => loc["Legion.Hint"],
            { Kind: "sent" } => string.Format(loc["Legion.Sent"], last.Legion, last.Members, last.Known, last.Lookup),
            { Kind: "refused" } => last.Message is { Length: > 0 } refused ? last.Legion + ": " + refused : string.Format(loc["Legion.Refused"], last.Legion),
            _ => last.Message is { Length: > 0 } later ? last.Legion + ": " + later : string.Format(loc["Legion.Later"], last.Legion),
        };
    }

    /// <summary>The legion's own site: linked (and how its checks went), waiting for the approval there, or not linked (the address to type).</summary>
    private void DrawOwnSite()
    {
        if (OwnSite.Linked && OwnSite.Host is { } host)
        {
            _ownPending = null;
            _ownTimer.Stop();
            _ownStatus.Text = "Linked to " + host + (OwnSite.Name is { Length: > 0 } name ? " as " + name : "") + ".";
            _ownHint.Text = _ownMessage ?? OwnSiteText(host);
            _ownHint.Visibility = Visibility.Visible;
            _ownCode.Visibility = Visibility.Collapsed;
            _ownAddressBox.Visibility = Visibility.Collapsed;
            _ownLink.Visibility = Visibility.Collapsed;
            _ownOpenAgain.Visibility = Visibility.Collapsed;
            _ownCancel.Visibility = Visibility.Collapsed;
            _ownOpen.Content = "Open " + host;
            _ownOpen.Visibility = Visibility.Visible;
            _ownUnlink.Visibility = Visibility.Visible;
            return;
        }

        _ownOpen.Visibility = Visibility.Collapsed;
        _ownUnlink.Visibility = Visibility.Collapsed;
        if (_ownPending is { } p)
        {
            _ownStatus.Text = "Approve it on " + OwnSiteAddress.HostOf(p.Site) + ", signed in there with Discord. Its page shows this code:";
            _ownCode.Text = p.Code;
            _ownCode.Visibility = Visibility.Visible;
            _ownHint.Text = _ownMessage ?? "The app notices it by itself, also if you close this window.";
            _ownHint.Visibility = Visibility.Visible;
            _ownAddressBox.Visibility = Visibility.Collapsed;
            _ownLink.Visibility = Visibility.Collapsed;
            _ownOpenAgain.Visibility = Visibility.Visible;
            _ownCancel.Visibility = Visibility.Visible;
            return;
        }

        _ownStatus.Text = "If your legion runs its own copy of the Atreia website, with its own Discord bot, link the app to it too. The app then also sends that site the "
            + "check of the character you play and, with the legion list on, your legion's member list, so that bot's rosters fill by themselves. That site is run "
            + "by your legion, not by Atreia.";
        _ownHint.Text = _ownMessage ?? "";
        _ownHint.Visibility = _ownHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _ownCode.Visibility = Visibility.Collapsed;
        _ownAddressBox.Visibility = Visibility.Visible;
        _ownLink.Visibility = Visibility.Visible;
        _ownLink.IsEnabled = true;
        _ownOpenAgain.Visibility = Visibility.Collapsed;
        _ownCancel.Visibility = Visibility.Collapsed;
    }

    /// <summary>How the app's sends to the legion's own site went in this run, in the window's words (what happens at the next login before the first one).</summary>
    private static string OwnSiteText(string host)
    {
        var lines = new List<string>();
        if (OwnSiteShare.LastVerify is { } verify)
        {
            lines.Add(VerifyText(verify));
        }

        if (OwnSiteShare.LastLegion is { } legion)
        {
            lines.Add(LegionText(legion));
        }

        return lines.Count > 0
            ? string.Join("\n", lines)
            : $"At your next login in the game, the app sends {host} the check of the character you play, then your legion's member list (with the legion list on).";
    }

    private async Task StartOwnAsync()
    {
        _ownLink.IsEnabled = false;
        _ownMessage = null;
        _ownHint.Text = "Looking at " + (OwnSiteAddress.Normalize(_ownAddress.Text) is { } typed ? OwnSiteAddress.HostOf(typed) : "the address") + "…";
        _ownHint.Visibility = Visibility.Visible;
        (OwnSite.LinkStart? start, string? error) = await OwnSite.StartAsync(_ownAddress.Text);
        if (start is null)
        {
            _ownMessage = error;
            DrawOwnSite();
            return;
        }

        _ownPending = start;
        OwnSite.Started(start);
        _ownFailures = 0;
        DrawOwnSite();
        OpenOwn(start.Url, start.Site);
        _ownTimer.Interval = TimeSpan.FromSeconds(start.Interval);
        _ownTimer.Start();
    }

    private async Task PollOwnAsync()
    {
        if (_ownPending is not { } p)
        {
            _ownTimer.Stop();
            return;
        }

        if (DateTime.UtcNow > p.ExpiresAt)
        {
            _ownPending = null;
            OwnSite.Finished(p);
            _ownMessage = "The link ran out. Press Link to try again.";
            DrawOwnSite();
            return;
        }

        _ownTimer.Stop();
        Account.LinkState state = await OwnSite.PollAsync(p);
        if (_ownPending != p)
        {
            return;
        }

        switch (state)
        {
            case Account.LinkState.Linked:
                _ownMessage = null;
                DrawOwnSite();
                await OwnSite.RefreshAsync();
                DrawOwnSite();
                return;
            case Account.LinkState.Expired:
                _ownPending = null;
                OwnSite.Finished(p);
                _ownMessage = "The link ran out or was used already. Press Link to try again.";
                DrawOwnSite();
                return;
            case Account.LinkState.Failed:
                _ownFailures++;
                _ownMessage = _ownFailures >= 3 ? OwnSiteAddress.HostOf(p.Site) + " can’t be reached right now. Still trying…" : null;
                DrawOwnSite();
                break;
            default:
                _ownFailures = 0;
                break;
        }

        if (IsLoaded)
        {
            _ownTimer.Start();
        }
    }

    /// <summary>A page of the legion's own site, in the browser: only that site's own pages.</summary>
    private static void OpenOwn(string? url, string? site)
    {
        if (site is null || !OwnSiteAddress.IsOnSite(url, site))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no browser set up: the code is shown here, and the page is that site's /meter/link
        }
    }

    private async Task StartAsync()
    {
        _signIn.IsEnabled = false;
        (Account.LinkStart? start, string? error) = await Account.StartAsync();
        if (start is null)
        {
            Draw(error);
            return;
        }

        _pending = start;
        Account.Started(start);
        _failures = 0;
        Draw(null);
        Open(start.Url);
        _timer.Interval = TimeSpan.FromSeconds(start.Interval);
        _timer.Start();
    }

    private async Task PollAsync()
    {
        if (_pending is not { } p)
        {
            _timer.Stop();
            return;
        }

        if (DateTime.UtcNow > p.ExpiresAt)
        {
            _pending = null;
            Account.Finished(p);
            Draw("The sign-in ran out. Press Sign in with Discord to try again.");
            return;
        }

        _timer.Stop();
        Account.LinkState state = await Account.PollAsync(p.Poll);
        if (_pending != p)
        {
            return;
        }

        switch (state)
        {
            case Account.LinkState.Linked:
                Account.Finished(p);
                Draw(null);
                await Account.RefreshAsync();
                Draw(null);
                return;
            case Account.LinkState.Expired:
                _pending = null;
                Account.Finished(p);
                Draw("The sign-in ran out or was used already. Press Sign in with Discord to try again.");
                return;
            case Account.LinkState.Failed:
                _failures++;
                Draw(_failures >= 3 ? "The site can’t be reached right now. Still trying…" : null);
                break;
            default:
                _failures = 0;
                break;
        }

        if (IsLoaded)
        {
            _timer.Start();
        }
    }

    private static void Open(string? url)
    {
        if (!Brand.IsSiteUrl(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // no browser set up: the code is shown here, and the page is atreia.app/meter/link
        }
    }
}
