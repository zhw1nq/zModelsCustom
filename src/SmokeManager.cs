using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using System.Collections.Concurrent;
using System.Globalization;

namespace zModelsCustom;

public class SmokeManager
{
    private readonly ConcurrentDictionary<ulong, string> _playerSmokeColors = new();
    private readonly Random _random = new();

    public void OnEntityCreated(CEntityInstance entity)
    {
        if (entity.DesignerName != "smokegrenade_projectile")
            return;

        var grenade = new CSmokeGrenadeProjectile(entity.Handle);
        if (grenade.Handle == IntPtr.Zero)
            return;

        Server.NextFrame(() =>
        {
            var player = grenade.Thrower.Value?.Controller.Value;
            if (player == null)
                return;

            var steamId = player.SteamID;
            if (!_playerSmokeColors.TryGetValue(steamId, out var color))
                return;

            ApplySmokeColor(grenade, color);
        });
    }

    private void ApplySmokeColor(CSmokeGrenadeProjectile grenade, string color)
    {
        if (color == "random")
        {
            // Random color each time
            grenade.SmokeColor.X = _random.NextSingle() * 255.0f;
            grenade.SmokeColor.Y = _random.NextSingle() * 255.0f;
            grenade.SmokeColor.Z = _random.NextSingle() * 255.0f;
        }
        else
        {
            // Parse "R G B" format
            var parts = color.Split(' ');
            if (parts.Length >= 3)
            {
                if (float.TryParse(parts[0], CultureInfo.InvariantCulture, out var r) &&
                    float.TryParse(parts[1], CultureInfo.InvariantCulture, out var g) &&
                    float.TryParse(parts[2], CultureInfo.InvariantCulture, out var b))
                {
                    grenade.SmokeColor.X = r;
                    grenade.SmokeColor.Y = g;
                    grenade.SmokeColor.Z = b;
                }
            }
        }
    }

    public void SetPlayerSmokeColor(ulong steamId, string color)
    {
        _playerSmokeColors[steamId] = color;
    }

    public void RemovePlayerSmokeColor(ulong steamId)
    {
        _playerSmokeColors.TryRemove(steamId, out _);
    }

    public void ClearPlayerData(ulong steamId)
    {
        _playerSmokeColors.TryRemove(steamId, out _);
    }

    public string? GetPlayerSmokeColor(ulong steamId)
    {
        return _playerSmokeColors.TryGetValue(steamId, out var color) ? color : null;
    }

    /// <summary>
    /// Load player's smoke color from database into memory cache
    /// </summary>
    public async Task LoadPlayerSmokeColorAsync(ulong steamId)
    {
        var color = await zModelsCustom.Database.GetPlayerSmokeColorAsync(steamId);
        if (!string.IsNullOrEmpty(color))
        {
            _playerSmokeColors[steamId] = color;
        }
    }
}
