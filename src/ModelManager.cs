using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;
using System.Collections.Concurrent;

namespace zModelsCustom;

public class ModelManager
{
    private static readonly Color LegDisabledColor = Color.FromArgb(254, 255, 255, 255);
    private static readonly Color DefaultColor = Color.FromArgb(255, 255, 255, 255);
    
    private static readonly IReadOnlyDictionary<CsTeam, string> DefaultModels = new Dictionary<CsTeam, string>
    {
        { CsTeam.CounterTerrorist, "characters/models/ctm_sas/ctm_sas.vmdl" },
        { CsTeam.Terrorist, "characters/models/tm_phoenix/tm_phoenix.vmdl" }
    };

    private readonly ConcurrentDictionary<ulong, List<CBaseModelEntity>> _inspectEntities = new();

    public void ApplyModel(CCSPlayerController player, PlayerModelData model)
    {
        if (!IsValidPlayer(player)) return;

        var pawn = player.PlayerPawn.Value!;
        RemoveGloves(pawn);

        Server.NextFrame(() =>
        {
            if (!IsValidPlayer(player)) return;

            pawn.SetModel(model.Model);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_CRenderComponent");
            pawn.Render = model.DisableLeg ? LegDisabledColor : DefaultColor;
        });
    }

    public void ResetModel(CCSPlayerController player)
    {
        if (!IsValidPlayer(player)) return;
        if (!DefaultModels.TryGetValue(player.Team, out var defaultModel)) return;

        var pawn = player.PlayerPawn.Value!;
        
        Server.NextFrame(() =>
        {
            if (!IsValidPlayer(player)) return;

            pawn.SetModel(defaultModel);
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_CRenderComponent");
            pawn.Render = DefaultColor;
        });
    }

    private static void RemoveGloves(CCSPlayerPawn pawn)
    {
        if (pawn.EconGloves == null) return;

        pawn.EconGloves.Initialized = false;
        pawn.EconGloves.ItemDefinitionIndex = 0;
    }

    public void InspectModel(CCSPlayerController player, PlayerModelData model)
    {
        if (!player.IsValid || player.PlayerPawn.Value is not CCSPlayerPawn pawn) return;

        CleanupInspectEntities(player.SteamID);

        var entity = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
        if (entity?.IsValid != true) return;

        var origin = GetFrontPosition(pawn.AbsOrigin!, pawn.EyeAngles);
        var angles = new QAngle(0, pawn.EyeAngles.Y + 180, 0);

        entity.Spawnflags = 256u;
        entity.Collision.SolidType = SolidType_t.SOLID_VPHYSICS;
        entity.Teleport(origin, angles, pawn.AbsVelocity);
        entity.DispatchSpawn();

        var entities = _inspectEntities.GetOrAdd(player.SteamID, _ => new List<CBaseModelEntity>());
        lock (entities)
        {
            entities.Add(entity);
        }

        Server.NextFrame(() =>
        {
            if (entity.IsValid)
                entity.SetModel(model.Model);
        });

        zModelsCustom.Instance.AddTimer(1.0f, () => RotateEntity(player.SteamID, entity, 0.0f));
    }

    private void RotateEntity(ulong steamId, CBaseModelEntity entity, float elapsed)
    {
        if (!entity.IsValid)
        {
            CleanupInspectEntity(steamId, entity);
            return;
        }

        const float totalTime = 5.0f;
        const float interval = 0.04f;
        const float rotationSpeed = 9.0f;

        var currentRotation = entity.AbsRotation!;
        entity.Teleport(null, new QAngle(currentRotation.X, currentRotation.Y + rotationSpeed, currentRotation.Z), null);

        if (elapsed < totalTime)
        {
            zModelsCustom.Instance.AddTimer(interval, () => RotateEntity(steamId, entity, elapsed + interval));
        }
        else
        {
            entity.Remove();
            CleanupInspectEntity(steamId, entity);
        }
    }

    private void CleanupInspectEntity(ulong steamId, CBaseModelEntity entity)
    {
        if (!_inspectEntities.TryGetValue(steamId, out var entities)) return;

        lock (entities)
        {
            entities.Remove(entity);
        }
    }

    public void CleanupInspectEntities(ulong steamId)
    {
        if (!_inspectEntities.TryGetValue(steamId, out var entities)) return;

        lock (entities)
        {
            foreach (var entity in entities.Where(e => e.IsValid))
            {
                entity.Remove();
            }
            entities.Clear();
        }

        _inspectEntities.TryRemove(steamId, out _);
    }

    private static Vector GetFrontPosition(Vector position, QAngle angles, float distance = 100.0f)
    {
        var radYaw = angles.Y * (MathF.PI / 180.0f);
        return position + new Vector(
            MathF.Cos(radYaw) * distance, 
            MathF.Sin(radYaw) * distance, 
            0
        );
    }

    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsValidPlayerForModel(player)) return HookResult.Continue;

        var steamId = player!.SteamID;
        var team = player.Team;

        _ = LoadAndApplyModelAsync(player, steamId, team);

        return HookResult.Continue;
    }

    private async Task LoadAndApplyModelAsync(CCSPlayerController player, ulong steamId, CsTeam team)
    {
        var modelId = await zModelsCustom.Database.GetPlayerModelAsync(steamId, team);
        
        if (modelId == null)
        {
            return;
        }

        var model = PlayerModelsConfig.Load(zModelsCustom.Instance.ModuleDirectory).FindModelByUniqueId(modelId);
        
        if (model == null)
        {
            await zModelsCustom.Database.RemovePlayerModelAsync(steamId, team);
            return;
        }

        Server.NextFrame(() => ProcessModelApplication(player, steamId, team, model));
    }

    private void ProcessModelApplication(CCSPlayerController player, ulong steamId, CsTeam team, PlayerModelData model)
    {
        if (!IsValidPlayer(player)) return;

        if (!IsModelSlotValid(model, player.Team))
        {
            _ = HandleInvalidSlot(player, steamId, team, model.Slot);
            return;
        }

        zModelsCustom.Instance.AddTimer(0.1f, () =>
        {
            if (IsValidPlayer(player))
            {
                ApplyModel(player, model);
            }
        });
    }

    private async Task HandleInvalidSlot(CCSPlayerController player, ulong steamId, CsTeam team, string slot)
    {
        await zModelsCustom.Database.RemovePlayerModelAsync(steamId, team);
        
        Server.NextFrame(() =>
        {
            if (IsValidPlayer(player))
                ResetModel(player);
        });
    }

    private static bool IsModelSlotValid(PlayerModelData model, CsTeam team)
    {
        var slot = model.Slot.ToUpperInvariant();
        return slot switch
        {
            "ALL" => true,
            "CT" => team == CsTeam.CounterTerrorist,
            "T" => team == CsTeam.Terrorist,
            _ => false
        };
    }

    private static bool IsValidPlayer(CCSPlayerController? player) =>
        player?.IsValid == true && player.PlayerPawn.Value != null;

    private static bool IsValidPlayerForModel(CCSPlayerController? player) =>
        player?.IsBot == false && player.Team >= CsTeam.Terrorist;

    public void PrecacheModels(PlayerModelsConfig models)
    {
        foreach (var model in models.Categories.Values.SelectMany(c => c.Values))
        {
            Server.PrecacheModel(model.Model);
            
            if (!string.IsNullOrEmpty(model.ArmModel))
                Server.PrecacheModel(model.ArmModel);
        }
    }
}
