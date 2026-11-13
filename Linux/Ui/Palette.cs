using Avalonia.Media;

namespace AionDPS.Linux.Ui;

/// <summary>
/// The Windows app's dark theme (Client/Ui/Themes/Dark.xaml), the colours the meter uses: the panel over the game, its text, the muted text, the gold
/// accent, the healing green, the warning and danger reds. And the website's faces, compiled into the app (Client/Fonts).
/// </summary>
internal static class Palette
{
    public static readonly IBrush OverlayBg = Brush("#EB121016");
    public static readonly IBrush Panel = Brush("#1B1820");
    public static readonly IBrush Control = Brush("#211E27");
    public static readonly IBrush ControlHover = Brush("#2A2631");
    public static readonly IBrush Border = Brush("#3A3441");
    public static readonly IBrush Text = Brush("#EFE8DC");
    public static readonly IBrush OverlayText = Brush("#F4EEE4");
    public static readonly IBrush TextSubtle = Brush("#CFC6B6");
    public static readonly IBrush TextMuted = Brush("#A59C8E");
    public static readonly IBrush TextDisabled = Brush("#6E6676");
    public static readonly IBrush Accent = Brush("#DCB877");
    public static readonly IBrush Heal = Brush("#8FD19A");
    public static readonly IBrush Warning = Brush("#E8C35A");
    public static readonly IBrush Danger = Brush("#E06A5E");
    public static readonly IBrush Green = Brush("#6CD48A");
    public static readonly IBrush Amber = Brush("#E8B04A");

    /// <summary>The chosen one of a switch: a gold-tinted pill with gold text (the website's switches).</summary>
    public static readonly IBrush ScopeOn = Brush("#33DCB877");

    public static readonly IBrush Separator = Brush("#26FFFFFF");

    /// <summary>
    /// Inter (it comes with Avalonia). Not the bundled A2H Sans: its files (Client/Fonts) hold only the space, A, B and the accented letters - the
    /// rest of the alphabet is not in them - so on Windows nearly all text is drawn in its fallback, Segoe UI, which Linux does not have; Inter is the
    /// nearest face to it here.
    /// </summary>
    public static readonly FontFamily UiFont = new("fonts:Inter#Inter");

    /// <summary>Cinzel, the site's title face.</summary>
    public static readonly FontFamily TitleFont = new("avares://AtreiaApp/Fonts#Cinzel");

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex)).ToImmutable();
}
