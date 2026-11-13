using System.Drawing;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// The game window's picture (Ui.GameCapture: Windows' own capture where this Windows has it without the yellow frame, else PrintWindow), on a window of the
/// check's own in two colours with a title bar: the picture is its client area only (the title bar left out), in its own colours; a second picture of the
/// unchanged window is the same; a change shows in the next picture. Which way took them is printed (the runner may have no capture of its own).
/// </summary>
public static class SelfCheckWindowCapture
{
    private static readonly Color Red = Color.FromArgb(200, 40, 40), Blue = Color.FromArgb(40, 40, 200), Green = Color.FromArgb(40, 200, 40);

    public static bool Run()
    {
        Console.WriteLine("[selftest] the game window's picture (a window of the check's own: red on the left, blue on the right, with a title bar):");
        using var form = new System.Windows.Forms.Form
        {
            Text = "Atreia App capture check",
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle,
            StartPosition = System.Windows.Forms.FormStartPosition.Manual,
            Location = new Point(60, 60),
            ClientSize = new Size(320, 200),
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = Red,
        };
        var right = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Right, Width = 160, BackColor = Blue };
        form.Controls.Add(right);
        form.Show();
        Pump(500);
        IntPtr window = form.Handle;
        Size size = AionDPS.Ui.GameWindow.ClientSizeInPixels(window) ?? form.ClientSize;

        (Color Left, Color Right, Color Corner)? Take(TimeSpan within)
        {
            (Color, Color, Color)? seen = null;
            DateTime until = DateTime.UtcNow + within;
            do
            {
                if (GameCapture.Use(window, size, TimeSpan.Zero, picture =>
                    seen = (picture.GetPixel(size.Width / 4, size.Height / 2), picture.GetPixel(size.Width * 3 / 4, size.Height / 2), picture.GetPixel(3, 3))))
                {
                    return seen;
                }

                Pump(100);
            }
            while (DateTime.UtcNow < until);
            return null;
        }

        var first = Take(TimeSpan.FromSeconds(4));
        string way = GameCapture.LastWay + (WindowCapture.LastError is { } why ? " (Windows' capture: " + why + ")" : "");
        bool own = first is { } a && Near(a.Left, Red) && Near(a.Right, Blue) && Near(a.Corner, Red);
        Console.WriteLine($"  -> taken by {way}: the window's client area in its own colours, the title bar left out: {own}"
            + (first is { } f ? $" ({Text(f.Left)}, {Text(f.Right)}, corner {Text(f.Corner)})" : " (no picture)"));

        var again = Take(TimeSpan.FromSeconds(2));
        bool same = again is { } b && Near(b.Left, Red) && Near(b.Right, Blue);
        Console.WriteLine($"  -> a second picture of the unchanged window right away is the same: {same}");

        right.BackColor = Green;
        right.Refresh();
        Pump(400);
        var changed = Take(TimeSpan.FromSeconds(2));
        bool follows = changed is { } c && Near(c.Left, Red) && Near(c.Right, Green);
        Console.WriteLine($"  -> after the right half turned green, the next picture shows it ({GameCapture.LastWay}): {follows}"
            + (changed is { } g ? $" ({Text(g.Right)})" : " (no picture)"));

        form.Close();
        WindowCapture.Close();
        return own && same && follows;
    }

    private static void Pump(int milliseconds)
    {
        DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static bool Near(Color seen, Color wanted) =>
        Math.Abs(seen.R - wanted.R) <= 12 && Math.Abs(seen.G - wanted.G) <= 12 && Math.Abs(seen.B - wanted.B) <= 12;

    private static string Text(Color c) => $"{c.R},{c.G},{c.B}";
}
