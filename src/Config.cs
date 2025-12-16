using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace zModelsCustom;

public partial class Config
{
    [JsonPropertyName("database")]
    public DatabaseConfig DatabaseConfig { get; set; } = new();

    [JsonPropertyName("reload_commands")]
    public List<string> ReloadCommands { get; set; } = new() { "reloadmodels", "rlmodels" };

    [JsonPropertyName("website_url")]
    public string WebsiteUrl { get; set; } = "https://example.com/models";

    public static Config Load(string moduleDirectory)
    {
        var path = GetConfigPath(moduleDirectory);
        var configDir = Path.GetDirectoryName(path)!;

        // Ensure directory exists
        Directory.CreateDirectory(configDir);

        var config = File.Exists(path)
            ? LoadExistingConfig(path)
            : CreateDefaultConfig(path);

        // Ensure player models config exists
        var modelsPath = Path.Combine(configDir, "zModels.json");
        if (!File.Exists(modelsPath))
        {
            PlayerModelsConfig.CreateDefault(modelsPath);
        }

        // Ensure weapon models config exists
        var weaponsPath = Path.Combine(configDir, "zWeapons.json");
        if (!File.Exists(weaponsPath))
        {
            WeaponModelsConfig.CreateDefault(weaponsPath);
        }

        return config;
    }

    private static Config LoadExistingConfig(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error loading config: {ex.Message}");
            return new Config();
        }
    }

    private static string GetConfigPath(string moduleDirectory) =>
        Path.Combine(moduleDirectory, "../../configs/plugins/zModelsCustom/zConfig.json");

    private static Config CreateDefaultConfig(string path)
    {
        var defaultConfig = new Config();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error creating default config: {ex.Message}");
        }

        return defaultConfig;
    }
}

public class DatabaseConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 3306;

    [JsonPropertyName("database")]
    public string Database { get; set; } = "cs2";

    [JsonPropertyName("user")]
    public string User { get; set; } = "root";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}

// Player Models Config (zModels.json)
public partial class PlayerModelsConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"//.*|/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CommentPattern();

    [JsonPropertyName("Categories")]
    public Dictionary<string, Dictionary<string, PlayerModelData>> Categories { get; set; } = new();

    public static PlayerModelsConfig Load(string moduleDirectory)
    {
        var path = GetModelsPath(moduleDirectory);

        if (!File.Exists(path))
        {
            return CreateDefaultModels(path);
        }

        try
        {
            var json = File.ReadAllText(path);
            json = CommentPattern().Replace(json, "");

            var config = JsonSerializer.Deserialize<PlayerModelsConfig>(json, JsonOptions);
            return config?.Categories?.Count > 0 ? config : new PlayerModelsConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error loading player models: {ex.Message}");
            return new PlayerModelsConfig();
        }
    }

    private static string GetModelsPath(string moduleDirectory) =>
        Path.Combine(moduleDirectory, "../../configs/plugins/zModelsCustom/zModels.json");

    private static PlayerModelsConfig CreateDefaultModels(string path)
    {
        var defaultModels = new PlayerModelsConfig
        {
            Categories = new()
            {
                ["Example Category"] = new()
                {
                    ["Example Model"] = new()
                    {
                        UniqueId = "example_model",
                        Model = "models/player/example.mdl",
                        Slot = "ALL",
                        DisableLeg = false
                    }
                }
            }
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(defaultModels, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error creating default player models: {ex.Message}");
        }

        return defaultModels;
    }

    public static void CreateDefault(string path)
    {
        CreateDefaultModels(path);
    }

    public PlayerModelData? FindModelByUniqueId(string uniqueId)
    {
        foreach (var category in Categories.Values)
        {
            foreach (var model in category.Values)
            {
                if (model.UniqueId == uniqueId)
                    return model;
            }
        }
        return null;
    }

    public string GetModelNameByUniqueId(string uniqueId)
    {
        foreach (var category in Categories.Values)
        {
            foreach (var kvp in category)
            {
                if (kvp.Value.UniqueId == uniqueId)
                    return kvp.Key;
            }
        }
        return uniqueId;
    }
}

public class PlayerModelData
{
    [JsonPropertyName("uniqueid")]
    public string UniqueId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("armModel")]
    public string? ArmModel { get; set; }

    [JsonPropertyName("slot")]
    public string Slot { get; set; } = "ALL";

    [JsonPropertyName("disable_leg")]
    public bool DisableLeg { get; set; }
}

// Weapon Models Config (zWeapons.json)
public partial class WeaponModelsConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"//.*|/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CommentPattern();

    [JsonPropertyName("Weapons")]
    public Dictionary<string, Dictionary<string, WeaponModelData>> Weapons { get; set; } = new();

    public static WeaponModelsConfig Load(string moduleDirectory)
    {
        var path = GetModelsPath(moduleDirectory);

        if (!File.Exists(path))
        {
            return CreateDefaultModels(path);
        }

        try
        {
            var json = File.ReadAllText(path);
            json = CommentPattern().Replace(json, "");

            var config = JsonSerializer.Deserialize<WeaponModelsConfig>(json, JsonOptions);
            return config?.Weapons?.Count > 0 ? config : new WeaponModelsConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error loading weapon models: {ex.Message}");
            return new WeaponModelsConfig();
        }
    }

    private static string GetModelsPath(string moduleDirectory) =>
        Path.Combine(moduleDirectory, "../../configs/plugins/zModelsCustom/zWeapons.json");

    private static WeaponModelsConfig CreateDefaultModels(string path)
    {
        var defaultModels = new WeaponModelsConfig
        {
            Weapons = new()
            {
                ["weapon_ak47"] = new()
                {
                    ["Example AK47 Skin"] = new()
                    {
                        UniqueId = "ak47_example",
                        Model = "weapons/models/example_ak47.vmdl",
                        CustomName = "Example Skin"
                    }
                }
            }
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(defaultModels, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error creating default weapon models: {ex.Message}");
        }

        return defaultModels;
    }

    public static void CreateDefault(string path)
    {
        CreateDefaultModels(path);
    }

    public WeaponModelData? FindModelByUniqueId(string uniqueId)
    {
        foreach (var weapon in Weapons.Values)
        {
            foreach (var model in weapon.Values)
            {
                if (model.UniqueId == uniqueId)
                    return model;
            }
        }
        return null;
    }

    public string GetModelNameByUniqueId(string uniqueId)
    {
        foreach (var weapon in Weapons.Values)
        {
            foreach (var kvp in weapon)
            {
                if (kvp.Value.UniqueId == uniqueId)
                    return kvp.Key;
            }
        }
        return uniqueId;
    }

    public string? GetWeaponTypeByUniqueId(string uniqueId)
    {
        foreach (var weaponKvp in Weapons)
        {
            foreach (var model in weaponKvp.Value.Values)
            {
                if (model.UniqueId == uniqueId)
                    return weaponKvp.Key;
            }
        }
        return null;
    }
}

public class WeaponModelData
{
    [JsonPropertyName("uniqueid")]
    public string UniqueId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("custom_name")]
    public string CustomName { get; set; } = "";
}
