using System.Runtime.InteropServices;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// Linux counterpart of the Windows app's NpcapAvailability (Client/Aion2/Capture/NpcapAvailability.cs): whether the packet capture library is there.
/// On Linux that is libpcap (the library tcpdump and Wireshark use), which SharpPcap loads by these names; most systems have it, a few need the package
/// (libpcap0.8 on Debian and Ubuntu, libpcap on Fedora, Arch and SteamOS). The name keeps the Windows one, so the shared code that asks it is the same.
/// </summary>
public sealed class NpcapAvailability
{
    /// <summary>The names SharpPcap tries, in its order.</summary>
    internal static readonly string[] LibraryNames = { "libpcap.so", "libpcap.so.0", "libpcap.so.0.8", "libpcap.so.1" };

    private NpcapAvailability(bool installed, string? location)
    {
        IsInstalled = installed;
        Location = location;
    }

    public bool IsInstalled { get; }

    /// <summary>The library's name as it was found.</summary>
    public string? Location { get; }

    /// <summary>Where libpcap comes from; on Linux it is installed with the system's package manager (see README.md).</summary>
    public static string DownloadUrl => "https://www.tcpdump.org/";

    public static NpcapAvailability Detect()
    {
        foreach (string name in LibraryNames)
        {
            if (NativeLibrary.TryLoad(name, out IntPtr handle))
            {
                // only asked whether it is there: SharpPcap loads it again itself, and the loader counts both
                NativeLibrary.Free(handle);
                return new NpcapAvailability(true, name);
            }
        }

        return new NpcapAvailability(false, null);
    }
}
