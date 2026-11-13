using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.Linux.Platform;

/// <summary>
/// The command line's "listen [seconds]": the capture alone, without the window - what it says and every hit it reads, one line each - to see from a
/// terminal whether the game's traffic arrives (the Linux workflow runs it against made-up game traffic on this machine). Writes no file.
/// </summary>
internal static class Listen
{
    public static int Run(string[] args)
    {
        int seconds = args.Length > 1 && int.TryParse(args[1], out int s) && s > 0 ? s : 30;
        Console.WriteLine(CaptureCheck.Summary());
        using var source = new Aion2PacketCombatSource(Aion2Protocol.Load());
        source.StatusChanged += status => Console.WriteLine($"status: {status.State} - {status.Message}");
        source.Start();
        int events = 0;
        DateTime until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(500);
            CombatBatch batch = source.Poll(paused: false);
            foreach (DamageEvent ev in batch.Damage)
            {
                events++;
                string who = source.Entities.NameFor(ev.SourceObjectId) ?? ev.SourceObjectId.ToString();
                Console.WriteLine($"event: {ev.Timestamp:HH:mm:ss.fff} {who} -> {ev.TargetObjectId}: {ev.Skill} {ev.Amount}{(ev.IsCritical ? " crit" : "")}{(ev.IsHeal ? " heal" : "")}");
            }
        }

        Console.WriteLine($"listen: {events} hit(s) read in {seconds} s" + (source.ServerFingerprint is { } server ? $" from {server}" : ""));
        return 0;
    }
}
