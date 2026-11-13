using System.Globalization;
using System.IO;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// Linux counterpart of the Windows app's GameConnections (Client/Aion2/Capture/GameConnections.cs, which asks Windows' connection table): the TCP
/// connections the game has open right now, read from /proc. The game runs under Wine or Proton, so it is a Linux process like any other: its name is the
/// Windows program's (Wine names the process after the .exe), its sockets are links in /proc/&lt;pid&gt;/fd, and the kernel's connection table
/// (/proc/&lt;pid&gt;/net/tcp and tcp6) says where each of them goes. With a ping booster or a VPN the game talks to another port, so the meter learns the ports
/// from the process instead of guessing - the same as on Windows. Nothing is sent or changed; only these files are read, a few times a minute at most.
/// </summary>
public static class GameConnections
{
    public const string ProcessName = "AION2";

    /// <summary>The game's program as Wine runs it.</summary>
    private const string ExeName = ProcessName + ".exe";

    /// <summary>Where the process table is (a check points it at a folder of its own).</summary>
    internal static string ProcRoot { get; set; } = "/proc";

    /// <summary>Is the game running at all.</summary>
    public static bool GameRunning() => GamePids().Count > 0;

    /// <summary>The remote ports of the game's established TCP connections (empty when the game isn't running or nothing can be read).</summary>
    public static IReadOnlyList<int> RemotePorts()
    {
        List<int> pids = GamePids();
        if (pids.Count == 0)
        {
            return Array.Empty<int>();
        }

        var inodes = new HashSet<long>();
        foreach (int pid in pids)
        {
            SocketInodes(pid, inodes);
        }

        if (inodes.Count == 0)
        {
            return Array.Empty<int>();
        }

        var ports = new SortedSet<int>();
        foreach (int pid in pids)
        {
            // the game's own view of the network (a container of its own, such as Steam's runtime, may have one); the same table for all its processes
            if (ReadTable(Path.Combine(ProcRoot, pid.ToString(CultureInfo.InvariantCulture), "net", "tcp"), inodes, ports)
                | ReadTable(Path.Combine(ProcRoot, pid.ToString(CultureInfo.InvariantCulture), "net", "tcp6"), inodes, ports))
            {
                break;
            }
        }

        return ports.ToList();
    }

    /// <summary>The process ids of the game: a process named after the game's program (Wine renames its processes so), or one whose command line starts
    /// with it (an older Wine, or a process name cut at 15 letters).</summary>
    internal static List<int> GamePids()
    {
        var found = new List<int>();
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(ProcRoot))
            {
                if (int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && IsGame(dir))
                {
                    found.Add(pid);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // no process table: nothing found
        }

        return found;
    }

    /// <summary>Whether one process of the table is the game.</summary>
    internal static bool IsGame(string processDir)
    {
        try
        {
            string comm = File.ReadAllText(Path.Combine(processDir, "comm")).Trim();
            if (comm.Equals(ExeName, StringComparison.OrdinalIgnoreCase) || comm.Equals(ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Wine's own processes (the preloader, a name cut short): the program they run is the first word of the command line, a Windows path
            // ("C:\Program Files\...\AION2.exe" or "Z:\home\...\AION2.exe") or a Linux one
            string cmdline = File.ReadAllText(Path.Combine(processDir, "cmdline"));
            string first = cmdline.Split('\0', 2)[0];
            string program = first.Replace('\\', '/');
            int slash = program.LastIndexOf('/');
            return (slash >= 0 ? program[(slash + 1)..] : program).Equals(ExeName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // the process ended in between, or belongs to another user
        }
    }

    /// <summary>The sockets a process holds: its file links that read "socket:[inode]".</summary>
    internal static void SocketInodes(int pid, HashSet<long> into)
    {
        string fds = Path.Combine(ProcRoot, pid.ToString(CultureInfo.InvariantCulture), "fd");
        try
        {
            foreach (string fd in Directory.EnumerateFileSystemEntries(fds))
            {
                string? target;
                try
                {
                    target = new FileInfo(fd).LinkTarget;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue; // closed in between
                }

                if (target is not null && SocketInode(target) is long inode)
                {
                    into.Add(inode);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the process ended, or is another user's
        }
    }

    /// <summary>"socket:[12345]" -> 12345; null for anything else.</summary>
    internal static long? SocketInode(string linkTarget) =>
        linkTarget.StartsWith("socket:[", StringComparison.Ordinal) && linkTarget.EndsWith(']')
            && long.TryParse(linkTarget.AsSpan(8, linkTarget.Length - 9), NumberStyles.None, CultureInfo.InvariantCulture, out long inode)
            ? inode : null;

    /// <summary>
    /// Adds the remote ports of the established connections in one of the kernel's tables whose socket is one of <paramref name="inodes"/>. A line reads
    /// <c>sl local_address rem_address st tx:rx tr:when retrnsmt uid timeout inode ...</c>, the addresses as hex "address:port", the state 01 for an
    /// established connection. False when the table could not be read.
    /// </summary>
    internal static bool ReadTable(string path, IReadOnlySet<long> inodes, ISet<int> ports)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        foreach (string line in lines.Skip(1))
        {
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 10 || f[3] != "01" || !long.TryParse(f[9], NumberStyles.None, CultureInfo.InvariantCulture, out long inode) || !inodes.Contains(inode))
            {
                continue;
            }

            int colon = f[2].LastIndexOf(':');
            if (colon > 0 && int.TryParse(f[2].AsSpan(colon + 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int port) && port is > 0 and not 80 and not 443)
            {
                ports.Add(port);
            }
        }

        return true;
    }
}
