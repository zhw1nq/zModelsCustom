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
    public Dictionary<string, WeaponCollection> Weapons { get; set; } = new();

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

            using var doc = JsonDocument.Parse(json);
            var config = new WeaponModelsConfig();

            if (doc.RootElement.TryGetProperty("Weapons", out var weaponsElement))
            {
                foreach (var collectionProp in weaponsElement.EnumerateObject())
                {
                    var collection = ParseCollection(collectionProp.Value);
                    config.Weapons[collectionProp.Name] = collection;
                }
            }

            return config.Weapons.Count > 0 ? config : new WeaponModelsConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error loading weapon models: {ex.Message}");
            return new WeaponModelsConfig();
        }
    }

    private static WeaponCollection ParseCollection(JsonElement element)
    {
        var collection = new WeaponCollection();

        foreach (var prop in element.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "image":
                    collection.Image = prop.Value.GetString() ?? "";
                    break;
                case "name":
                    collection.Name = prop.Value.GetString() ?? "";
                    break;
                default:
                    // weapon_xxx properties - contains dictionary of skins
                    if (prop.Name.StartsWith("weapon_"))
                    {
                        var weaponType = prop.Name;
                        var skinsList = new List<WeaponModelData>();

                        // Iterate through each skin in this weapon type
                        foreach (var skinProp in prop.Value.EnumerateObject())
                        {
                            var weaponData = JsonSerializer.Deserialize<WeaponModelData>(skinProp.Value.GetRawText(), JsonOptions);
                            if (weaponData != null)
                            {
                                weaponData.WeaponType = weaponType;
                                weaponData.SkinKey = skinProp.Name; // e.g., "AK-47 Zaomeng"
                                skinsList.Add(weaponData);
                            }
                        }

                        if (skinsList.Count > 0)
                        {
                            collection.WeaponItems[weaponType] = skinsList;
                        }
                    }
                    break;
            }
        }

        return collection;
    }

    private static string GetModelsPath(string moduleDirectory) =>
        Path.Combine(moduleDirectory, "../../configs/plugins/zModelsCustom/zWeapons.json");

    private static WeaponModelsConfig CreateDefaultModels(string path)
    {
        var defaultJson = @"{
  ""Weapons"": {
    ""example_collection"": {
      ""image"": ""example.png"",
      ""name"": ""Example Collection"",
      ""weapon_ak47"": {
        ""name"": ""AK-47 Example"",
        ""uniqueid"": ""ak47_example"",
        ""model"": ""weapons/models/example/ak47_example.vmdl"",
        ""image_gun"": ""ak47_example.png""
      }
    }
  }
}";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, defaultJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error creating default weapon models: {ex.Message}");
        }

        return new WeaponModelsConfig();
    }

    public static void CreateDefault(string path)
    {
        CreateDefaultModels(path);
    }

    public WeaponModelData? FindModelByUniqueId(string uniqueId)
    {
        foreach (var collection in Weapons.Values)
        {
            foreach (var skins in collection.WeaponItems.Values)
            {
                foreach (var weapon in skins)
                {
                    if (weapon.UniqueId == uniqueId)
                        return weapon;
                }
            }
        }
        return null;
    }

    public string GetModelNameByUniqueId(string uniqueId)
    {
        foreach (var collection in Weapons.Values)
        {
            foreach (var skins in collection.WeaponItems.Values)
            {
                foreach (var weapon in skins)
                {
                    if (weapon.UniqueId == uniqueId)
                        return weapon.Name;
                }
            }
        }
        return uniqueId;
    }

    public string? GetWeaponTypeByUniqueId(string uniqueId)
    {
        foreach (var collection in Weapons.Values)
        {
            foreach (var skins in collection.WeaponItems.Values)
            {
                foreach (var weapon in skins)
                {
                    if (weapon.UniqueId == uniqueId)
                        return weapon.WeaponType;
                }
            }
        }
        return null;
    }

    public (string CollectionName, WeaponCollection Collection)? GetCollectionByUniqueId(string uniqueId)
    {
        foreach (var kvp in Weapons)
        {
            foreach (var skins in kvp.Value.WeaponItems.Values)
            {
                foreach (var weapon in skins)
                {
                    if (weapon.UniqueId == uniqueId)
                        return (kvp.Key, kvp.Value);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Get total count of all skins across all collections
    /// </summary>
    public int GetTotalSkinsCount()
    {
        return Weapons.Values
            .SelectMany(c => c.WeaponItems.Values)
            .Sum(skins => skins.Count);
    }
}

public class WeaponCollection
{
    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // Weapons in this collection - each weapon type can have multiple skins
    // Key: weapon_type (e.g., "weapon_ak47")
    // Value: List of skins for that weapon type
    [JsonIgnore]
    public Dictionary<string, List<WeaponModelData>> WeaponItems { get; set; } = new();

    /// <summary>
    /// Get total count of skins in this collection
    /// </summary>
    public int GetSkinsCount() => WeaponItems.Values.Sum(s => s.Count);
}

public class WeaponModelData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("uniqueid")]
    public string UniqueId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("image_gun")]
    public string ImageGun { get; set; } = "";

    // Set programmatically based on the JSON property name (weapon_ak47, etc.)
    [JsonIgnore]
    public string WeaponType { get; set; } = "";

    // The key name in JSON (e.g., "AK-47 Zaomeng")
    [JsonIgnore]
    public string SkinKey { get; set; } = "";

    /// <summary>
    /// Extracts the subclass name from the model path.
    /// Example: "phase2/weapons/models/2en0w/ak47_zaomeng/ak47_zaomeng_ag2.vmdl" => "ak47_zaomeng_ag2"
    /// </summary>
    public string GetSubclassName()
    {
        if (string.IsNullOrEmpty(Model))
            return "";

        var fileName = Path.GetFileNameWithoutExtension(Model);
        return fileName;
    }
}

