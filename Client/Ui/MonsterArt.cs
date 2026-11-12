using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// A monster's picture for the pages that list monsters (11 November 2026, the owner: "grind for example it's missing the mob icons"): a boss's own picture, else
/// the picture of the pet the monster gives (the game's pets are its monsters: the Ribbit's pet is a Ribbit), found by its id or its name in any language, else
/// the picture of the model it wears (13 November 2026, the owner: "a lot of icons are missing from grind": the Abyss's elite Drakans, the Blades, the
/// Draconutes give no pet; <see cref="PictureModel"/>); without any, a tile with its initial, so every row has one. From the image pack the app downloads.
/// </summary>
public static class MonsterArt
{
    private static readonly Dictionary<int, string?> Paths = new();
    private static Dictionary<string, string>? _modelPictures;
    private static string? _picturesOf;

    /// <summary>The file of the monster's picture, null when there is none.</summary>
    public static string? PathOf(int npcId)
    {
        if (npcId <= 0)
        {
            return null;
        }

        lock (Paths)
        {
            // a newer image pack came in: the pictures are looked for again
            if (_picturesOf != GameArt.PackFolder)
            {
                Paths.Clear();
                _modelPictures = null;
                _picturesOf = GameArt.PackFolder;
            }

            if (Paths.TryGetValue(npcId, out string? known))
            {
                return known;
            }
        }

        string? path = Aion2Artwork.BossPicturePath(npcId);
        if (path is null)
        {
            foreach (int pet in Aion2Pets.PetsOfNpc(npcId))
            {
                if (Aion2Pets.PetIconPath(pet) is { } found)
                {
                    path = found;
                    break;
                }
            }
        }

        if (path is null && Aion2Npcs.NameOf(npcId, "en") is { Length: > 0 } name)
        {
            foreach (int pet in Aion2Pets.PetsOfMonsterName(name))
            {
                if (Aion2Pets.PetIconPath(pet) is { } found)
                {
                    path = found;
                    break;
                }
            }
        }

        if (path is null)
        {
            Dictionary<string, string> pictures;
            lock (Paths)
            {
                pictures = _modelPictures ??= ModelPictures();
            }

            if (PictureModel(Aion2Npcs.ModelOf(npcId), Aion2Npcs.NameOf(npcId, "en"), pictures.Keys) is { } model)
            {
                path = pictures[model];
            }
        }

        lock (Paths)
        {
            Paths[npcId] = path;
        }

        return path;
    }

    /// <summary>The pictures of the game's monster models the image pack has, by model in lower case: the pets' (a pet's key is the model of its monster)
    /// and the named monsters' portraits (bosses/field/mob_&lt;model&gt;.png).</summary>
    private static Dictionary<string, string> ModelPictures()
    {
        var pictures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (int pet in Aion2Pets.AllPetIds)
        {
            if (Aion2Pets.PetKey(pet) is { } key && Aion2Pets.PetIconPath(pet) is { } file)
            {
                pictures.TryAdd(key.ToLowerInvariant(), file);
            }
        }

        string field = GameArt.FolderOf("aion2", "bosses", "field");
        if (Directory.Exists(field))
        {
            foreach (string file in Directory.EnumerateFiles(field, "mob_*.png"))
            {
                pictures.TryAdd(Path.GetFileNameWithoutExtension(file)[4..].ToLowerInvariant(), file);
            }
        }

        return pictures;
    }

    // families of models that hold different creatures: BeritraC is a Drakan soldier, a guard dog and a probe; DranaVar the Drana mutant, brute and
    // predator. Their monsters take only a picture of their own model.
    private static readonly HashSet<string> MixedFamilies = new(StringComparer.Ordinal) { "beritraa", "beritrac", "beritrad", "dranavar", "kraoelite", "spider" };

    // the races whose models begin alike (KrallWar, KrallArc, KrallElite are all Krall), and the word of a monster's name that says its race
    private static readonly (string Models, string[] Words)[] Races =
    {
        ("drakan", new[] { "Drakan" }), ("krall", new[] { "Krall" }), ("draco", new[] { "Draconute" }), ("lycan", new[] { "Mau" }),
        ("addshulack", new[] { "Shulak" }), ("owl", new[] { "Aullaeu", "Urugugu" }), ("skurv", new[] { "Mumu" }), ("virota", new[] { "Biruta" }),
        ("sura", new[] { "Sura" }), ("naga", new[] { "Naga", "Nagarant" }), ("dratona", new[] { "Dratona" }), ("kuru", new[] { "Kuru" }),
        ("duduka", new[] { "Duduka" }), ("pino", new[] { "Pino" }), ("manduri", new[] { "Manduri" }), ("kalnif", new[] { "Karnif" }),
        ("ursuss", new[] { "Ursus" }), ("daeva", new[] { "Daeva" }),
    };

