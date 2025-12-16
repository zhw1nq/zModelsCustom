using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace zModelsCustom;

public class zModelsCustom : BasePlugin
{
    public override string ModuleName => "zModelsCustom";
    public override string ModuleVersion => "1.0.0";

    public static zModelsCustom Instance { get; private set; } = null!;
    public static Config Config { get; private set; } = null!;
    public static Database Database { get; private set; } = null!;
    public static ModelManager ModelManager { get; private set; } = null!;
    public static WeaponManager WeaponManager { get; private set; } = null!;

    private readonly ConcurrentDictionary<ulong, ReloadInfo> _reloadTracking = new();

    public override void Load(bool hotReload)
    {
        Instance = this;
        Config = Config.Load(ModuleDirectory);
        Database = new Database(Config.DatabaseConfig);
        ModelManager = new ModelManager();
        WeaponManager = new WeaponManager();

        // Player model events
        RegisterEventHandler<EventPlayerSpawn>(ModelManager.OnPlayerSpawn);
        
        // Weapon events
        RegisterListener<Listeners.OnEntityCreated>(WeaponManager.OnEntityCreated);
        RegisterEventHandler<EventItemEquip>(WeaponManager.OnItemEquip);
        
        // Common events
        RegisterEventHandler<EventPlayerConnectFull>(Database.OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        RegisterCommands();

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player?.IsBot == false && player.AuthorizedSteamID != null)
                {
                    _ = Database.PreloadAllPlayerDataAsync(player.SteamID);
                }
            }
        }
    }

    private void RegisterCommands()
    {
        foreach (var cmd in Config.ReloadCommands)
        {
            AddCommand($"css_{cmd}", "Reload models configuration", Command_ReloadModels);
        }

        // Unified Web API commands (Console only)
        AddCommand("css_webquery", "Apply model/weapon to player via web (Console only)", Command_WebQuery);
        AddCommand("css_weblogin", "Display web login notification (Console only)", Command_WebLogin);
        AddCommand("css_webdelete", "Remove player model/weapon via web (Console only)", Command_WebDelete);

        // Website commands
        var websiteCommands = new[] { "svip", "vip", "md", "mds" };
        foreach (var cmd in websiteCommands)
        {
            AddCommand($"css_{cmd}", "Open models website", Command_ModelsWebsite);
        }
    }

    private void Command_ReloadModels(CCSPlayerController? player, CommandInfo info)
    {
        try
        {
            var newPlayerModels = PlayerModelsConfig.Load(ModuleDirectory);
            var newWeaponModels = WeaponModelsConfig.Load(ModuleDirectory);
            
            ModelManager.PrecacheModels(newPlayerModels);
            WeaponManager.PrecacheModels(newWeaponModels);
            WeaponManager.UpdateModelsConfig(newWeaponModels);

            var playerCategoriesCount = newPlayerModels.Categories.Count;
            var playerTotalModels = newPlayerModels.Categories.Values.Sum(c => c.Count);
            var weaponCount = newWeaponModels.Weapons.Count;
            var weaponTotalModels = newWeaponModels.Weapons.Values.Sum(w => w.Count);

            var successMessage = Localizer["zModelsCustom.reload_success", 
                playerCategoriesCount, playerTotalModels, weaponCount, weaponTotalModels];

            if (player?.IsValid == true)
            {
                player.PrintToChat(Localizer["zModelsCustom.prefix"] + successMessage);
            }

            Server.PrintToConsole($"[zModelsCustom] Reloaded: {playerCategoriesCount} player categories ({playerTotalModels} models), {weaponCount} weapon types ({weaponTotalModels} skins)");
        }
        catch (Exception ex)
        {
            var errorMessage = Localizer["zModelsCustom.reload_error", ex.Message];

            if (player?.IsValid == true)
            {
                player.PrintToChat(Localizer["zModelsCustom.prefix"] + errorMessage);
            }

            Server.PrintToConsole($"[zModelsCustom] Error reloading: {ex.Message}");
        }
    }

    // css_webquery <type> <steamid> <uniqueid> <site/weapon>
    private void Command_WebQuery(CCSPlayerController? player, CommandInfo info)
    {
        // Console only command
        if (player != null)
        {
            if (player.IsValid)
            {
                player.PrintToChat(Localizer["zModelsCustom.prefix"] +
                    Localizer["zModelsCustom.console_only"]);
            }
            return;
        }

        if (info.ArgCount < 5)
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_webquery <type> <steamid> <uniqueid> <site/weapon>");
            Server.PrintToConsole("[zModelsCustom] Type: 'model' or 'weapon'");
            Server.PrintToConsole("[zModelsCustom] For model: site can be 't', 'ct', or 'all'");
            Server.PrintToConsole("[zModelsCustom] For weapon: weapon name like 'weapon_ak47' or 'all'");
            return;
        }

        var type = info.GetArg(1).ToLowerInvariant();
        var steamIdStr = info.GetArg(2);
        var uniqueId = info.GetArg(3);
        var target = info.GetArg(4).ToLowerInvariant();

        if (!ulong.TryParse(steamIdStr, out var steamId))
        {
            Server.PrintToConsole($"[zModelsCustom] Invalid SteamID: {steamIdStr}");
            return;
        }

        switch (type)
        {
            case "model":
                if (target != "t" && target != "ct" && target != "all")
                {
                    Server.PrintToConsole($"[zModelsCustom] Invalid site: {target}. Must be 't', 'ct', or 'all'");
                    return;
                }
                _ = ProcessModelWebQuery(steamId, uniqueId, target);
                break;

            case "weapon":
                _ = ProcessWeaponWebQuery(steamId, uniqueId, target);
                break;

            default:
                Server.PrintToConsole($"[zModelsCustom] Invalid type: {type}. Must be 'model' or 'weapon'");
                break;
        }
    }

    private async Task ProcessModelWebQuery(ulong steamId, string uniqueId, string site)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            if (targetPlayer == null)
            {
                Server.NextFrame(() =>
                    Server.PrintToConsole($"[zModelsCustom] Player with SteamID {steamId} not found or not connected"));
                return;
            }

            var modelsConfig = PlayerModelsConfig.Load(ModuleDirectory);
            var model = modelsConfig.FindModelByUniqueId(uniqueId);

            if (model == null)
            {
                Server.NextFrame(() =>
                    Server.PrintToConsole($"[zModelsCustom] Model with UniqueID '{uniqueId}' not found in configuration"));
                return;
            }

            List<CsTeam> teamsToApply = new();

            if (site == "all")
            {
                teamsToApply.Add(CsTeam.Terrorist);
                teamsToApply.Add(CsTeam.CounterTerrorist);
            }
            else if (site == "t")
            {
                teamsToApply.Add(CsTeam.Terrorist);
            }
            else if (site == "ct")
            {
                teamsToApply.Add(CsTeam.CounterTerrorist);
            }

            foreach (var team in teamsToApply)
            {
                await Database.SavePlayerModelAsync(steamId, team, uniqueId);
            }

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];
                var modelName = modelsConfig.GetModelNameByUniqueId(uniqueId);
                var siteDisplay = site.ToUpperInvariant();

                if (targetPlayer.IsValid && targetPlayer.PlayerPawn.Value != null && teamsToApply.Contains(targetPlayer.Team))
                {
                    ModelManager.ApplyModel(targetPlayer, model);
                    targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_model_applied_site", modelName, siteDisplay]}");
                    Server.PrintToConsole($"[zModelsCustom] Applied model '{uniqueId}' to player {steamId} ({targetPlayer.PlayerName}) for site: {siteDisplay}");
                }
                else
                {
                    targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_model_saved_site", modelName, siteDisplay]}");
                    Server.PrintToConsole($"[zModelsCustom] Model '{uniqueId}' saved for player {steamId} for site: {siteDisplay}. Will apply on next spawn.");
                }
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in model webquery: {ex.Message}"));
        }
    }

    private async Task ProcessWeaponWebQuery(ulong steamId, string uniqueId, string weapon)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            if (targetPlayer == null)
            {
                Server.NextFrame(() =>
                    Server.PrintToConsole($"[zModelsCustom] Player with SteamID {steamId} not found or not connected"));
                return;
            }

            var modelsConfig = WeaponModelsConfig.Load(ModuleDirectory);
            var model = modelsConfig.FindModelByUniqueId(uniqueId);

            if (model == null)
            {
                Server.NextFrame(() =>
                    Server.PrintToConsole($"[zModelsCustom] Weapon model with UniqueID '{uniqueId}' not found in configuration"));
                return;
            }

            List<string> weaponsToApply = new();

            if (weapon == "all")
            {
                weaponsToApply.AddRange(modelsConfig.Weapons.Keys);
            }
            else
            {
                weaponsToApply.Add(weapon);
            }

            foreach (var weaponName in weaponsToApply)
            {
                await Database.SavePlayerWeaponAsync(steamId, weaponName, uniqueId);
            }

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];
                var modelName = modelsConfig.GetModelNameByUniqueId(uniqueId);
                var weaponDisplay = weapon.ToUpperInvariant().Replace("WEAPON_", "");

                if (targetPlayer.IsValid && targetPlayer.PlayerPawn.Value != null)
                {
                    WeaponManager.RefreshPlayerWeapons(targetPlayer);
                    targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_weapon_applied", modelName, weaponDisplay]}");
                    Server.PrintToConsole($"[zModelsCustom] Applied weapon model '{uniqueId}' to player {steamId} ({targetPlayer.PlayerName}) for weapon: {weaponDisplay}");
                }
                else
                {
                    targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_weapon_saved", modelName, weaponDisplay]}");
                    Server.PrintToConsole($"[zModelsCustom] Weapon model '{uniqueId}' saved for player {steamId} for weapon: {weaponDisplay}. Will apply on next equip.");
                }
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in weapon webquery: {ex.Message}"));
        }
    }

    private void Command_WebLogin(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null)
        {
            if (player.IsValid)
            {
                player.PrintToChat(Localizer["zModelsCustom.prefix"] +
                    Localizer["zModelsCustom.console_only"]);
            }
            return;
        }

        if (info.ArgCount < 3)
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_weblogin <steamid> <json_oneline>");
            return;
        }

        var steamIdStr = info.GetArg(1);
        var fullCommand = info.GetCommandString;
        var parts = fullCommand.Split(new[] { ' ' }, 3);

        if (parts.Length < 3)
        {
            Server.PrintToConsole("[zModelsCustom] Missing JSON data");
            return;
        }

        var jsonData = parts[2].Trim();

        if (!ulong.TryParse(steamIdStr, out var steamId))
        {
            Server.PrintToConsole($"[zModelsCustom] Invalid SteamID: {steamIdStr}");
            return;
        }

        ProcessWebLogin(steamId, jsonData);
    }

    private void ProcessWebLogin(ulong steamId, string jsonData)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var loginData = JsonSerializer.Deserialize<WebLoginResponse>(jsonData, options);

            if (loginData?.Success != true || loginData.Info == null)
            {
                Server.PrintToConsole($"[zModelsCustom] Invalid login data for SteamID {steamId}");
                return;
            }

            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            if (targetPlayer == null)
            {
                Server.PrintToConsole($"[zModelsCustom] Player with SteamID {steamId} not found or not connected");
                return;
            }

            var info = loginData.Info;
            var prefix = Localizer["zModelsCustom.prefix"];

            targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_login_success"]}");
            targetPlayer.PrintToChat($" {Localizer["zModelsCustom.web_login_time", info.Time]}");
            targetPlayer.PrintToChat($" {Localizer["zModelsCustom.web_login_location", info.Country, info.City]}");
            targetPlayer.PrintToChat($" {Localizer["zModelsCustom.web_login_domain", info.Domain]}");

            Server.PrintToConsole($"[zModelsCustom] Web login notification sent to {targetPlayer.PlayerName} (SteamID: {steamId})");
        }
        catch (JsonException ex)
        {
            Server.PrintToConsole($"[zModelsCustom] JSON parse error: {ex.Message}");
            Server.PrintToConsole($"[zModelsCustom] Received data: {jsonData}");
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error processing web login: {ex.Message}");
        }
    }

    // css_webdelete <type> <steamid> <site/weapon>
    private void Command_WebDelete(CCSPlayerController? player, CommandInfo info)
    {
        if (player != null)
        {
            if (player.IsValid)
            {
                player.PrintToChat(Localizer["zModelsCustom.prefix"] +
                    Localizer["zModelsCustom.console_only"]);
            }
            return;
        }

        if (info.ArgCount < 4)
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_webdelete <type> <steamid> <site/weapon>");
            Server.PrintToConsole("[zModelsCustom] Type: 'model' or 'weapon'");
            return;
        }

        var type = info.GetArg(1).ToLowerInvariant();
        var steamIdStr = info.GetArg(2);
        var target = info.GetArg(3).ToLowerInvariant();

        if (!ulong.TryParse(steamIdStr, out var steamId))
        {
            Server.PrintToConsole($"[zModelsCustom] Invalid SteamID: {steamIdStr}");
            return;
        }

        switch (type)
        {
            case "model":
                if (target != "t" && target != "ct" && target != "all")
                {
                    Server.PrintToConsole($"[zModelsCustom] Invalid site: {target}. Must be 't', 'ct', or 'all'");
                    return;
                }
                _ = ProcessModelWebDelete(steamId, target);
                break;

            case "weapon":
                _ = ProcessWeaponWebDelete(steamId, target);
                break;

            default:
                Server.PrintToConsole($"[zModelsCustom] Invalid type: {type}. Must be 'model' or 'weapon'");
                break;
        }
    }

    private async Task ProcessModelWebDelete(ulong steamId, string site)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            if (site == "all")
            {
                await Database.RemovePlayerModelAsync(steamId, CsTeam.Terrorist);
                await Database.RemovePlayerModelAsync(steamId, CsTeam.CounterTerrorist);

                Server.NextFrame(() =>
                {
                    var prefix = Localizer["zModelsCustom.prefix"];

                    if (targetPlayer?.IsValid == true && targetPlayer.PlayerPawn.Value != null)
                    {
                        ModelManager.ResetModel(targetPlayer);
                    }

                    if (targetPlayer?.IsValid == true)
                    {
                        targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_model_removed_all"]}");
                    }

                    Server.PrintToConsole($"[zModelsCustom] Removed all models for player {steamId}");
                });
            }
            else
            {
                var team = site == "t" ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
                await Database.RemovePlayerModelAsync(steamId, team);

                Server.NextFrame(() =>
                {
                    var prefix = Localizer["zModelsCustom.prefix"];
                    var teamName = site.ToUpperInvariant();

                    if (targetPlayer?.IsValid == true &&
                        targetPlayer.PlayerPawn.Value != null &&
                        targetPlayer.Team == team)
                    {
                        ModelManager.ResetModel(targetPlayer);
                    }

                    if (targetPlayer?.IsValid == true)
                    {
                        targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_model_removed_team", teamName]}");
                    }

                    Server.PrintToConsole($"[zModelsCustom] Removed {teamName} model for player {steamId}");
                });
            }
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in model webdelete: {ex.Message}"));
        }
    }

    private async Task ProcessWeaponWebDelete(ulong steamId, string weapon)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            if (weapon == "all")
            {
                await Database.RemoveAllPlayerWeaponsAsync(steamId);

                Server.NextFrame(() =>
                {
                    var prefix = Localizer["zModelsCustom.prefix"];

                    if (targetPlayer?.IsValid == true && targetPlayer.PlayerPawn.Value != null)
                    {
                        WeaponManager.RefreshPlayerWeapons(targetPlayer);
                    }

                    if (targetPlayer?.IsValid == true)
                    {
                        targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_weapon_removed_all"]}");
                    }

                    Server.PrintToConsole($"[zModelsCustom] Removed all weapon models for player {steamId}");
                });
            }
            else
            {
                await Database.RemovePlayerWeaponAsync(steamId, weapon);

                Server.NextFrame(() =>
                {
                    var prefix = Localizer["zModelsCustom.prefix"];
                    var weaponDisplay = weapon.ToUpperInvariant().Replace("WEAPON_", "");

                    if (targetPlayer?.IsValid == true && targetPlayer.PlayerPawn.Value != null)
                    {
                        WeaponManager.RefreshPlayerWeapons(targetPlayer);
                    }

                    if (targetPlayer?.IsValid == true)
                    {
                        targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_weapon_removed", weaponDisplay]}");
                    }

                    Server.PrintToConsole($"[zModelsCustom] Removed {weaponDisplay} model for player {steamId}");
                });
            }
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in weapon webdelete: {ex.Message}"));
        }
    }

    private void Command_ModelsWebsite(CCSPlayerController? player, CommandInfo info)
    {
        if (player?.IsValid != true) return;

        var steamId = player.SteamID;
        var currentTime = Server.CurrentTime;

        var reloadInfo = _reloadTracking.GetOrAdd(steamId, _ => new ReloadInfo());

        lock (reloadInfo)
        {
            reloadInfo.CommandHistory.Add(currentTime);
            reloadInfo.CommandHistory.RemoveAll(t => currentTime - t > 15.0f);

            if (reloadInfo.CommandHistory.Count >= 3)
            {
                Server.PrintToConsole(Localizer["zModelsCustom.console_kick_spam",
                    player.PlayerName, steamId]);
                Server.ExecuteCommand($"kickid {player.UserId} \"{Localizer["zModelsCustom.kick_reason_spam"]}\"");

                reloadInfo.CommandHistory.Clear();
                return;
            }

            player.PrintToChat($" {Localizer["zModelsCustom.prefix"]}" +
                $"{Localizer["zModelsCustom.website_message", Config.WebsiteUrl]}");

            var timeSinceLastReload = currentTime - reloadInfo.LastReloadTime;
            const float cooldownSeconds = 120.0f;

            if (timeSinceLastReload < cooldownSeconds)
            {
                var remainingCooldown = (int)(cooldownSeconds - timeSinceLastReload);
                player.PrintToChat(Localizer["zModelsCustom.prefix"] +
                    Localizer["zModelsCustom.cooldown_remaining", remainingCooldown]);
                return;
            }

            reloadInfo.LastReloadTime = currentTime;

            try
            {
                var newPlayerModels = PlayerModelsConfig.Load(ModuleDirectory);
                var newWeaponModels = WeaponModelsConfig.Load(ModuleDirectory);
                
                ModelManager.PrecacheModels(newPlayerModels);
                WeaponManager.PrecacheModels(newWeaponModels);
                WeaponManager.UpdateModelsConfig(newWeaponModels);

                var categoriesCount = newPlayerModels.Categories.Count;
                var totalModels = newPlayerModels.Categories.Values.Sum(c => c.Count);

                player.PrintToChat(Localizer["zModelsCustom.prefix"] +
                    Localizer["zModelsCustom.reload_success_player", categoriesCount, totalModels]);

                Server.PrintToConsole($"[zModelsCustom] Reload triggered by {player.PlayerName}");
            }
            catch (Exception ex)
            {
                player.PrintToChat(Localizer["zModelsCustom.prefix"] +
                    Localizer["zModelsCustom.reload_error", ex.Message]);

                Server.PrintToConsole($"[zModelsCustom] Error reloading: {ex.Message}");
            }
        }
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsBot != false) return HookResult.Continue;

        var steamId = player.SteamID;

        ModelManager.CleanupInspectEntities(steamId);
        Database.ClearPlayerCache(steamId);
        WeaponManager.ClearPlayerData(steamId);
        _reloadTracking.TryRemove(steamId, out _);

        return HookResult.Continue;
    }

    public override void Unload(bool hotReload)
    {
        Database?.Dispose();
        _reloadTracking.Clear();
    }

    private sealed class ReloadInfo
    {
        public float LastReloadTime { get; set; }
        public List<float> CommandHistory { get; } = new();
    }
}

// JSON models for web login
public class WebLoginResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("info")]
    public WebLoginInfo? Info { get; set; }
}

public class WebLoginInfo
{
    [JsonPropertyName("country")]
    public string Country { get; set; } = "";

    [JsonPropertyName("city")]
    public string City { get; set; } = "";

    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";
}

// Helper class for thread-safe HashSet
public class ConcurrentHashSet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary = new();

    public void Add(T item) => _dictionary.TryAdd(item, 0);

    public bool TryRemove(T item) => _dictionary.TryRemove(item, out _);

    public void Clear() => _dictionary.Clear();
}
