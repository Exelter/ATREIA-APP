using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SharpPcap;

namespace AionDPS.Aion2.Capture;

/// <summary>One network adapter the game's traffic could leave through.</summary>
public sealed record CaptureAdapter(string Id, string Name, string Description, string Ipv4, bool HasGateway, bool IsVirtual, bool IsVpn, bool IsRecommended)
{
    public string Label => $"{Name} - {Ipv4}" + (IsVpn ? " [VPN]" : IsVirtual ? " [virtual]" : "") + (IsRecommended ? " *" : "");
}

/// <summary>
/// Linux counterpart of the Windows app's CaptureAdapters (Client/Aion2/Capture/CaptureAdapters.cs). Linux has a capture device of its own that sees every
/// interface at once, "any": the automatic choice takes that one alone, so the game's traffic is found whichever way it leaves (the network card, a VPN's
/// tun device, a ping booster on 127.0.0.1) and no segment comes twice. The adapter's id is the interface's name ("eth0", "wlan0"), which is also how
/// libpcap names its devices. The recommended adapter is the one the default route goes through.
/// </summary>
public static class CaptureAdapters
{
    public const string AllAdapters = "all";

    /// <summary>libpcap's device for every interface together.</summary>
    internal const string AnyDevice = "any";

    private static readonly string[] VirtualMarkers = { "docker", "veth", "br-", "virbr", "vmnet", "vboxnet", "lo" };
    private static readonly string[] VpnMarkers = { "tun", "tap", "wg", "ppp", "vpn", "tailscale", "zt", "nordlynx", "proton" };

    /// <summary>Devices that carry no IP traffic (netfilter queues, D-Bus, USB, Bluetooth): never opened.</summary>
    private static readonly string[] NotNetwork = { "nflog", "nfqueue", "dbus", "usbmon", "bluetooth", "dummy" };

    public static IReadOnlyList<CaptureAdapter> List()
    {
        string? routed = DefaultRouteInterface();
        var result = new List<(CaptureAdapter Adapter, int Score)>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus is not (OperationalStatus.Up or OperationalStatus.Unknown) || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties props = nic.GetIPProperties();
            string? ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            if (ipv4 is null)
            {
                continue;
            }

            string name = nic.Name.ToLowerInvariant();
            bool isVpn = nic.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp || VpnMarkers.Any(name.StartsWith);
            bool isVirtual = !isVpn && VirtualMarkers.Any(name.StartsWith);
            bool hasGateway = nic.Name == routed;
            int score = (hasGateway ? 1000 : 0) + (isVirtual ? -500 : 0);
            result.Add((new CaptureAdapter(nic.Name, nic.Name, nic.Description, ipv4, hasGateway, isVirtual, isVpn, false), score));
        }

        string? bestId = result.OrderByDescending(r => r.Score).Select(r => r.Adapter.Id).FirstOrDefault();
        return result
            .OrderByDescending(r => r.Adapter.Id == bestId)
            .ThenByDescending(r => r.Score)
            .Select(r => r.Adapter with { IsRecommended = r.Adapter.Id == bestId })
            .ToList();
    }

    public static CaptureAdapter? Recommended(IReadOnlyList<CaptureAdapter> adapters) => adapters.FirstOrDefault(a => a.IsRecommended);

    /// <summary>
    /// The libpcap devices to capture on for the stored choice: automatic is the "any" device alone (every interface, each segment once), "all" every real
    /// device, anything else the device of that name. Falls back to every device (with a note) when the choice cannot be matched, so a stale setting never
    /// silently captures nothing.
    /// </summary>
    public static IReadOnlyList<ILiveDevice> Select(IReadOnlyList<ILiveDevice> devices, string? adapterId, out string? note)
    {
        note = null;
        var real = devices.Where(d => d.Name != AnyDevice && !NotNetwork.Any(n => d.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase))).ToList();
        if (string.IsNullOrEmpty(adapterId))
        {
            ILiveDevice? any = devices.FirstOrDefault(d => d.Name == AnyDevice);
            return any is not null ? new[] { any } : real;
        }

        if (adapterId == AllAdapters)
        {
            return real;
        }

        var matched = real.Where(d => string.Equals(d.Name, adapterId, StringComparison.Ordinal)).ToList();
        if (matched.Count == 0)
        {
            note = $"adapter {adapterId} not found among the capture devices - capturing on all";
            return devices.FirstOrDefault(d => d.Name == AnyDevice) is { } fallback ? new[] { fallback } : real;
        }

        return matched;
    }

    /// <summary>The interface of the default route (the kernel's routing table, /proc/net/route: destination 00000000), or null.</summary>
    internal static string? DefaultRouteInterface()
    {
        try
        {
            foreach (string line in File.ReadLines("/proc/net/route").Skip(1))
            {
                string[] f = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length > 2 && f[1] == "00000000")
                {
                    return f[0];
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // no routing table to read
        }

        return null;
    }
}
