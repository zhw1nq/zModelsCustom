using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System.Collections.Concurrent;

namespace zModelsCustom;

public class WeaponManager
{
    private WeaponModelsConfig _modelsConfig = new();
    private readonly ConcurrentDictionary<ulong, Dictionary<string, string>> _playerWeapons = new();
    private static readonly Dictionary<nint, string> OldSubclassByHandle = new();

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
        // No precaching needed for subclass-based weapon changes
        // Subclasses are already defined in game files
    }

    public void OnEntityCreated(CEntityInstance entity)
    {
        if (!entity.DesignerName.StartsWith("weapon_"))
            return;

        Server.NextWorldUpdate(() =>
        {
            CBasePlayerWeapon? weapon = entity.As<CBasePlayerWeapon>();
            if (weapon?.IsValid != true || weapon.OriginalOwnerXuidLow <= 0)
                return;

            CCSPlayerController? player = FindPlayerFromWeapon(weapon);
            if (player == null || !player.IsValid || player.IsBot)
                return;

            ApplyPlayerWeaponSubclass(player, weapon);
        });
    }

    public HookResult OnItemEquip(EventItemEquip @event, GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        CBasePlayerWeapon? activeWeapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (activeWeapon?.IsValid != true)
            return HookResult.Continue;

        ApplyPlayerWeaponSubclass(player, activeWeapon);

        return HookResult.Continue;
    }

    private void ApplyPlayerWeaponSubclass(CCSPlayerController player, CBasePlayerWeapon weapon)
    {
        var steamId = player.SteamID;
        var weaponDesignerName = GetDesignerName(weapon);

        // Check cache first
        if (_playerWeapons.TryGetValue(steamId, out var weapons) &&
            weapons.TryGetValue(weaponDesignerName, out var modelId))
        {
            var modelData = _modelsConfig.FindModelByUniqueId(modelId);
            if (modelData != null)
            {
                var subclass = modelData.GetSubclassName();
                if (!string.IsNullOrEmpty(subclass) && weaponDesignerName.Equals(modelData.WeaponType, StringComparison.Ordinal))
                {
                    SetSubclass(weapon, weaponDesignerName, subclass);
                }
            }
        }
        else
        {
            // Load from database async
            _ = LoadAndApplyWeaponSubclassAsync(steamId, player, weapon, weaponDesignerName);
        }
    }

    private async Task LoadAndApplyWeaponSubclassAsync(ulong steamId, CCSPlayerController player, CBasePlayerWeapon weapon, string weaponName)
    {
        var modelId = await zModelsCustom.Database.GetPlayerWeaponAsync(steamId, weaponName);

        if (modelId == null)
            return;

        // Cache the result
        var weapons = _playerWeapons.GetOrAdd(steamId, _ => new Dictionary<string, string>());
        weapons[weaponName] = modelId;

        var modelData = _modelsConfig.FindModelByUniqueId(modelId);
        if (modelData == null)
        {
            // Model no longer exists in config, remove from database
            await zModelsCustom.Database.RemovePlayerWeaponAsync(steamId, weaponName);
            return;
        }

        var subclass = modelData.GetSubclassName();
        if (string.IsNullOrEmpty(subclass))
            return;

        if (!weaponName.Equals(modelData.WeaponType, StringComparison.Ordinal))
            return;

        Server.NextFrame(() =>
        {
            if (player.IsValid && weapon?.IsValid == true)
            {
                SetSubclass(weapon, weaponName, subclass);
            }
        });
    }

    public void RefreshPlayerWeapons(CCSPlayerController player)
    {
        if (!player.IsValid || player.PlayerPawn.Value?.WeaponServices == null)
            return;

        var activeWeapon = player.PlayerPawn.Value.WeaponServices.ActiveWeapon.Value;
        if (activeWeapon?.IsValid == true)
        {
            ApplyPlayerWeaponSubclass(player, activeWeapon);
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

    #region Weapon Helpers

    public static string GetDesignerName(CBasePlayerWeapon weapon)
    {
        string weaponDesignerName = weapon.DesignerName;
        ushort weaponIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;

        return (weaponDesignerName, weaponIndex) switch
        {
            var (name, _) when name.Contains("bayonet") => "weapon_knife",
            ("weapon_deagle", 64) => "weapon_revolver",
            ("weapon_m4a1", 60) => "weapon_m4a1_silencer",
            ("weapon_hkp2000", 61) => "weapon_usp_silencer",
            ("weapon_mp7", 23) => "weapon_mp5sd",
            _ => weaponDesignerName
        };
    }

    public static void SetSubclass(CBasePlayerWeapon weapon, string oldSubclass, string newSubclass)
    {
        if (string.IsNullOrEmpty(newSubclass))
            return;

        var handle = weapon.Handle;
        OldSubclassByHandle[handle] = oldSubclass;
        weapon.AcceptInput("ChangeSubclass", weapon, weapon, newSubclass);
    }

    public static void ResetSubclass(CBasePlayerWeapon weapon)
    {
        var handle = weapon.Handle;
        if (!OldSubclassByHandle.TryGetValue(handle, out string? oldSubclass) || string.IsNullOrEmpty(oldSubclass))
            return;

        weapon.AcceptInput("ChangeSubclass", weapon, weapon, oldSubclass);
        OldSubclassByHandle.Remove(handle);
    }

    private static CCSPlayerController? FindPlayerFromWeapon(CBasePlayerWeapon weapon)
    {
        if (weapon.OwnerEntity.Value == null)
            return null;

        CCSPlayerPawn? pawn = weapon.OwnerEntity.Value.As<CCSPlayerPawn>();
        return pawn?.OriginalController.Value;
    }

    #endregion

    #region Equip/Unequip

    public bool HandleEquip(CCSPlayerController player, WeaponModelData item, bool isEquip)
    {
        if (!player.PawnIsAlive)
            return true;

        var subclass = item.GetSubclassName();
        if (string.IsNullOrEmpty(subclass) || string.IsNullOrEmpty(item.WeaponType))
            return true;

        CBasePlayerWeapon? weapon = GetPlayerWeapon(player, item.WeaponType);
        if (weapon != null)
        {
            if (isEquip)
            {
                SetSubclass(weapon, item.WeaponType, subclass);
            }
            else
            {
                ResetSubclass(weapon);
            }
        }

        return true;
    }

    private static CBasePlayerWeapon? GetPlayerWeapon(CCSPlayerController player, string weaponName)
    {
        CPlayer_WeaponServices? weaponServices = player.PlayerPawn?.Value?.WeaponServices;
        if (weaponServices == null)
            return null;

        CBasePlayerWeapon? activeWeapon = weaponServices.ActiveWeapon?.Value;
        if (activeWeapon != null && GetDesignerName(activeWeapon) == weaponName)
            return activeWeapon;

        return weaponServices.MyWeapons.FirstOrDefault(p => p.Value != null && GetDesignerName(p.Value) == weaponName)?.Value;
    }

    #endregion

    #region Inspect

    public void Inspect(CCSPlayerController player, WeaponModelData modelData)
    {
        if (player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value is not CBasePlayerWeapon activeWeapon)
            return;

        var subclass = modelData.GetSubclassName();
        if (string.IsNullOrEmpty(subclass) || string.IsNullOrEmpty(modelData.WeaponType))
            return;

        if (GetDesignerName(activeWeapon) != modelData.WeaponType)
        {
            player.PrintToChat($"You need to equip {modelData.WeaponType} first!");
            return;
        }

        SetSubclass(activeWeapon, modelData.WeaponType, subclass);

        // Reset after 3 seconds
        zModelsCustom.Instance.AddTimer(3.0f, () =>
        {
            if (player.IsValid && player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value == activeWeapon)
            {
                ResetSubclass(activeWeapon);
            }
        });
    }

    #endregion
}
