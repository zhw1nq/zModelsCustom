using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Collections.Concurrent;
using System.Drawing;

namespace zModelsCustom;

/// <summary>
/// Handles Trail and Tracer effects (CS2-Store compatible)
/// Optimized for performance and memory safety
/// </summary>
public class EffectsManager
{
    // Player data - cleaned on disconnect
    private readonly ConcurrentDictionary<ulong, string> _playerTrails = new();
    private readonly ConcurrentDictionary<ulong, string> _playerTracers = new();
    
    // Position tracking - cleaned on disconnect
    private readonly ConcurrentDictionary<int, Vector> _lastPosition = new();
    private readonly ConcurrentDictionary<int, Vector> _endPosition = new();
    
    // Config references
    private TrailsConfig? _trailsConfig;
    private TracersConfig? _tracersConfig;
    
    // Constants
    private const float MIN_MOVE_DISTANCE = 5.0f;
    private static readonly Random _random = new();

    #region Initialization

    public void Initialize(string moduleDirectory)
    {
        _trailsConfig = TrailsConfig.Load(moduleDirectory);
        _tracersConfig = TracersConfig.Load(moduleDirectory);
    }

    public void Reload(string moduleDirectory)
    {
        _trailsConfig = TrailsConfig.Load(moduleDirectory);
        _tracersConfig = TracersConfig.Load(moduleDirectory);
    }

    #endregion

    #region Trail Processing

    public void OnGameFrame()
    {
        if (_trailsConfig == null) return;

        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsValidPlayer(player)) continue;
            if (!_playerTrails.TryGetValue(player.SteamID, out var trailId)) continue;
            
