using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace AionDPS.Ui;

/// <summary>
/// The overlays' motion (10 November 2026, the owner: "way more smooth"): an overlay fades in and settles from a few pixels below when it comes up; bars ease
/// to their new value from the one they showed before, even when the overlay draws its lines anew (the last value is kept per bar); a moment worth seeing (a
/// mission done, an event going live, a skill ready) gets a short gold glow. Short and quiet: the game is the show.
/// </summary>
internal static class OverlayMotion
{
    private static readonly Dictionary<string, double> LastShare = new(StringComparer.Ordinal);
    private static readonly IEasingFunction Ease = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>Fades <paramref name="element"/> in and lets it settle from 6 pixels below (180 ms).</summary>
    public static void Appear(UIElement element)
    {
        var shift = element.RenderTransform as TranslateTransform;
        if (shift is null)
        {
            if (element.RenderTransform is not (null or MatrixTransform { Matrix.IsIdentity: true }))
            {
                Fade(element); // a transform of its own is kept: only the fade
                return;
            }

            shift = new TranslateTransform();
            element.RenderTransform = shift;
        }

        Fade(element);
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = Ease });
    }

    private static void Fade(UIElement element) =>
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = Ease });

    /// <summary>
    /// A bar <paramref name="share"/> (0 to 1) full, easing there from what the bar of the same <paramref name="key"/> showed last (the first time: from empty).
    /// The fill is drawn whole and scaled, so it needs no width of its own and stays round at its ends.
    /// </summary>
    public static Grid Bar(string key, double share, Brush fill, Brush track, double height = 3, double radius = 1.5)
    {
        share = double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0;
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(new Border { Background = track, CornerRadius = new CornerRadius(radius) });
        var scale = new ScaleTransform(share, 1);
        var bar = new Border { Background = fill, CornerRadius = new CornerRadius(radius), RenderTransform = scale, RenderTransformOrigin = new Point(0, 0.5) };
        grid.Children.Add(bar);
        double from = LastShare.TryGetValue(key, out double last) ? last : 0;
        LastShare[key] = share;
        if (Math.Abs(from - share) > 0.002)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, share, TimeSpan.FromMilliseconds(Math.Abs(share - from) > 0.5 ? 650 : 420)) { EasingFunction = Ease });
        }

        if (LastShare.Count > 4000)
        {
            LastShare.Clear();
        }

        return grid;
    }

    /// <summary>
    /// A bar that fills by itself, evenly, from <paramref name="share"/> to full over <paramref name="left"/> (an event coming closer: full when it starts).
    /// </summary>
    public static Grid Countdown(double share, TimeSpan left, Brush fill, Brush track, double height = 2)
    {
        share = double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0;
        var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Bottom };
        grid.Children.Add(new Border { Background = track, CornerRadius = new CornerRadius(height / 2) });
        var scale = new ScaleTransform(share, 1);
        grid.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2), RenderTransform = scale, RenderTransformOrigin = new Point(0, 0.5) });
        if (left > TimeSpan.Zero && share < 1)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(share, 1, left));
        }

        return grid;
    }

    /// <summary>A short gold glow around <paramref name="element"/>, twice (a mission done, an event going live, a skill ready).</summary>
    public static void Pulse(UIElement element, Color? color = null)
    {
        var glow = new DropShadowEffect { Color = color ?? Color.FromRgb(0xDC, 0xB8, 0x77), BlurRadius = 16, ShadowDepth = 0, Opacity = 0 };
        element.Effect = glow;
        var pulse = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1300) };
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.95, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), Ease));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600)), Ease));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(850)), Ease));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300)), Ease));
        pulse.Completed += (_, _) =>
        {
            if (ReferenceEquals(element.Effect, glow))
            {
                element.Effect = null;
            }
        };
        glow.BeginAnimation(DropShadowEffect.OpacityProperty, pulse);
    }

    /// <summary>Whether <paramref name="key"/> turned true just now: false the first time it is seen (an overlay drawn the first time pulses nothing).</summary>
    public static bool BecameTrue(string key, bool now)
    {
        bool known = Seen.TryGetValue(key, out bool before);
        Seen[key] = now;
        if (Seen.Count > 4000)
        {
            Seen.Clear();
        }

        return known && now && !before;
    }

    private static readonly Dictionary<string, bool> Seen = new(StringComparer.Ordinal);

    private static IEasingFunction Frozen(CubicEase ease)
    {
        ease.Freeze();
        return ease;
    }
}
