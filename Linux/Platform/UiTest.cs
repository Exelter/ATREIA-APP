using Avalonia;
using Avalonia.Controls;

namespace AionDPS.Linux.Platform;

/// <summary>
/// The workflow's test of the window's buttons (Linux/packaging/ci/ui-test.py clicks them on an X server with xdotool): with ATREIA_UI_TEST=1 the meter
/// writes into linux.log where on the screen its buttons, its scope switch, its grip and the items of an open menu are, so the test knows where to click.
/// Without it nothing is written. What a click did is in the log either way (<see cref="LinuxLog"/>, "action: ...").
/// </summary>
internal static class UiTest
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("ATREIA_UI_TEST") == "1";

    private static readonly Dictionary<string, string> Said = new();

    /// <summary>Notes where the middle of <paramref name="control"/> is on the screen (in pixels), or that it is hidden; only when that changed.</summary>
    public static void Where(string name, Control control) => WherePoint(name, control, new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));

    /// <summary>Notes where a point of <paramref name="control"/> is on the screen.</summary>
    public static void WherePoint(string name, Control control, Point point)
    {
        if (!Enabled)
        {
            return;
        }

        string where = "hidden";
        if (control.IsEffectivelyVisible && control.Bounds.Width > 0 && TopLevel.GetTopLevel(control) is not null)
        {
            PixelPoint at = control.PointToScreen(point);
            where = $"{at.X},{at.Y}";
        }

        if (Said.TryGetValue(name, out string? before) && before == where)
        {
            return;
        }

        Said[name] = where;
        LinuxLog.Write($"ui: {name} {where}");
    }

    /// <summary>Notes that a part is gone from the screen (the items of a menu that closed).</summary>
    public static void Gone(string name)
    {
        if (!Enabled || (Said.TryGetValue(name, out string? before) && before == "hidden"))
        {
            return;
        }

        Said[name] = "hidden";
        LinuxLog.Write($"ui: {name} hidden");
    }
}
