using System.Text;
using AionDPS.Aion2.Capture;
using SharpPcap;

namespace AionDPS.Linux.Platform;

/// <summary>
/// What the app can see of the network and the game, in a few lines (Program "capture-check", and the meter's menu): whether libpcap is there, whether this
/// app may read with it, which devices it offers, whether the game runs and which connections it has open. Reads, never changes anything.
/// </summary>
internal static class CaptureCheck
{
    public static string Summary()
    {
        var text = new StringBuilder();
        NpcapAvailability pcap = NpcapAvailability.Detect();
        text.Append(pcap.IsInstalled ? $"libpcap: {pcap.Location}" : "libpcap: missing");
        CaptureRights.Rights rights = CaptureRights.Check();
        text.Append(rights switch
        {
            CaptureRights.Rights.Granted => " · may read the network",
            CaptureRights.Rights.Ignored => " · the right to read the network is given but switched off (a nosuid drive)",
            _ => " · may not read the network yet",
        });
        if (pcap.IsInstalled)
        {
            try
            {
                var devices = CaptureDeviceList.Instance.Select(d => d.Name).ToList();
                text.Append($" · devices: {(devices.Count == 0 ? "none" : string.Join(", ", devices.Take(8)) + (devices.Count > 8 ? " …" : ""))}");
            }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or PcapException)
            {
                text.Append(" · devices: " + ex.GetBaseException().Message);
            }
        }

        List<int> game = GameConnections.GamePids();
        if (game.Count == 0)
        {
            text.Append(" · the game (AION2.exe) is not running");
        }
        else
        {
            IReadOnlyList<int> ports = GameConnections.RemotePorts();
            text.Append($" · the game runs (process {string.Join(", ", game)})" + (ports.Count > 0 ? $", its connections go to port {string.Join(", ", ports)}" : ", no connection of it seen yet"));
        }

        if (CaptureAdapters.DefaultRouteInterface() is { } routed)
        {
            text.Append($" · internet through {routed}");
        }

        return text.ToString();
    }

    /// <summary>The command line's "capture-check": the summary, one part a line.</summary>
    public static int Run()
    {
        foreach (string part in Summary().Split(" · "))
        {
            Console.WriteLine(part);
        }

        if (CaptureRights.Check() != CaptureRights.Rights.Granted)
        {
            Console.WriteLine();
            Console.WriteLine("To give this app the right to read the network (once; again after the app was replaced):");
            Console.WriteLine("  " + CaptureRights.SetcapCommand);
        }

        return 0;
    }
}
