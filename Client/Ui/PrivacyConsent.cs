namespace AionDPS.Ui;

/// <summary>
/// The player's answer to the privacy policy (<see cref="MeterSettings.PrivacyDecision"/>): nothing is uploaded without a current "accepted"; a "declined" also puts the meter in its
/// restricted mode (see <see cref="Restricted"/>); closing the question without answering is no answer (asked again at the next start). The policy has a version: when its text adds something, <see cref="Version"/> is raised here (and on the website's privacy page, in all
/// languages) and everybody who accepted is asked once more. Keep the three in step - see the project memory.
/// </summary>
public static partial class PrivacyConsent
{
    /// <summary>Raise with every change of the privacy texts that adds to or widens what is used or sent (website page and the client's texts): the accepted
    /// version in the settings is then too old and the player is asked again. A change that only takes something out leaves it (nothing new to agree to).</summary>
    public const int Version = 6; // 3: the map accuracy data (5 November 2026); 4: the Genus and pets for the player window, the pictures of the game window (5 November 2026);
                                  // 5: every upload switched on with the answer (5 November 2026, the owner: "all these should be ticked since we requested consent");
                                  // 6: the map accuracy data names the game window's size and the minimap's zoom (10 November 2026);
                                  // still 6: the map accuracy data and the pictures for the minimap and the game windows taken out (13 November 2026)

    public const int NotAsked = 0, Accepted = 1, Declined = 2;

    public const string PolicyUrl = Brand.Site + "/terms";

    /// <summary>Raised after the answer changed (the main window shows or hides what the restricted mode removes).</summary>
    public static event Action? Changed;

    /// <summary>The accepted version is the current one.</summary>
    public static bool IsAccepted(MeterSettings s) => s.PrivacyDecision == Accepted && s.PrivacyVersion >= Version;

    /// <summary>Anything may go online: the player accepted the current policy.</summary>
    public static bool CanUpload => IsAccepted(MeterSettings.Load());

    /// <summary>The player declined: the meter shows only the DPS list and the tools that need no server and no other player's data.</summary>
    public static bool Restricted => MeterSettings.Load().PrivacyDecision == Declined;

    /// <summary>Never asked, or accepted an older version of the policy.</summary>
    public static bool NeedsAsking(MeterSettings s) => s.PrivacyDecision == NotAsked || (s.PrivacyDecision == Accepted && s.PrivacyVersion < Version);

    /// <summary>The tools that stay in the restricted mode (the others need uploads, the server or other players' data).</summary>
    public static readonly IReadOnlySet<string> RestrictedTools = new HashSet<string> { "checklist", "attributes", "cpcalc", "loot", "craft", "timetable", "theme", "network" };

    /// <summary>The answer was written by somebody else (the first-start wizard): the main window re-reads it.</summary>
    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>Records the answer. "Accepted" switches every upload the terms name on (each can be switched off in Settings, Upload); "declined" every one off.</summary>
    public static void Record(bool accepted)
    {
        MeterSettings s = MeterSettings.Load();
        s.PrivacyDecision = accepted ? Accepted : Declined;
        s.PrivacyVersion = accepted ? Version : 0;
        if (accepted)
        {
            s.AutoUploadProfile = true;
            s.UploadOtherPlayersProfiles = true;
            s.AutoUploadBoss = true;
            s.UploadReplay = true;
            s.ShareCpMeasurements = true;
        }
        else
        {
            s.AutoUploadBoss = false;
            s.AutoUploadProfile = false;
            s.UploadOtherPlayersProfiles = false;
            s.ShareLoginWithLegion = false;
            s.ShareCpMeasurements = false;
        }

        s.Save();
        Changed?.Invoke();
    }
}
