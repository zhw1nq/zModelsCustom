using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System.Collections.Concurrent;

namespace zModelsCustom;

public class WeaponManager
{
    private WeaponModelsConfig _modelsConfig = new();
    private readonly ConcurrentDictionary<ulong, Dictionary<string, string>> _playerWeapons = new();

    public WeaponManager()
    {
        _modelsConfig = WeaponModelsConfig.Load(zModelsCustom.Instance.ModuleDirectory);
    }

    public void UpdateModelsConfig(WeaponModelsConfig config)
    {
        _modelsConfig = config;
    }

    public void PrecacheModels(WeaponModelsConfig models)
    {
        foreach (var weapon in models.Weapons.Values)
        {
            foreach (var model in weapon.Values)
            {
                if (!string.IsNullOrEmpty(model.Model))
                {
                    Server.PrecacheModel(model.Model);
                }
            }
        }
    }

    public void OnEntityCreated(CEntityInstance entity)
    {
        if (!entity.DesignerName.StartsWith("weapon_"))
        {
            return;
        }

        CBasePlayerWeapon weapon = entity.As<CBasePlayerWeapon>();
        SetWeaponModel(weapon, false);
    }

    public HookResult OnItemEquip(EventItemEquip @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;

        if (player == null || !player.IsValid)
            return HookResult.Continue;

        CBasePlayerWeapon? weapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;

        if (weapon != null)
        {
            SetWeaponModel(weapon, true);
        }

        return HookResult.Continue;
    }

    public void SetWeaponModel(CBasePlayerWeapon? weapon, bool isUpdate, bool reset = false)
    {
        Server.NextWorldUpdate(() =>
        {
            if (weapon == null || !weapon.IsValid || weapon.OwnerEntity.Value == null || weapon.OwnerEntity.Index <= 0)
                return;

            CCSPlayerPawn? pawn = weapon.OwnerEntity.Value?.As<CCSPlayerPawn>();

            if (pawn == null || !pawn.IsValid)
                return;

            CCSPlayerController? player = pawn.OriginalController.Value;
            if (player == null || !player.IsValid || player.IsBot)
                return;

            var steamId = player.SteamID;
            var weaponName = GetNormalizedWeaponName(weapon);

            // Try to get model from cache or database
            WeaponModelData? modelData = null;

            if (!reset)
            {
                // Check player weapon cache first
                if (_playerWeapons.TryGetValue(steamId, out var weapons) && 
                    weapons.TryGetValue(weaponName, out var modelId))
                {
                    modelData = _modelsConfig.FindModelByUniqueId(modelId);
                }
                else
                {
                    // Load from database async
                    _ = LoadAndApplyWeaponModelAsync(steamId, weapon, weaponName);
                    return;
                }
            }

            ApplyWeaponModelInternal(weapon, modelData, isUpdate, reset);
        });
    }

    private async Task LoadAndApplyWeaponModelAsync(ulong steamId, CBasePlayerWeapon weapon, string weaponName)
    {
        var modelId = await zModelsCustom.Database.GetPlayerWeaponAsync(steamId, weaponName);

        if (modelId == null)
        {
            return;
        }

        // Cache the result
        var weapons = _playerWeapons.GetOrAdd(steamId, _ => new Dictionary<string, string>());
        weapons[weaponName] = modelId;

        var modelData = _modelsConfig.FindModelByUniqueId(modelId);
        if (modelData == null)
        {
            return;
        }

        Server.NextFrame(() =>
        {
            if (weapon != null && weapon.IsValid)
            {
                ApplyWeaponModelInternal(weapon, modelData, true, false);
            }
        });
    }

    private static void ApplyWeaponModelInternal(CBasePlayerWeapon weapon, WeaponModelData? modelData, bool isUpdate, bool reset)
    {
        if (reset || modelData == null)
        {
            // Reset to original model
            if (!string.IsNullOrEmpty(weapon.Globalname))
            {
                string[] globalnameData = weapon.Globalname.Split(',');
                weapon.Globalname = string.Empty;
                
                if (globalnameData.Length >= 1 && !string.IsNullOrEmpty(globalnameData[0]))
                {
                    weapon.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName = globalnameData[0];
                    weapon.SetModel(globalnameData[0]);
                }
                
                if (globalnameData.Length >= 2)
                {
                    weapon.AttributeManager.Item.CustomName = globalnameData[1];
                }
            }
        }
        else
        {
            // Save original model info if not already saved
            if (!isUpdate && string.IsNullOrEmpty(weapon.Globalname))
            {
                weapon.Globalname = $"{weapon.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName},{weapon.AttributeManager.Item.CustomName}";
            }

            // Apply custom model
            weapon.CBodyComponent!.SceneNode!.GetSkeletonInstance().ModelState.ModelName = modelData.Model;
            weapon.SetModel(modelData.Model);

            if (!string.IsNullOrEmpty(modelData.CustomName))
            {
                weapon.AttributeManager.Item.CustomName = modelData.CustomName;
            }
        }
    }

    public void RefreshPlayerWeapons(CCSPlayerController player)
    {
        if (!player.IsValid || player.PlayerPawn.Value?.WeaponServices == null)
            return;

        var activeWeapon = player.PlayerPawn.Value.WeaponServices.ActiveWeapon.Value;
        if (activeWeapon != null)
        {
            SetWeaponModel(activeWeapon, true);
        }
    }

    public void ClearPlayerData(ulong steamId)
    {
        _playerWeapons.TryRemove(steamId, out _);
    }

    public void UpdatePlayerWeaponCache(ulong steamId, string weaponName, string? modelId)
    {
        var weapons = _playerWeapons.GetOrAdd(steamId, _ => new Dictionary<string, string>());
        
        if (modelId == null)
        {
            weapons.Remove(weaponName);
        }
        else
        {
            weapons[weaponName] = modelId;
        }
    }

    private static string GetNormalizedWeaponName(CBasePlayerWeapon weapon)
    {
        string weaponDesignerName = weapon.DesignerName;
        ushort weaponIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

        return (weaponDesignerName, weaponIndex) switch
        {
            var (name, _) when name.Contains("bayonet") => "weapon_knife",
            ("weapon_m4a1", 60) => "weapon_m4a1_silencer",
            ("weapon_hkp2000", 61) => "weapon_usp_silencer",
            ("weapon_mp7", 23) => "weapon_mp5sd",
            _ => weaponDesignerName
        };
    }
}
