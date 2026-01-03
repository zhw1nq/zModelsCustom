using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
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
    public static SmokeManager SmokeManager { get; private set; } = null!;
    public static EffectsManager EffectsManager { get; private set; } = null!;

    private readonly ConcurrentDictionary<ulong, ReloadInfo> _reloadTracking = new();

    public override void Load(bool hotReload)
    {
        Instance = this;
        Config = Config.Load(ModuleDirectory);
        Database = new Database(Config.DatabaseConfig);
        ModelManager = new ModelManager();
        WeaponManager = new WeaponManager();
        SmokeManager = new SmokeManager();
        EffectsManager = new EffectsManager();
        EffectsManager.Initialize(ModuleDirectory);

        // Player model events
        RegisterEventHandler<EventPlayerSpawn>(ModelManager.OnPlayerSpawn);
        
        // Weapon events
        RegisterListener<Listeners.OnEntityCreated>(OnEntityCreated);
        RegisterEventHandler<EventItemEquip>(WeaponManager.OnItemEquip);
        
        // Common events
        RegisterEventHandler<EventPlayerConnectFull>(Database.OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        
        // Effects events (Trail/Tracer)
        RegisterListener<Listeners.OnTick>(EffectsManager.OnGameFrame);
        RegisterEventHandler<EventBulletImpact>(EffectsManager.OnBulletImpact);

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
        AddCommand("css_webreload", "Reload config and refresh all players (Console only)", Command_WebReload);

        // Website commands
        var websiteCommands = new[] { "svip", "vip", "md", "mds" };
        foreach (var cmd in websiteCommands)
        {
            AddCommand($"css_{cmd}", "Open models website", Command_ModelsWebsite);
        }
    }

    [RequiresPermissions("@css/root")]
    private void Command_ReloadModels(CCSPlayerController? player, CommandInfo info)
    {
        try
        {
            // Reload configs
            var newPlayerModels = PlayerModelsConfig.Load(ModuleDirectory);
            var newWeaponModels = WeaponModelsConfig.Load(ModuleDirectory);
            
            ModelManager.PrecacheModels(newPlayerModels);
            WeaponManager.PrecacheModels(newWeaponModels);
            WeaponManager.UpdateModelsConfig(newWeaponModels);

            var playerCategoriesCount = newPlayerModels.Categories.Count;
            var playerTotalModels = newPlayerModels.Categories.Values.Sum(c => c.Count);
            var weaponCount = newWeaponModels.Weapons.Count;
            var weaponTotalModels = newWeaponModels.GetTotalSkinsCount();

            Server.PrintToConsole($"[zModelsCustom] Reloaded: {playerCategoriesCount} player categories ({playerTotalModels} models), {weaponCount} collections ({weaponTotalModels} skins)");

            // Refresh all connected players with their DB skins
            var players = Utilities.GetPlayers().Where(p => p?.IsBot == false && p.IsValid).ToList();
            int refreshedCount = 0;

            foreach (var targetPlayer in players)
            {
                try
                {
                    WeaponManager.RefreshPlayerWeapons(targetPlayer);
                    refreshedCount++;
                }
                catch (Exception ex)
                {
                    Server.PrintToConsole($"[zModelsCustom] Error refreshing {targetPlayer.PlayerName}: {ex.Message}");
                }
            }

            var successMessage = Localizer["zModelsCustom.reload_success", 
                playerCategoriesCount, playerTotalModels, weaponCount, weaponTotalModels];

            if (player?.IsValid == true)
            {
                player.PrintToChat(Localizer["zModelsCustom.prefix"] + successMessage);
                player.PrintToChat(Localizer["zModelsCustom.prefix"] + $" Refreshed {refreshedCount} players");
            }

            Server.PrintToConsole($"[zModelsCustom] Refreshed {refreshedCount} players with their DB skins");
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

        if (info.ArgCount < 4)
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_webquery <type> <steamid> <uniqueid> [target]");
            Server.PrintToConsole("[zModelsCustom] Type: 'model', 'weapon', or 'smoke'");
            Server.PrintToConsole("[zModelsCustom] For model: target can be 't', 'ct', or 'all'");
            Server.PrintToConsole("[zModelsCustom] For weapon/smoke: target is optional");
            return;
        }

        var type = info.GetArg(1).ToLowerInvariant();
        var steamIdStr = info.GetArg(2);
        var uniqueId = info.GetArg(3);
        var target = info.ArgCount >= 5 ? info.GetArg(4).ToLowerInvariant() : "";

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
                _ = ProcessWeaponWebQuery(steamId, uniqueId);
                break;

            case "smoke":
                _ = ProcessSmokeWebQuery(steamId, uniqueId);
                break;

            case "trail":
                _ = ProcessTrailWebQuery(steamId, uniqueId);
                break;

            case "tracer":
                _ = ProcessTracerWebQuery(steamId, uniqueId);
                break;

            default:
                Server.PrintToConsole($"[zModelsCustom] Invalid type: {type}. Must be 'model', 'weapon', 'smoke', 'trail', or 'tracer'");
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

    private async Task ProcessWeaponWebQuery(ulong steamId, string uniqueId)
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

            // Get the weapon type from the model's WeaponType property
            string weaponTypeToApply = model.WeaponType;

            if (string.IsNullOrEmpty(weaponTypeToApply))
            {
                Server.NextFrame(() =>
                    Server.PrintToConsole($"[zModelsCustom] Weapon model '{uniqueId}' has no weapon type defined"));
                return;
            }

            // Save the weapon skin to database
            await Database.SavePlayerWeaponAsync(steamId, weaponTypeToApply, uniqueId);

            // Update cache
            WeaponManager.UpdatePlayerWeaponCache(steamId, weaponTypeToApply, uniqueId);

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];
                var modelName = modelsConfig.GetModelNameByUniqueId(uniqueId);
                var weaponDisplay = weaponTypeToApply.ToUpperInvariant().Replace("WEAPON_", "");

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

    private async Task ProcessSmokeWebQuery(ulong steamId, string color)
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

            // Save to database and update cache
            await Database.SavePlayerSmokeColorAsync(steamId, color);
            SmokeManager.SetPlayerSmokeColor(steamId, color);

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];
                var colorDisplay = color == "random" ? "Rainbow" : color;

                targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_smoke_applied", colorDisplay]}");
                Server.PrintToConsole($"[zModelsCustom] Applied smoke color '{color}' to player {steamId} ({targetPlayer.PlayerName})");
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in smoke webquery: {ex.Message}"));
        }
    }

    private async Task ProcessTrailWebQuery(ulong steamId, string uniqueId)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            if (targetPlayer == null)
            {
                Server.NextFrame(() =>
                    Server.PrintToConsole($"[zModelsCustom] Player with SteamID {steamId} not found"));
                return;
            }

            await Database.SavePlayerTrailAsync(steamId, uniqueId);
            EffectsManager.SetPlayerTrail(steamId, uniqueId);

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];
                targetPlayer.PrintToChat($" {prefix} Trail applied: {uniqueId}");
                Server.PrintToConsole($"[zModelsCustom] Applied trail '{uniqueId}' to player {steamId}");
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in trail webquery: {ex.Message}"));
        }
    }

    private async Task ProcessTracerWebQuery(ulong steamId, string uniqueId)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            if (targetPlayer == null)
            {
                Server.NextFrame(() =>
                    Server.PrintToConsole($"[zModelsCustom] Player with SteamID {steamId} not found"));
                return;
            }

            await Database.SavePlayerTracerAsync(steamId, uniqueId);
            EffectsManager.SetPlayerTracer(steamId, uniqueId);

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];
                targetPlayer.PrintToChat($" {prefix} Tracer applied: {uniqueId}");
                Server.PrintToConsole($"[zModelsCustom] Applied tracer '{uniqueId}' to player {steamId}");
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in tracer webquery: {ex.Message}"));
        }
    }

    // Central entity created handler - routes to appropriate managers
    private void OnEntityCreated(CEntityInstance entity)
    {
        // Route to WeaponManager for weapon entities
        WeaponManager.OnEntityCreated(entity);
        
        // Route to SmokeManager for smoke grenades
        SmokeManager.OnEntityCreated(entity);
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

            case "smoke":
                _ = ProcessSmokeWebDelete(steamId);
                break;

            case "trail":
                _ = ProcessTrailWebDelete(steamId);
                break;

            case "tracer":
                _ = ProcessTracerWebDelete(steamId);
                break;

            default:
                Server.PrintToConsole($"[zModelsCustom] Invalid type: {type}. Must be 'model', 'weapon', 'smoke', 'trail', or 'tracer'");
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

    private async Task ProcessSmokeWebDelete(ulong steamId)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            await Database.RemovePlayerSmokeColorAsync(steamId);
            SmokeManager.RemovePlayerSmokeColor(steamId);

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];

                if (targetPlayer?.IsValid == true)
                {
                    targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_smoke_removed"]}");
                }

                Server.PrintToConsole($"[zModelsCustom] Removed smoke color for player {steamId}");
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in smoke webdelete: {ex.Message}"));
        }
    }

    private async Task ProcessTrailWebDelete(ulong steamId)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            await Database.RemovePlayerTrailAsync(steamId);
            EffectsManager.RemovePlayerTrail(steamId);

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];
                if (targetPlayer?.IsValid == true)
                    targetPlayer.PrintToChat($" {prefix} Trail removed");
                Server.PrintToConsole($"[zModelsCustom] Removed trail for player {steamId}");
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in trail webdelete: {ex.Message}"));
        }
    }

    private async Task ProcessTracerWebDelete(ulong steamId)
    {
        try
        {
            var targetPlayer = Utilities.GetPlayers()
                .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

            await Database.RemovePlayerTracerAsync(steamId);
            EffectsManager.RemovePlayerTracer(steamId);

            Server.NextFrame(() =>
            {
                var prefix = Localizer["zModelsCustom.prefix"];
                if (targetPlayer?.IsValid == true)
                    targetPlayer.PrintToChat($" {prefix} Tracer removed");
                Server.PrintToConsole($"[zModelsCustom] Removed tracer for player {steamId}");
            });
        }
        catch (Exception ex)
        {
            Server.NextFrame(() =>
                Server.PrintToConsole($"[zModelsCustom] Error in tracer webdelete: {ex.Message}"));
        }
    }

    // css_webreload <steamid> - Reload specific player's weapons from DB (Console/Web only)
    private void Command_WebReload(CCSPlayerController? player, CommandInfo info)
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

        if (info.ArgCount < 2)
        {
            Server.PrintToConsole("[zModelsCustom] Usage: css_webreload <steamid>");
            Server.PrintToConsole("[zModelsCustom] Reloads config and refreshes specific player's weapons from DB");
            return;
        }

        var steamIdStr = info.GetArg(1);
        if (!ulong.TryParse(steamIdStr, out var steamId))
        {
            Server.PrintToConsole($"[zModelsCustom] Invalid SteamID: {steamIdStr}");
            return;
        }

        var targetPlayer = Utilities.GetPlayers()
            .FirstOrDefault(p => p?.IsValid == true && p.SteamID == steamId);

        if (targetPlayer == null)
        {
            Server.PrintToConsole($"[zModelsCustom] Player with SteamID {steamId} not found or not connected");
            return;
        }

        try
        {
            // Reload configs first
            var newPlayerModels = PlayerModelsConfig.Load(ModuleDirectory);
            var newWeaponModels = WeaponModelsConfig.Load(ModuleDirectory);

            ModelManager.PrecacheModels(newPlayerModels);
            WeaponManager.PrecacheModels(newWeaponModels);
            WeaponManager.UpdateModelsConfig(newWeaponModels);

            // Clear player's cache and reload from DB
            Database.ClearPlayerCache(steamId);
            WeaponManager.ClearPlayerData(steamId);

            // Reload player data from DB
            _ = Database.PreloadAllPlayerDataAsync(steamId);

            // Refresh player's weapons
            WeaponManager.RefreshPlayerWeapons(targetPlayer);

            var prefix = Localizer["zModelsCustom.prefix"];
            targetPlayer.PrintToChat($" {prefix} {Localizer["zModelsCustom.web_reload_success"]}");

            Server.PrintToConsole($"[zModelsCustom] Reloaded and refreshed player {targetPlayer.PlayerName} (SteamID: {steamId})");
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[zModelsCustom] Error in webreload for {steamId}: {ex.Message}");
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
        SmokeManager.ClearPlayerData(steamId);
        EffectsManager.ClearPlayerData(steamId);
        EffectsManager.ClearPlayerSlot(player.Slot);
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
    [JsonPropertyName("sc")]
    public bool Success { get; set; }

    [JsonPropertyName("i")]
    public WebLoginInfo? Info { get; set; }
}

public class WebLoginInfo
{
    [JsonPropertyName("ct")]
    public string Country { get; set; } = "";

    [JsonPropertyName("cty")]
    public string City { get; set; } = "";

    [JsonPropertyName("t")]
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