    // words of names that are no creature: such a monster takes only a picture of its own model (the Blades Defense Cannon is an AltKnife, as the Blades are)
    private static readonly HashSet<string> NotCreatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cannon", "Totem", "Turret", "Trap", "Aura", "Stone", "Root", "Reactor", "Apparatus", "Device", "Ballista", "Catapult", "Barricade", "Flag",
        "Banner", "Gate", "Crystal", "Egg", "Colony", "Nest", "Cocoon", "Tower", "Altar", "Pillar", "Statue", "Seal", "Orb", "Core", "Bomb", "Mine",
        "Generator", "Machine", "Petrification",
    };

    /// <summary>
    /// The model whose picture a monster gets, among the models with pictures (<paramref name="pictures"/>, lower case), from the model it wears
    /// (<paramref name="model"/>, lower case) and its English name: the same model; else the same model in another colour (_v01, _cv01); else, for a
    /// creature, another model of its family (AltKnife_04 for the AltKnife_01 of a Blades Combatant), of its race (a Krall for the KrallArc of a Chief's
    /// Sentinel), or the race its name says (Abyss Drakan Brawler: a Drakan). Families that hold different creatures (<see cref="MixedFamilies"/>) and
    /// things that are no creature (cannons, totems ...) take only their own model. Null when nothing fits: the row draws the monster's initial.
    /// </summary>
    internal static string? PictureModel(string? model, string? englishName, IReadOnlyCollection<string> pictures)
    {
        static string Base(string m) => Regex.Match(m, "^[a-z]+_[0-9]{2}").Value is { Length: > 0 } b ? b : m;
        static string Family(string m) => Regex.Match(m, "^[a-z]+").Value;
        var sorted = pictures.Order(StringComparer.Ordinal).ToList();
        string[] words = englishName is null ? Array.Empty<string>() : Regex.Matches(englishName, "[A-Za-z]+").Select(w => w.Value).ToArray();
        bool creature = !words.Any(NotCreatures.Contains);
        string? OfRace(string race) => sorted.FirstOrDefault(p => Family(p).StartsWith(race, StringComparison.Ordinal) && !MixedFamilies.Contains(Family(p)));
        if (model is { Length: > 0 })
        {
            model = model.ToLowerInvariant();
            if (pictures.Contains(model))
            {
                return model;
            }

            if (sorted.FirstOrDefault(p => Base(p) == Base(model)) is { } colour)
            {
                return colour;
            }

            string family = Family(model);
            if (creature && !MixedFamilies.Contains(family))
            {
                if (sorted.FirstOrDefault(p => Family(p) == family) is { } kin)
                {
                    return kin;
                }

                if (Races.Where(r => family.StartsWith(r.Models, StringComparison.Ordinal)).OrderByDescending(r => r.Models.Length).Select(r => OfRace(r.Models))
                    .FirstOrDefault(p => p is not null) is { } race)
                {
                    return race;
                }
            }
        }

        if (creature)
        {
            foreach ((string models, string[] names) in Races)
            {
                if (names.Any(words.Contains))
                {
                    return OfRace(models);
                }
            }
        }

        return null;
    }

    private static readonly Dictionary<string, ImageSource?> NamePictures = new();
    private static string? _namePicturesLanguage;

    /// <summary>
    /// The picture of a pet (its monster) or of a collectible, found by the name a row shows in the app's language (the pet list, the pet information over a
    /// monster); null when there is none.
    /// </summary>
    public static ImageSource? PictureOfName(string name)
    {
        string language = LocalizationManager.Instance.Language;
        lock (NamePictures)
        {
            if (_namePicturesLanguage != language)
            {
                NamePictures.Clear();
                _namePicturesLanguage = language;
            }

            if (NamePictures.TryGetValue(name, out ImageSource? known))
            {
                return known;
            }
        }

        string folded = Aion2Pets.Fold(name);
        string? path = null;
        foreach (int pet in Aion2Pets.AllPetIds)
        {
            if (Aion2Pets.PetName(pet, language) is { } petName && Aion2Pets.Fold(petName) == folded && Aion2Pets.PetIconPath(pet) is { } found)
            {
                path = found;
                break;
            }
        }

        if (path is null)
        {
            foreach (int pet in Aion2Pets.PetsOfMonsterName(name))
            {
                if (Aion2Pets.PetIconPath(pet) is { } found)
                {
                    path = found;
                    break;
                }
            }
        }

        if (path is null)
        {
            foreach (var item in Aion2Gather.Items())
            {
                if (Aion2Pets.Fold(item.NameIn(language)) == folded && Aion2Gather.IconPath(item.Key) is { } found)
                {
                    path = found;
                    break;
                }
            }
        }

        ImageSource? picture = OverlayPictures.Load(path, 48);
        lock (NamePictures)
        {
            NamePictures[name] = picture;
        }

        return picture;
    }

    /// <summary>The monster's picture as a rounded tile of <paramref name="size"/>; without one, its initial on a tile of a colour of its own.</summary>
    public static FrameworkElement Icon(int npcId, string name, double size = 32)
    {
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24), VerticalAlignment = VerticalAlignment.Center, SnapsToDevicePixels = true };
        if (OverlayPictures.Load(PathOf(npcId), (int)Math.Ceiling(size * 2)) is { } picture)
        {
            tile.Background = new ImageBrush(picture) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top };
            tile.BorderThickness = new Thickness(1);
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            tile.ToolTip = name;
            return tile;
        }

        // no picture: its initial, on a colour taken from its name (the same monster always the same)
        uint hash = 2166136261;
        foreach (char ch in name)
        {
            hash = (hash ^ ch) * 16777619;
        }

        double hue = hash % 360;
        Color tint = FromHue(hue, 0.42, 0.34), ink = FromHue(hue, 0.55, 0.78);
        tile.Background = new SolidColorBrush(tint);
        tile.BorderThickness = new Thickness(1);
        tile.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, ink.R, ink.G, ink.B));
        string initial = name.Trim() is { Length: > 0 } t ? char.ToUpperInvariant(t[0]).ToString() : "?";
        tile.Child = new TextBlock
        {
            Text = initial, FontSize = size * 0.46, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(ink),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        return tile;
    }

    private static Color FromHue(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = lightness - c / 2;
        (double r, double g, double b) = hue switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
