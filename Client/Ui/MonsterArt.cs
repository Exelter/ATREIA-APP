using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Ui;

/// <summary>
/// A monster's picture for the pages that list monsters (11 November 2026, the owner: "grind for example it's missing the mob icons"): a boss's own picture, else
/// the picture of the pet the monster gives (the game's pets are its monsters: the Ribbit's pet is a Ribbit), found by its id or its name in any language; without
/// either, a tile with its initial, so every row has one. From the image pack the app downloads.
/// </summary>
public static class MonsterArt
{
    private static readonly Dictionary<int, string?> Paths = new();

    /// <summary>The file of the monster's picture, null when there is none.</summary>
    public static string? PathOf(int npcId)
    {
        if (npcId <= 0)
        {
            return null;
        }

        lock (Paths)
        {
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

        lock (Paths)
        {
            Paths[npcId] = path;
        }

        return path;
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
