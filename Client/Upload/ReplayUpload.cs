using AionDPS.Combat;

namespace AionDPS.Upload;

/// <summary>One path for the replay: the player, boss or other monster, and where it was (centimetres) at moments in milliseconds after <see cref="StartedAt"/> (UTC).</summary>
public sealed record ReplayTrackUpload(string Kind, string Name, string? ClassName, bool IsSelf, DateTime StartedAt, IReadOnlyList<int> T, IReadOnlyList<int> X, IReadOnlyList<int> Y);

/// <summary>The "Upload for the replay": the paths of one boss fight. <see cref="EncounterId"/> is the fight on the server as the boss upload answered it.</summary>
public sealed record ReplayUploadRequest(
    string ClientVersion,
    string ServerFingerprint,
    int? EncounterId,
    int? BossNpcId,
    DateTime StartedAt,
    DateTime EndedAt,
    IReadOnlyList<ReplayTrackUpload> Tracks,
    string Game = "aion2",
    IReadOnlyList<ReplayAreaUpload>? Areas = null,
    IReadOnlyList<ReplayHitUpload>? Hits = null);

/// <summary>
/// A boss's attack for the replay on the fight's page (11 November 2026, the owner: "for the replay of the boss fights, make sure to add all the boss animations, and
/// the actual boss, not just a ball and a bar loading"): who winds it up (the name of its path), the skill, its wind-up in milliseconds after the fight's start, the
/// spot it aims at and the way it faces (degrees, atan2 of y and x, centimetres as the paths), and its shape on the floor as the app's own replay draws it (circle,
/// ring, cone, line, cross or room; sizes in centimetres). <see cref="Zone"/>: the reach of an object the boss left on the floor while it stays; <see cref="Sized"/>:
/// the size rests on measurements, not on a guess.
/// </summary>
public sealed record ReplayAreaUpload(int StartMs, int EndMs, string Actor, string Skill, int SkillId, string Shape, int X, int Y, double Facing, int Radius,
    int? Inner = null, double? Half = null, int? Length = null, int? Width = null, int? Behind = null, bool Zone = false, bool Sized = false);

/// <summary>A boss's hit on a player whose path goes with the replay (milliseconds after the fight's start), for the ring on whoever was caught.</summary>
public sealed record ReplayHitUpload(int AtMs, string Victim, string Skill);

/// <summary>A path as the server hands it out, the paths of every client of the fight merged: <see cref="StartMs"/> is the first point's time (UTC, ms since 1970), the times count from it.</summary>
public sealed record ServerReplayTrack(string Kind, string Name, string? ClassName, long StartMs, int[] T, int[] X, int[] Y, int Sources);

/// <summary>Turns the replay map's data into the upload.</summary>
public static class ReplayUploadBuilder
{
    /// <summary>The most points one path carries (the backend takes 6,000).</summary>
    public const int MaxPoints = 4000;

    /// <summary>Readings closer in time than this are one for the upload.</summary>
    private const int MinGapMs = 500;

    /// <summary>
    /// The paths of the replay. The own character's path and the monsters' always go; another player's path only with <paramref name="includeOthers"/> (the switch
    /// "data of other players" of the settings), so without it nothing of anybody else's position leaves the PC.
    /// </summary>
    public static IReadOnlyList<ReplayTrackUpload> Tracks(ReplayData data, bool includeOthers)
    {
        var list = new List<ReplayTrackUpload>();
        foreach (ReplayEntity e in data.Entities)
        {
            if (e.Role == ReplayRole.Player && !e.IsSelf && !includeOthers)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(e.Name) || e.Track.T.Length == 0)
            {
                continue;
            }

            var t = new List<int>();
            var x = new List<int>();
            var y = new List<int>();
            for (int i = 0; i < e.Track.T.Length && t.Count < MaxPoints; i++)
            {
                if (t.Count > 0 && e.Track.T[i] - t[^1] < MinGapMs)
                {
                    continue;
                }

                t.Add(e.Track.T[i]);
                x.Add(e.Track.X[i]);
                y.Add(e.Track.Y[i]);
            }

            // (an object of a boss's mechanics, an orb or a pool, is "mech" since 2.0.2: the site drew it as a second boss)
            list.Add(new ReplayTrackUpload(e.Role switch { ReplayRole.Player => "player", ReplayRole.Add => "add", ReplayRole.Mechanic => "mech", _ => "boss" }, e.Name,
                e.Role == ReplayRole.Player ? e.ClassName : null, e.IsSelf, e.Track.Start.ToUniversalTime(), t, x, y));
        }

        return list;
    }

    /// <summary>The most attacks and hits one replay carries (the site keeps as many).</summary>
    public const int MaxAreas = 1500, MaxHits = 3000;

    /// <summary>
    /// The bosses' attacks of the replay and their hits, on the clock of the fight's upload (<paramref name="fightStart"/>): every wind-up of a boss or of an object of its
    /// mechanics with its shape, and the reach of what it left on the floor. A hit only on a player whose path goes too (the own character's; another player's only with
    /// <paramref name="includeOthers"/>, the switch "data of other players"), so nobody else's whereabouts leave the PC without it.
    /// </summary>
    public static (IReadOnlyList<ReplayAreaUpload> Areas, IReadOnlyList<ReplayHitUpload> Hits) Attacks(ReplayData data, DateTime fightStart, bool includeOthers)
    {
        double shift = (data.Start.ToUniversalTime() - fightStart.ToUniversalTime()).TotalMilliseconds;
        int Ms(double seconds) => (int)Math.Round(seconds * 1000 + shift);
        var names = data.Entities.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var areas = new List<ReplayAreaUpload>();
        foreach (ReplayArea a in data.Areas.OrderBy(a => a.Start))
        {
            if (areas.Count >= MaxAreas || !names.TryGetValue(a.Actor, out ReplayEntity? actor) || string.IsNullOrWhiteSpace(actor.Name)
                || !double.IsFinite(a.X) || !double.IsFinite(a.Y) || !double.IsFinite(a.Facing))
            {
                continue;
            }

            AionDPS.Aion2.Protocol.MechanicArea? shape = a.Shape;
            string kind = shape?.Shape is "ring" or "cone" or "line" or "cross" or "room" ? shape.Shape : "circle";
            int? Cm(double? v) => v is double d && double.IsFinite(d) ? (int)Math.Round(d) : null;
            areas.Add(new ReplayAreaUpload(Ms(a.Start), Ms(Math.Max(a.End, a.Start)), actor.Name, a.SkillName, a.SkillId, kind, (int)Math.Round(a.X), (int)Math.Round(a.Y),
                Math.Round(a.Facing, 1), (int)Math.Round(a.Radius), Cm(shape?.InnerCm), shape?.HalfAngle, Cm(shape?.LengthCm), Cm(shape?.WidthCm), Cm(shape?.BehindCm), a.Zone, a.Sized));
        }

        var hits = new List<ReplayHitUpload>();
        foreach (ReplayHit h in data.Hits.OrderBy(h => h.At))
        {
            if (hits.Count >= MaxHits || !names.TryGetValue(h.Victim, out ReplayEntity? victim) || victim.Role != ReplayRole.Player || string.IsNullOrWhiteSpace(victim.Name)
                || (!victim.IsSelf && !includeOthers))
            {
                continue;
            }

            hits.Add(new ReplayHitUpload(Ms(h.At), victim.Name, AionDPS.Aion2.Protocol.Aion2SkillNames.NameOf(h.SkillId)));
        }

        return (areas, hits);
    }
}
