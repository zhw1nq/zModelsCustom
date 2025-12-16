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
    private bool _disposed;

    // Common weapon types for table creation
    private static readonly string[] WeaponColumns = new[]
    {
        "weapon_ak47", "weapon_m4a1", "weapon_m4a1_silencer", "weapon_awp", "weapon_ssg08",
        "weapon_famas", "weapon_aug", "weapon_galilar", "weapon_sg556", "weapon_scar20", "weapon_g3sg1",
        "weapon_mp9", "weapon_mp7", "weapon_mp5sd", "weapon_ump45", "weapon_p90", "weapon_bizon", "weapon_mac10",
        "weapon_usp_silencer", "weapon_hkp2000", "weapon_glock", "weapon_elite", "weapon_p250",
        "weapon_fiveseven", "weapon_cz75a", "weapon_tec9", "weapon_revolver", "weapon_deagle",
        "weapon_nova", "weapon_xm1014", "weapon_mag7", "weapon_sawedoff", "weapon_m249", "weapon_negev",
        "weapon_knife", "weapon_taser"
    };

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

        // Create weapon models table
        var columnDefs = string.Join(",\n                ", 
            WeaponColumns.Select(w => $"`{w}` VARCHAR(64) DEFAULT NULL"));

        await using var cmd2 = new MySqlCommand($@"
            CREATE TABLE IF NOT EXISTS zCustomWeapons (
                steamid BIGINT UNSIGNED PRIMARY KEY,
                {columnDefs},
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_steamid (steamid)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn);

        await cmd2.ExecuteNonQueryAsync();
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

    #region Weapon Models

    public async Task<string?> GetPlayerWeaponAsync(ulong steamId, string weaponName)
    {
        if (_weaponCache.TryGetValue((steamId, weaponName), out var cached))
            return cached;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var safeWeaponName = SanitizeColumnName(weaponName);
        if (safeWeaponName == null) return null;

        await using var cmd = new MySqlCommand(
            $"SELECT `{safeWeaponName}` FROM zCustomWeapons WHERE steamid = @steamid LIMIT 1",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        var result = await cmd.ExecuteScalarAsync();
        if (result is string modelId && !string.IsNullOrEmpty(modelId))
        {
            _weaponCache.TryAdd((steamId, weaponName), modelId);
            return modelId;
        }

        return null;
    }

    public async Task<Dictionary<string, string>> GetAllPlayerWeaponsAsync(ulong steamId)
    {
        var weapons = new Dictionary<string, string>();

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "SELECT * FROM zCustomWeapons WHERE steamid = @steamid LIMIT 1",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                if (columnName.StartsWith("weapon_") && !reader.IsDBNull(i))
                {
                    var value = reader.GetString(i);
                    if (!string.IsNullOrEmpty(value))
                    {
                        weapons[columnName] = value;
                        _weaponCache[(steamId, columnName)] = value;
                    }
                }
            }
        }

        return weapons;
    }

    public async Task SavePlayerWeaponAsync(ulong steamId, string weaponName, string modelId)
    {
        _weaponCache[(steamId, weaponName)] = modelId;

        var safeWeaponName = SanitizeColumnName(weaponName);
        if (safeWeaponName == null) return;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand($@"
            INSERT INTO zCustomWeapons (steamid, `{safeWeaponName}`) 
            VALUES (@steamid, @model_id)
            ON DUPLICATE KEY UPDATE `{safeWeaponName}` = @model_id", conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);
        cmd.Parameters.AddWithValue("@model_id", modelId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemovePlayerWeaponAsync(ulong steamId, string weaponName)
    {
        _weaponCache.TryRemove((steamId, weaponName), out _);

        var safeWeaponName = SanitizeColumnName(weaponName);
        if (safeWeaponName == null) return;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            $"UPDATE zCustomWeapons SET `{safeWeaponName}` = NULL WHERE steamid = @steamid",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RemoveAllPlayerWeaponsAsync(ulong steamId)
    {
        foreach (var weapon in WeaponColumns)
        {
            _weaponCache.TryRemove((steamId, weapon), out _);
        }

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        var setNulls = string.Join(", ", WeaponColumns.Select(w => $"`{w}` = NULL"));

        await using var cmd = new MySqlCommand(
            $"UPDATE zCustomWeapons SET {setNulls} WHERE steamid = @steamid",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task PreloadPlayerWeaponsAsync(ulong steamId)
    {
        await GetAllPlayerWeaponsAsync(steamId);
    }

    private async Task EnsurePlayerExistsAsync(ulong steamId)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();

        await using var cmd = new MySqlCommand(
            "INSERT IGNORE INTO zCustomWeapons (steamid) VALUES (@steamid)",
            conn);

        cmd.Parameters.AddWithValue("@steamid", steamId);

        await cmd.ExecuteNonQueryAsync();
    }

    private static string? SanitizeColumnName(string weaponName)
    {
        var normalized = weaponName.ToLowerInvariant();
        return WeaponColumns.Contains(normalized) ? normalized : null;
    }

    #endregion

    #region Common

    public void ClearPlayerCache(ulong steamId)
    {
        // Clear model cache
        _modelCache.TryRemove((steamId, CsTeam.Terrorist), out _);
        _modelCache.TryRemove((steamId, CsTeam.CounterTerrorist), out _);

        // Clear weapon cache
        foreach (var weapon in WeaponColumns)
        {
            _weaponCache.TryRemove((steamId, weapon), out _);
        }
    }

    public async Task PreloadAllPlayerDataAsync(ulong steamId)
    {
        await EnsurePlayerExistsAsync(steamId);
        await Task.WhenAll(
            PreloadPlayerModelsAsync(steamId),
            PreloadPlayerWeaponsAsync(steamId)
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
        MySqlConnection.ClearAllPools();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