// Trails Config (zTrails.json) - CS2-Store compatible
public partial class TrailsConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"//.*|/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CommentPattern();

    [JsonPropertyName("Trail")]
    public Dictionary<string, Dictionary<string, TrailData>> Categories { get; set; } = new();

    public static TrailsConfig Load(string moduleDirectory)
    {
        var path = GetConfigPath(moduleDirectory);

        if (!File.Exists(path))
            return CreateDefault(path);

        try
        {
            var json = File.ReadAllText(path);
            json = CommentPattern().Replace(json, "");
            var config = JsonSerializer.Deserialize<TrailsConfig>(json, JsonOptions);
            return config ?? new TrailsConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error loading trails config: {ex.Message}");
            return new TrailsConfig();
        }
    }

    private static string GetConfigPath(string moduleDirectory) =>
        Path.Combine(moduleDirectory, "../../configs/plugins/zModelsCustom/zTrails.json");

    private static TrailsConfig CreateDefault(string path)
    {
        var config = new TrailsConfig
        {
            Categories = new()
            {
                ["Player Trail"] = new()
                {
                    ["Energycirc Trail"] = new TrailData
                    {
                        UniqueId = "energycircltrail",
                        Model = "particles/ui/status_levels/ui_status_level_8_energycirc.vpcf",
                        Lifetime = 1.3f,
                        AcceptInputValue = "Start",
                        AngleValue = "90 0 0"
                    },
                    ["Random color trail"] = new TrailData
                    {
                        UniqueId = "trailrandomcolor",
                        Lifetime = 1.3f,
                        WidthValue = 1.0f,
                        Color = ""
                    },
                    ["Green color trail"] = new TrailData
                    {
                        UniqueId = "trailgreencolor",
                        Lifetime = 1.3f,
                        WidthValue = 1.0f,
                        Color = "0 255 0"
                    }
                }
            }
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error creating default trails config: {ex.Message}");
        }

        return config;
    }

    public TrailData? FindByUniqueId(string uniqueId)
    {
        foreach (var category in Categories.Values)
        {
            foreach (var trail in category.Values)
            {
                if (trail.UniqueId == uniqueId)
                    return trail;
            }
        }
        return null;
    }
}

public class TrailData
{
    [JsonPropertyName("uniqueid")]
    public string UniqueId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = ""; // Particle model (.vpcf) - empty for beam trails

    [JsonPropertyName("color")]
    public string Color { get; set; } = ""; // "R G B" - empty for random/particle

    [JsonPropertyName("lifetime")]
    public float Lifetime { get; set; } = 1.3f;

    [JsonPropertyName("widthValue")]
    public float WidthValue { get; set; } = 1.0f;

    [JsonPropertyName("acceptInputValue")]
    public string AcceptInputValue { get; set; } = "Start";

    [JsonPropertyName("angleValue")]
    public string AngleValue { get; set; } = "90 0 0";

    // Helper to determine if this is a particle or beam trail
    [JsonIgnore]
    public bool IsParticle => !string.IsNullOrEmpty(Model);
}

// Tracers Config (zTracers.json) - CS2-Store compatible
public partial class TracersConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex(@"//.*|/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CommentPattern();

    [JsonPropertyName("Tracer")]
    public Dictionary<string, TracerData> Tracers { get; set; } = new();

    public static TracersConfig Load(string moduleDirectory)
    {
        var path = GetConfigPath(moduleDirectory);

        if (!File.Exists(path))
            return CreateDefault(path);

        try
        {
            var json = File.ReadAllText(path);
            json = CommentPattern().Replace(json, "");
            var config = JsonSerializer.Deserialize<TracersConfig>(json, JsonOptions);
            return config ?? new TracersConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error loading tracers config: {ex.Message}");
            return new TracersConfig();
        }
    }

    private static string GetConfigPath(string moduleDirectory) =>
        Path.Combine(moduleDirectory, "../../configs/plugins/zModelsCustom/zTracers.json");

    private static TracersConfig CreateDefault(string path)
    {
        var config = new TracersConfig
        {
            Tracers = new()
            {
                ["Energycirc Tracer"] = new TracerData
                {
                    UniqueId = "energycirctracer",
                    Model = "particles/ui/status_levels/ui_status_level_8_energycirc.vpcf",
                    Lifetime = 0.3f,
                    AcceptInputValue = "Start"
                }
            }
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[zModelsCustom] Error creating default tracers config: {ex.Message}");
        }

        return config;
    }

    public TracerData? FindByUniqueId(string uniqueId) =>
        Tracers.Values.FirstOrDefault(t => t.UniqueId == uniqueId);
}

public class TracerData
{
    [JsonPropertyName("uniqueid")]
    public string UniqueId { get; set; } = "";

    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("lifetime")]
    public float Lifetime { get; set; } = 0.3f;

    [JsonPropertyName("acceptInputValue")]
    public string AcceptInputValue { get; set; } = "Start";
}
