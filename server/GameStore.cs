using System.Text.Json;
using Microsoft.Data.Sqlite;
using SnakeClub.Core;

// SQLite is a transactional, single-node store for development and small pilots.
// Every request reloads its affected rows inside an immediate transaction so two
// server processes cannot award the same run twice or overwrite each other's data.
sealed class GameStore
{
    private readonly string connectionString;
    private readonly JsonSerializerOptions json;

    public GameStore(string file, string legacyJson, JsonSerializerOptions options)
    {
        string full = Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = full, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 30 }.ToString();
        json = options;
        using var connection = Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
PRAGMA journal_mode=WAL;
CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS players (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS identity_links (identity_id TEXT PRIMARY KEY, player_id TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS runs (id TEXT PRIMARY KEY, player_id TEXT NOT NULL, mode TEXT NOT NULL, day TEXT NOT NULL, rules TEXT NOT NULL, expires_at INTEGER NOT NULL, completed INTEGER NOT NULL, score INTEGER NOT NULL, finished_at INTEGER NOT NULL, payload TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS runs_player_expiry ON runs(player_id, expires_at);
CREATE INDEX IF NOT EXISTS runs_board ON runs(day, rules, mode, completed, player_id, score DESC, finished_at);
CREATE TABLE IF NOT EXISTS rewards (id TEXT PRIMARY KEY, player_id TEXT NOT NULL, payload TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS rewards_player ON rewards(player_id);
CREATE TABLE IF NOT EXISTS ads (id TEXT PRIMARY KEY, player_id TEXT NOT NULL, day TEXT NOT NULL, payload TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ads_player_day ON ads(player_id, day);";
            command.ExecuteNonQuery();
        }
        using var tx = connection.BeginTransaction(deferred: false);
        using var check = Command(connection, tx, "SELECT value FROM metadata WHERE key='schema'");
        if (check.ExecuteScalar() == null)
        {
            if (File.Exists(legacyJson))
            {
                var old = JsonSerializer.Deserialize<GameData>(File.ReadAllText(legacyJson), json) ?? new GameData();
                Save(connection, tx, old);
            }
            using var mark = Command(connection, tx, "INSERT INTO metadata(key,value) VALUES('schema','1')");
            mark.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        return connection;
    }

    public GameData LoadLogin(SqliteConnection db, SqliteTransaction tx, params string?[] identities)
    {
        var result = new GameData();
        foreach (string identity in identities.Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).Distinct())
        {
            using var find = Command(db, tx, "SELECT player_id FROM identity_links WHERE identity_id=$id", ("$id", identity));
            string? linked = find.ExecuteScalar() as string;
            if (linked != null) result.identityLinks[identity] = linked;
            LoadPlayer(db, tx, result, linked ?? identity);
        }
        return result;
    }

    public GameData LoadPlayerState(SqliteConnection db, SqliteTransaction tx, string playerId, string day, long now, string? receiptRunId = null)
    {
        var result = new GameData();
        LoadPlayer(db, tx, result, playerId);
        ReadRows(db, tx, "SELECT id,payload FROM runs WHERE player_id=$id AND expires_at >= $now", result.runs,
            ("$id", playerId), ("$now", now));
        // A signed, completed run remains readable as a receipt after its
        // submission window. Load just the requested row, not all history.
        if (!string.IsNullOrEmpty(receiptRunId))
            ReadRows(db, tx, "SELECT id,payload FROM runs WHERE id=$run AND player_id=$id AND completed=1 AND expires_at < $now", result.runs,
                ("$run", receiptRunId), ("$id", playerId), ("$now", now));
        ReadRows(db, tx, "SELECT id,payload FROM rewards WHERE player_id=$id", result.rewards, ("$id", playerId));
        ReadRows(db, tx, "SELECT id,payload FROM ads WHERE player_id=$id AND day=$day", result.ads, ("$id", playerId), ("$day", day));
        return result;
    }

    private void LoadPlayer(SqliteConnection db, SqliteTransaction tx, GameData data, string id)
    {
        using var command = Command(db, tx, "SELECT payload FROM players WHERE id=$id", ("$id", id));
        if (command.ExecuteScalar() is string payload)
        {
            var player = JsonSerializer.Deserialize<PlayerProgress>(payload, json);
            if (player != null) data.players[id] = player;
        }
    }

    private void ReadRows<T>(SqliteConnection db, SqliteTransaction tx, string sql, Dictionary<string, T> output, params (string, object)[] args)
    {
        using var command = Command(db, tx, sql, args);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = JsonSerializer.Deserialize<T>(reader.GetString(1), json);
            if (value != null) output[reader.GetString(0)] = value;
        }
    }

    public void Save(SqliteConnection db, SqliteTransaction tx, GameData data)
    {
        foreach (var row in data.players)
            Execute(db, tx, "INSERT INTO players(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
                ("$id", row.Key), ("$payload", JsonSerializer.Serialize(row.Value, json)));
        foreach (var row in data.identityLinks)
            Execute(db, tx, "INSERT INTO identity_links(identity_id,player_id) VALUES($id,$player) ON CONFLICT(identity_id) DO UPDATE SET player_id=excluded.player_id",
                ("$id", row.Key), ("$player", row.Value));
        foreach (var row in data.runs)
        {
            var run = row.Value;
            Execute(db, tx, @"INSERT INTO runs(id,player_id,mode,day,rules,expires_at,completed,score,finished_at,payload)
VALUES($id,$player,$mode,$day,$rules,$expires,$completed,$score,$finished,$payload)
ON CONFLICT(id) DO UPDATE SET completed=excluded.completed,score=excluded.score,finished_at=excluded.finished_at,payload=excluded.payload",
                ("$id", row.Key), ("$player", run.playerId), ("$mode", run.mode), ("$day", run.date), ("$rules", run.rules),
                ("$expires", run.expiresAt), ("$completed", run.completed ? 1 : 0), ("$score", run.score), ("$finished", run.finishedAt),
                ("$payload", JsonSerializer.Serialize(run, json)));
        }
        foreach (var row in data.rewards)
            Execute(db, tx, "INSERT INTO rewards(id,player_id,payload) VALUES($id,$player,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
                ("$id", row.Key), ("$player", row.Value.playerId), ("$payload", JsonSerializer.Serialize(row.Value, json)));
        foreach (var row in data.ads)
            Execute(db, tx, "INSERT INTO ads(id,player_id,day,payload) VALUES($id,$player,$day,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload",
                ("$id", row.Key), ("$player", row.Value.playerId), ("$day", row.Value.day), ("$payload", JsonSerializer.Serialize(row.Value, json)));
    }

    public IResult Leaderboard(SqliteConnection db, SqliteTransaction tx, string playerId, string day, string rules)
    {
        const string sql = @"
WITH player_runs AS (
 SELECT player_id, score, finished_at,
 ROW_NUMBER() OVER (PARTITION BY player_id ORDER BY score DESC, finished_at ASC) AS attempt
 FROM runs WHERE day=$day AND rules=$rules AND mode='daily' AND completed=1
), ranked AS (
 SELECT player_id, score,
 ROW_NUMBER() OVER (ORDER BY score DESC, finished_at ASC, player_id ASC) AS rank,
 COUNT(*) OVER () AS total,
 LAG(score) OVER (ORDER BY score DESC, finished_at ASC, player_id ASC) AS previous_score
 FROM player_runs WHERE attempt=1
)
SELECT ranked.player_id,ranked.score,ranked.rank,ranked.total,ranked.previous_score,players.payload
FROM ranked LEFT JOIN players ON players.id=ranked.player_id
WHERE ranked.rank<=100 OR ranked.player_id=$player ORDER BY ranked.rank";
        using var command = Command(db, tx, sql, ("$day", day), ("$rules", rules), ("$player", playerId));
        using var reader = command.ExecuteReader();
        var rows = new List<object>();
        int rank = 0, total = 0, gap = 0;
        while (reader.Read())
        {
            string id = reader.GetString(0); int score = reader.GetInt32(1), position = reader.GetInt32(2);
            total = reader.GetInt32(3);
            string name = "玩家";
            if (!reader.IsDBNull(5)) name = JsonSerializer.Deserialize<PlayerProgress>(reader.GetString(5), json)?.displayName ?? name;
            if (position <= 100) rows.Add(new { rank = position, playerId = id, name, score });
            if (id == playerId) { rank = position; gap = reader.IsDBNull(4) ? 0 : reader.GetInt32(4) - score; }
        }
        return Results.Json(new { ok = true, date = day, rules, rows, rank, total, gap });
    }

    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction tx, string sql, params (string, object)[] args)
    {
        var command = db.CreateCommand(); command.Transaction = tx; command.CommandText = sql;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value);
        return command;
    }

    private static void Execute(SqliteConnection db, SqliteTransaction tx, string sql, params (string, object)[] args)
    {
        using var command = Command(db, tx, sql, args);
        command.ExecuteNonQuery();
    }
}
