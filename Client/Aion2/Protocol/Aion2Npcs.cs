using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Every monster's name by the NPC id the game sends when it appears
/// (assets/aion2/npcs/npc_names.json: {"2300709": {"en": "Talisra of the Void", "de": "..."}}, English,
/// German, French, Spanish and Russian). The bosses are in <see cref="Aion2BossCatalog"/> too; this covers the rest.
/// </summary>
public static class Aion2Npcs
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "npcs", "npc_names.json");
    private static readonly string SummonFilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "npcs", "player_summon_npcs.json");
    private static readonly string ModelFilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "npcs", "npc_models.json");
    private static Dictionary<int, Dictionary<string, string>>? _table;
    private static HashSet<int>? _playerSummons;
    private static Dictionary<int, string>? _models;

    /// <summary>
    /// The model a monster wears, as the game names it in lower case ("drakanelite_01", "lycan_04"), for its picture; null when it is not known
    /// (assets/aion2/npcs/npc_models.json, from the names of its spawns in the game client: Tools/monster-pictures/npc_models.py).
    /// </summary>
    public static string? ModelOf(int npcId) => (_models ??= LoadModels()).TryGetValue(npcId, out string? model) ? model : null;

    private static Dictionary<int, string> LoadModels()
    {
        var models = new Dictionary<int, string>();
        try
        {
            if (File.Exists(ModelFilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ModelFilePath));
                foreach (JsonProperty entry in doc.RootElement.GetProperty("models").EnumerateObject())
                {
                    if (int.TryParse(entry.Name, out int id) && entry.Value.GetString() is { Length: > 0 } model)
                    {
                        models[id] = model;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // No table: monsters keep the pictures found by their pet or their name.
        }

        return models;
    }

    /// <summary>True for a monster the NPC table knows that is not one of the player summons (pet, trap, aura, spirit):
    /// a spawn of it that names a player names its target (a Blazing Totem naming the player it burns), not an owner.
    /// False for the player summons, and for ids neither list knows.</summary>
    public static bool IsMonsterNotPlayerSummon(int npcId)
    {
        var table = _table ??= Load();
        var summons = _playerSummons ??= LoadSummons();
        return summons.Count > 0 && table.ContainsKey(npcId) && !summons.Contains(npcId);
    }

    /// <summary>True for a player summon (pet, trap, aura, spirit) of the list; false for everything else and when the list is missing.</summary>
    public static bool IsPlayerSummon(int npcId) => (_playerSummons ??= LoadSummons()).Contains(npcId);

    private static HashSet<int> LoadSummons()
    {
        try
        {
            if (File.Exists(SummonFilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SummonFilePath));
                return doc.RootElement.GetProperty("npcs").EnumerateArray().Select(e => e.GetInt32()).ToHashSet();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // No list: every named spawn stays an owner claim, as before.
        }

        return new HashSet<int>();
    }

    /// <summary>The monster's name in a language, else English; null when the NPC id is unknown.</summary>
    public static string? NameOf(int npcId, string language)
    {
        var table = _table ??= Load();
        return table.TryGetValue(npcId, out var names) ? names.GetValueOrDefault(language) ?? names.GetValueOrDefault("en") : null;
    }

    private static Dictionary<int, Dictionary<string, string>> Load()
    {
        var table = new Dictionary<int, Dictionary<string, string>>();
        try
        {
            if (File.Exists(FilePath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(FilePath)) ?? new();
                foreach ((string key, var names) in raw)
                {
                    if (int.TryParse(key, out int id))
                    {
                        table[id] = names;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // No table: monsters stay unnamed.
        }

        return table;
    }
}
