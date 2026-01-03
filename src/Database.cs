using MySqlConnector;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using System.Collections.Concurrent;

namespace zModelsCustom;

public class Database : IDisposable
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<(ulong, CsTeam), string> _modelCache = new();
    private readonly ConcurrentDictionary<(ulong, string), string> _weaponCache = new();
    private readonly ConcurrentDictionary<ulong, string> _smokeCache = new();
    private readonly ConcurrentDictionary<ulong, string> _trailCache = new();
    private readonly ConcurrentDictionary<ulong, string> _tracerCache = new();
    private bool _disposed;

    public Database(DatabaseConfig config)
    {
        _connectionString = $"Server={config.Host};Port={config.Port};Database={config.Database};" +
                          $"Uid={config.User};Pwd={config.Password};" +
                          $"Pooling=true;MinimumPoolSize=2;MaximumPoolSize=20;" +
                          $"ConnectionTimeout=30;DefaultCommandTimeout=30;";

        InitializeDatabaseAsync().GetAwaiter().GetResult();
    }

    private async Task InitializeDatabaseAsync()
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        // Create player models table
        await using var cmd1 = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS zPlayerModels (
                steamid BIGINT UNSIGNED,
                team VARCHAR(2) NOT NULL,
                model_id VARCHAR(64) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (steamid, team),
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn);

        await cmd1.ExecuteNonQueryAsync();

        // Create normalized weapon equipments table (new structure)
        await using var cmd2 = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS zWeaponEquipments (
                id INT AUTO_INCREMENT PRIMARY KEY,
                steamid BIGINT UNSIGNED NOT NULL,
                weapon_type VARCHAR(32) NOT NULL,
                uniqueid VARCHAR(64) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE KEY uk_player_weapon (steamid, weapon_type),
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn);

        await cmd2.ExecuteNonQueryAsync();

        // Create smoke colors table
        await using var cmd3 = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS zSmokeColors (
                steamid BIGINT UNSIGNED PRIMARY KEY,
                color VARCHAR(32) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn);

        await cmd3.ExecuteNonQueryAsync();

        // Create trails table
        await using var cmd4 = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS zTrails (
                steamid BIGINT UNSIGNED PRIMARY KEY,
                uniqueid VARCHAR(64) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn);

        await cmd4.ExecuteNonQueryAsync();

        // Create tracers table
        await using var cmd5 = new MySqlCommand(@"
            CREATE TABLE IF NOT EXISTS zTracers (
                steamid BIGINT UNSIGNED PRIMARY KEY,
                uniqueid VARCHAR(64) NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn);

        await cmd5.ExecuteNonQueryAsync();
    }

    #region Player Models

    public async Task<string?> GetPlayerModelAsync(ulong steamId, CsTeam team)
    {
        if (_modelCache.TryGetValue((steamId, team), out var cached))
            return cached;

        var teamStr = GetTeamString(team);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT model_id FROM zPlayerModels WHERE steamid = @steamid AND team = @team LIMIT 1",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", teamStr);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string modelId)
        {
            _modelCache.TryAdd((steamId, team), modelId);
            return modelId;
        }

        return null;
    }

    public async Task SavePlayerModelAsync(ulong steamId, CsTeam team, string modelId)
    {
        _modelCache[(steamId, team)] = modelId;

        var teamStr = GetTeamString(team);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zPlayerModels (steamid, team, model_id) 
            VALUES (@steamid, @team, @model_id)
            ON DUPLICATE KEY UPDATE model_id = @model_id", conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", teamStr);
        cmd.Parameters.AddWithValue("@model_id", modelId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerModelAsync(ulong steamId, CsTeam team)
    {
        _modelCache.TryRemove((steamId, team), out _);

        var teamStr = GetTeamString(team);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zPlayerModels WHERE steamid = @steamid AND team = @team",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@team", teamStr);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PreloadPlayerModelsAsync(ulong steamId)
    {
        await Task.WhenAll(
            GetPlayerModelAsync(steamId, CsTeam.Terrorist),
            GetPlayerModelAsync(steamId, CsTeam.CounterTerrorist)
        );
    }

    private static string GetTeamString(CsTeam team) =>
        team == CsTeam.CounterTerrorist ? "CT" : "T";

    #endregion

    #region Weapon Equipments (Normalized)

    public async Task<string?> GetPlayerWeaponAsync(ulong steamId, string weaponType)
    {
        if (_weaponCache.TryGetValue((steamId, weaponType), out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT uniqueid FROM zWeaponEquipments WHERE steamid = @steamid AND weapon_type = @weapon_type LIMIT 1",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string uniqueId && !string.IsNullOrEmpty(uniqueId))
        {
            _weaponCache.TryAdd((steamId, weaponType), uniqueId);
            return uniqueId;
        }

        return null;
    }

    public async Task<Dictionary<string, string>> GetAllPlayerWeaponsAsync(ulong steamId)
    {
        var weapons = new Dictionary<string, string>();

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT weapon_type, uniqueid FROM zWeaponEquipments WHERE steamid = @steamid",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var weaponType = reader.GetString(0);
            var uniqueId = reader.GetString(1);
            weapons[weaponType] = uniqueId;
            _weaponCache[(steamId, weaponType)] = uniqueId;
        }

        return weapons;
    }

    public async Task SavePlayerWeaponAsync(ulong steamId, string weaponType, string uniqueId)
    {
        _weaponCache[(steamId, weaponType)] = uniqueId;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zWeaponEquipments (steamid, weapon_type, uniqueid) 
            VALUES (@steamid, @weapon_type, @uniqueid)
            ON DUPLICATE KEY UPDATE uniqueid = @uniqueid", conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);
        cmd.Parameters.AddWithValue("@uniqueid", uniqueId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerWeaponAsync(ulong steamId, string weaponType)
    {
        _weaponCache.TryRemove((steamId, weaponType), out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zWeaponEquipments WHERE steamid = @steamid AND weapon_type = @weapon_type",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@weapon_type", weaponType);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveAllPlayerWeaponsAsync(ulong steamId)
    {
        // Clear all weapon cache for this player
        var keysToRemove = _weaponCache.Keys.Where(k => k.Item1 == steamId).ToList();
        foreach (var key in keysToRemove)
        {
            _weaponCache.TryRemove(key, out _);
        }

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zWeaponEquipments WHERE steamid = @steamid",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PreloadPlayerWeaponsAsync(ulong steamId)
    {
        await GetAllPlayerWeaponsAsync(steamId);
    }

    #endregion

    #region Smoke Colors

    public async Task<string?> GetPlayerSmokeColorAsync(ulong steamId)
    {
        if (_smokeCache.TryGetValue(steamId, out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT color FROM zSmokeColors WHERE steamid = @steamid LIMIT 1",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string color && !string.IsNullOrEmpty(color))
        {
            _smokeCache.TryAdd(steamId, color);
            return color;
        }

        return null;
    }

    public async Task SavePlayerSmokeColorAsync(ulong steamId, string color)
    {
        _smokeCache[steamId] = color;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zSmokeColors (steamid, color) 
            VALUES (@steamid, @color)
            ON DUPLICATE KEY UPDATE color = @color", conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@color", color);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerSmokeColorAsync(ulong steamId)
    {
        _smokeCache.TryRemove(steamId, out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zSmokeColors WHERE steamid = @steamid",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PreloadPlayerSmokeColorAsync(ulong steamId)
    {
        await GetPlayerSmokeColorAsync(steamId);
    }

    #endregion

    #region Trails

    public async Task<string?> GetPlayerTrailAsync(ulong steamId)
    {
        if (_trailCache.TryGetValue(steamId, out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT uniqueid FROM zTrails WHERE steamid = @steamid LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string uniqueId && !string.IsNullOrEmpty(uniqueId))
        {
            _trailCache.TryAdd(steamId, uniqueId);
            return uniqueId;
        }
        return null;
    }

    public async Task SavePlayerTrailAsync(ulong steamId, string uniqueId)
    {
        _trailCache[steamId] = uniqueId;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zTrails (steamid, uniqueid) VALUES (@steamid, @uniqueid)
            ON DUPLICATE KEY UPDATE uniqueid = @uniqueid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@uniqueid", uniqueId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerTrailAsync(ulong steamId)
    {
        _trailCache.TryRemove(steamId, out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zTrails WHERE steamid = @steamid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PreloadPlayerTrailAsync(ulong steamId) => await GetPlayerTrailAsync(steamId);

    #endregion

    #region Tracers

    public async Task<string?> GetPlayerTracerAsync(ulong steamId)
    {
        if (_tracerCache.TryGetValue(steamId, out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT uniqueid FROM zTracers WHERE steamid = @steamid LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string uniqueId && !string.IsNullOrEmpty(uniqueId))
        {
            _tracerCache.TryAdd(steamId, uniqueId);
            return uniqueId;
        }
        return null;
    }

    public async Task SavePlayerTracerAsync(ulong steamId, string uniqueId)
    {
        _tracerCache[steamId] = uniqueId;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(@"
            INSERT INTO zTracers (steamid, uniqueid) VALUES (@steamid, @uniqueid)
            ON DUPLICATE KEY UPDATE uniqueid = @uniqueid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@uniqueid", uniqueId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerTracerAsync(ulong steamId)
    {
        _tracerCache.TryRemove(steamId, out _);

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "DELETE FROM zTracers WHERE steamid = @steamid", conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PreloadPlayerTracerAsync(ulong steamId) => await GetPlayerTracerAsync(steamId);

    #endregion

    #region Common

    public void ClearPlayerCache(ulong steamId)
    {
        // Clear model cache
        _modelCache.TryRemove((steamId, CsTeam.Terrorist), out _);
        _modelCache.TryRemove((steamId, CsTeam.CounterTerrorist), out _);

        // Clear weapon cache - find all keys for this player
        var keysToRemove = _weaponCache.Keys.Where(k => k.Item1 == steamId).ToList();
        foreach (var key in keysToRemove)
        {
            _weaponCache.TryRemove(key, out _);
        }

        // Clear smoke cache
        _smokeCache.TryRemove(steamId, out _);
        
        // Clear trail/tracer cache
        _trailCache.TryRemove(steamId, out _);
        _tracerCache.TryRemove(steamId, out _);
    }

    public async Task PreloadAllPlayerDataAsync(ulong steamId)
    {
        // Don't create row here - row is only created when saving data
        // This prevents empty rows for players who never save any skin
        await Task.WhenAll(
            PreloadPlayerModelsAsync(steamId),
            PreloadPlayerWeaponsAsync(steamId),
            PreloadPlayerSmokeColorAsync(steamId),
            PreloadPlayerTrailAsync(steamId),
            PreloadPlayerTracerAsync(steamId)
        );
    }

    public HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsBot != false) return HookResult.Continue;

        _ = PreloadAllPlayerDataAsync(player.SteamID);

        return HookResult.Continue;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _modelCache.Clear();
        _weaponCache.Clear();
        _smokeCache.Clear();
        _trailCache.Clear();
        _tracerCache.Clear();
        MySqlConnection.ClearAllPools();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
