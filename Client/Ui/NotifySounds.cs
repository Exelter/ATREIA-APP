using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace AionDPS.Ui;

/// <summary>The short notification sounds (assets/sounds/*.wav) of the timetable reminders: the list for the settings and playing one at a volume on the chosen audio output.</summary>
public static class NotifySounds
{
    /// <summary>The ids of the sounds, in the order the settings offer them.</summary>
    public static readonly string[] All = { "chime", "bell", "ping", "blip", "drop", "horn", "warhorn", "fanfare", "blades", "roar", "bounce" };

    private static readonly List<MediaPlayer> Playing = new();

    /// <summary>Plays a sound (volume 0 to 100); an unknown id plays nothing.</summary>
    public static void Play(string id, int volume)
    {
        if (All.Contains(id))
        {
            PlayFile(id, volume);
        }
    }

    /// <summary>The Hidden Cube arrival chime (three rising notes, unlike any radar beep): not one of the sounds to choose from, it always goes with the cube sound.
    /// The idea is Stacy's (her fork, 17 October 2026).</summary>
    public static void PlayCubeArrival(int volume) => PlayFile("cube-arrival", volume);

    /// <summary>The Hidden Cube "out of sight" sound (three falling notes, made by Tools/make_cube_left_sound.py): a cube went out of range, the player moves away from it.</summary>
    public static void PlayCubeLeft(int volume) => PlayFile("cube-left", volume);

    /// <summary>
    /// How loud a volume of the settings (0 to 100) plays: as the ear hears it, not straight (15 November 2026, a player: "turned this down to the lowest but its
    /// still super loud"; at 5 the player's volume was 0.05 of the sound, which the ear still hears plainly). The square of it: 70 is -6 dB, half way -12 dB,
    /// 20 -28 dB, 10 -40 dB, the lowest step (5) -52 dB.
    /// </summary>
    public static double Gain(int volume)
    {
        double share = Math.Clamp(volume, 0, 100) / 100.0;
        return share * share;
    }

    private static readonly object CacheGate = new();

    /// <summary>
    /// The sound at a volume: a copy of its file with the samples turned down by <see cref="Gain"/> (kept in the temporary folder, made once), played at the
    /// player's full volume - so how loud it is does not depend on how the player turns its volume into loudness. Null: the file is not plain 16-bit PCM, or the
    /// copy cannot be written (the player's volume does it then).
    /// </summary>
    private static string? QuieterCopy(string path, string id, int volume)
    {
        try
        {
            var source = new FileInfo(path);
            string folder = Path.Combine(Path.GetTempPath(), "AtreiaApp-sounds");
            string copy = Path.Combine(folder, $"{id}-{volume}-{source.Length:x}-{source.LastWriteTimeUtc.Ticks:x}.wav");
            lock (CacheGate)
            {
                if (File.Exists(copy))
                {
                    return copy;
                }

                byte[] wav = File.ReadAllBytes(path);
                if (!Scale(wav, Gain(volume)))
                {
                    return null;
                }

                Directory.CreateDirectory(folder);
                string part = copy + ".part";
                File.WriteAllBytes(part, wav);
                File.Move(part, copy, overwrite: true);
                return copy;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Turns the samples of a 16-bit PCM WAV down in place; false for any other kind of file.</summary>
    internal static bool Scale(byte[] wav, double gain)
    {
        if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F' || wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
        {
            return false;
        }

        bool pcm16 = false;
        int at = 12;
        while (at + 8 <= wav.Length)
        {
            string chunk = System.Text.Encoding.ASCII.GetString(wav, at, 4);
            int size = BitConverter.ToInt32(wav, at + 4);
            int body = at + 8;
            if (size < 0 || body + size > wav.Length)
            {
                size = wav.Length - body; // a length past the end (some writers): up to the end
            }

            if (chunk == "fmt " && size >= 16)
            {
                pcm16 = BitConverter.ToUInt16(wav, body) == 1 && BitConverter.ToUInt16(wav, body + 14) == 16;
            }
            else if (chunk == "data")
            {
                if (!pcm16)
                {
                    return false;
                }

                for (int i = body; i + 1 < body + size; i += 2)
                {
                    int sample = (int)Math.Round(BitConverter.ToInt16(wav, i) * gain);
                    short scaled = (short)Math.Clamp(sample, short.MinValue, short.MaxValue);
                    wav[i] = (byte)scaled;
                    wav[i + 1] = (byte)(scaled >> 8);
                }

                return true;
            }

            at = body + size + (size & 1); // chunks are padded to an even length
        }

        return false;
    }

    private static void PlayFile(string id, int volume)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", id + ".wav");
        if (AudioOutput.Muted || !File.Exists(path) || volume <= 0)
        {
            return;
        }

        string device = AudioOutput.DeviceId;
        _ = Task.Run(async () =>
        {
            try
            {
                // the samples turned down to the volume, played at full volume (else the player's volume, the same curve)
                string? quieter = volume >= 100 ? path : QuieterCopy(path, id, volume);
                string file = quieter ?? path;
                var player = new MediaPlayer { Volume = quieter is not null ? 1.0 : Gain(volume) };
                void Done()
                {
                    lock (Playing)
                    {
                        Playing.Remove(player);
                    }

                    player.Dispose();
                }

                player.MediaEnded += (_, _) => Done();
                player.MediaFailed += (_, _) => Done();
                lock (Playing)
                {
                    Playing.Add(player); // kept until it ends, else it would be collected mid-sound
                }

                await AudioOutput.ApplyAsync(player, device);
                player.Source = MediaSource.CreateFromUri(new Uri(file));
                player.Play();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // no audio device: the meter goes on without the sound
            }
        });
    }
}