            var trailData = _trailsConfig.FindByUniqueId(trailId);
            if (trailData != null)
                ProcessTrail(player, trailData);
        }
    }

    private void ProcessTrail(CCSPlayerController player, TrailData trail)
    {
        var origin = GetPlayerOrigin(player);
        if (origin == null) return;

        // Check movement distance
        if (_lastPosition.TryGetValue(player.Slot, out var lastPos))
        {
            if (Distance(lastPos, origin) <= MIN_MOVE_DISTANCE)
                return;
        }

        _lastPosition[player.Slot] = origin;

        // Create appropriate trail type
        if (trail.IsParticle)
            SpawnParticleTrail(player, origin, trail);
        else
            SpawnBeamTrail(player, origin, trail);
    }

    private void SpawnParticleTrail(CCSPlayerController player, Vector origin, TrailData trail)
    {
        var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
        if (particle == null) return;

        particle.EffectName = trail.Model;
        particle.DispatchSpawn();
        particle.Teleport(origin, ParseAngle(trail.AngleValue), new Vector());
        particle.AcceptInput(trail.AcceptInputValue);

        var pawn = player.PlayerPawn?.Value;
        if (pawn != null)
            particle.AcceptInput("FollowEntity", pawn, pawn, "!activator");

        ScheduleRemove(particle, trail.Lifetime);
    }

    private void SpawnBeamTrail(CCSPlayerController player, Vector origin, TrailData trail)
    {
        if (!_endPosition.TryGetValue(player.Slot, out var endPos))
        {
            _endPosition[player.Slot] = origin;
            return;
        }

        var beam = Utilities.CreateEntityByName<CBeam>("env_beam");
        if (beam == null) return;

        beam.RenderMode = RenderMode_t.kRenderTransColor;
        beam.Width = trail.WidthValue;
        beam.Render = ParseColor(trail.Color);
        beam.Teleport(origin, new QAngle(), new Vector());
        beam.EndPos.X = endPos.X;
        beam.EndPos.Y = endPos.Y;
        beam.EndPos.Z = endPos.Z;

        _endPosition[player.Slot] = origin;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");

        ScheduleRemove(beam, trail.Lifetime);
    }

    #endregion

    #region Tracer Processing

    public HookResult OnBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        if (_tracersConfig == null) return HookResult.Continue;

        var player = @event.Userid;
        if (player == null || !player.IsValid) return HookResult.Continue;
        if (!_playerTracers.TryGetValue(player.SteamID, out var tracerId)) return HookResult.Continue;

        var tracerData = _tracersConfig.FindByUniqueId(tracerId);
        if (tracerData != null)
            SpawnTracer(player, @event.X, @event.Y, @event.Z, tracerData);

        return HookResult.Continue;
    }

    private void SpawnTracer(CCSPlayerController player, float x, float y, float z, TracerData tracer)
    {
        var particle = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
        if (particle == null) return;

        particle.EffectName = tracer.Model;
        particle.DispatchSpawn();
        particle.Teleport(GetEyePosition(player), new QAngle(), new Vector());
        particle.AcceptInput(tracer.AcceptInputValue);

        ScheduleRemove(particle, tracer.Lifetime);
    }

    #endregion

    #region Player Data Management

    public void SetPlayerTrail(ulong steamId, string uniqueId) => _playerTrails[steamId] = uniqueId;
    public void RemovePlayerTrail(ulong steamId) => _playerTrails.TryRemove(steamId, out _);
    public void SetPlayerTracer(ulong steamId, string uniqueId) => _playerTracers[steamId] = uniqueId;
    public void RemovePlayerTracer(ulong steamId) => _playerTracers.TryRemove(steamId, out _);

    public void ClearPlayerData(ulong steamId)
    {
        _playerTrails.TryRemove(steamId, out _);
        _playerTracers.TryRemove(steamId, out _);
    }

    public void ClearPlayerSlot(int slot)
    {
        _lastPosition.TryRemove(slot, out _);
        _endPosition.TryRemove(slot, out _);
    }

    public async Task LoadPlayerDataAsync(ulong steamId)
    {
        var trail = await zModelsCustom.Database.GetPlayerTrailAsync(steamId);
        if (!string.IsNullOrEmpty(trail))
            _playerTrails[steamId] = trail;

        var tracer = await zModelsCustom.Database.GetPlayerTracerAsync(steamId);
        if (!string.IsNullOrEmpty(tracer))
            _playerTracers[steamId] = tracer;
    }

    #endregion

    #region Helpers

    private static bool IsValidPlayer(CCSPlayerController? player) =>
        player?.IsBot == false && player.IsValid && player.PawnIsAlive;

    private static Vector? GetPlayerOrigin(CCSPlayerController player) =>
        player.PlayerPawn.Value?.AbsOrigin is { } pos 
            ? new Vector(pos.X, pos.Y, pos.Z) 
            : null;

    private static Vector GetEyePosition(CCSPlayerController player)
    {
        var origin = player.PlayerPawn.Value?.AbsOrigin;
        return origin != null 
            ? new Vector(origin.X, origin.Y, origin.Z + 64.0f) 
            : new Vector();
    }

    private static float Distance(Vector a, Vector b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static Color ParseColor(string colorStr)
    {
        if (string.IsNullOrEmpty(colorStr))
            return Color.FromArgb(_random.Next(256), _random.Next(256), _random.Next(256));

        var parts = colorStr.Split(' ');
        if (parts.Length >= 3 &&
            int.TryParse(parts[0], out var r) &&
            int.TryParse(parts[1], out var g) &&
            int.TryParse(parts[2], out var b))
        {
            return Color.FromArgb(r, g, b);
        }
        return Color.FromArgb(_random.Next(256), _random.Next(256), _random.Next(256));
    }

    private static QAngle ParseAngle(string angleStr)
    {
        if (string.IsNullOrEmpty(angleStr))
            return new QAngle(90, 0, 0);

        var parts = angleStr.Split(' ');
        if (parts.Length >= 3 &&
            float.TryParse(parts[0], out var x) &&
            float.TryParse(parts[1], out var y) &&
            float.TryParse(parts[2], out var z))
        {
            return new QAngle(x, y, z);
        }
        return new QAngle(90, 0, 0);
    }

    private static void ScheduleRemove(CEntityInstance entity, float delay)
    {
        zModelsCustom.Instance.AddTimer(delay, () =>
        {
            if (entity?.IsValid == true)
                entity.Remove();
        });
    }

    public TrailsConfig? GetTrailsConfig() => _trailsConfig;
    public TracersConfig? GetTracersConfig() => _tracersConfig;

    #endregion
}
