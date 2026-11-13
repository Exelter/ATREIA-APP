namespace AionDPS.Ui;

/// <summary>One audio output the system offers, as the settings list it.</summary>
public sealed record AudioDevice(string Id, string Name);

/// <summary>
/// Linux counterpart of the Windows app's AudioOutput (Client/Ui/AudioOutput.cs, which asks Windows' media devices): the sound output the meter's sounds
/// play on. The Linux app has no sounds yet, so there is nothing to list; the settings still keep the Windows app's choice (the shared settings file).
/// </summary>
public static class AudioOutput
{
    /// <summary>The chosen device; empty = the system's default.</summary>
    public static string DeviceId { get; set; } = "";

    /// <summary>Every sound of the meter off.</summary>
    public static bool Muted { get; set; }

    /// <summary>The audio outputs the system has: none listed yet on Linux.</summary>
    public static IReadOnlyList<AudioDevice> Devices() => Array.Empty<AudioDevice>();
}
