using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace ConsultReady;

public sealed class CaseStore
{
    private readonly string connectionString;
    private readonly AppSettings settings;
    public CaseStore(AppSettings settings)
    {
        this.settings = settings;
        Directory.CreateDirectory(settings.DataDirectory);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(settings.DataDirectory, "consultready.db"), DefaultTimeout = 10 }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS cases(id TEXT PRIMARY KEY, owner TEXT NOT NULL, revision INTEGER NOT NULL, updated TEXT NOT NULL, payload TEXT NOT NULL); CREATE INDEX IF NOT EXISTS cases_owner ON cases(owner); CREATE TABLE IF NOT EXISTS usage(day TEXT PRIMARY KEY, calls INTEGER NOT NULL);";
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }
    public void Add(string owner, CaseFile item)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO cases(id,owner,revision,updated,payload) SELECT $id,$owner,$rev,$updated,$json WHERE (SELECT COUNT(*) FROM cases WHERE owner=$owner)<20 AND (SELECT COUNT(*) FROM cases)<2000";
        Bind(cmd, owner, item);
        if (cmd.ExecuteNonQuery() != 1)
            throw new AppError(429, "Case storage limit reached. Delete an earlier case before creating another.");
    }
    public CaseFile Get(string owner, string id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM cases WHERE id=$id AND owner=$owner";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$owner", owner);
        var json = cmd.ExecuteScalar() as string ?? throw new AppError(404, "Case not found or expired.");
        return JsonSerializer.Deserialize<CaseFile>(json, JsonDefaults.Options)!;
    }
    public object List(string owner)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM cases WHERE owner=$owner ORDER BY updated DESC";
        cmd.Parameters.AddWithValue("$owner", owner);
        using var reader = cmd.ExecuteReader();
        var result = new List<object>();
        while (reader.Read())
        {
            var c = JsonSerializer.Deserialize<CaseFile>(reader.GetString(0), JsonDefaults.Options)!;
            result.Add(new
            {
                c.Id,
                c.Title,
                c.Sample,
                c.UpdatedAt
            });
        }
        return result;
    }
    public void Save(string owner, CaseFile item, int expectedRevision)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE cases SET revision=$rev,updated=$updated,payload=$json WHERE id=$id AND owner=$owner AND revision=$expected";
        Bind(cmd, owner, item);
        cmd.Parameters.AddWithValue("$expected", expectedRevision);
        if (cmd.ExecuteNonQuery() != 1)
            throw new AppError(409, "This case changed during the operation. Reload it before retrying.");
    }
    private static void Bind(SqliteCommand cmd, string owner, CaseFile item)
    {
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$owner", owner);
        cmd.Parameters.AddWithValue("$rev", item.Revision);
        cmd.Parameters.AddWithValue("$updated", item.UpdatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(item, JsonDefaults.Options));
    }
    public void Delete(string owner, string id)
    {
        var item = Get(owner, id);
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM cases WHERE id=$id AND owner=$owner";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$owner", owner);
        cmd.ExecuteNonQuery();
        DeleteFiles(item);
    }
    private void DeleteFiles(CaseFile item)
    {
        foreach (var source in item.Sources.Where(s => s.OriginalFile is not null))
        {
            var file = OriginalPath(item.Id, source.Id);
            if (File.Exists(file))
                File.Delete(file);
        }
    }
    public string OriginalPath(string caseId, string sourceId)
    {
        if (!Guid.TryParseExact(caseId, "N", out _) || !Guid.TryParseExact(sourceId, "N", out _))
            throw new AppError(400, "Invalid document identifier.");
        var directory = Path.Combine(settings.DataDirectory, "originals");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"{caseId}-{sourceId}.pdf");
    }
    public void Cleanup()
    {
        if (!settings.Hosted)
            return;
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM cases WHERE updated < $before";
        cmd.Parameters.AddWithValue("$before", DateTimeOffset.UtcNow.AddHours(-24).ToString("O"));
        cmd.ExecuteNonQuery();
    }
    public void ReserveModelCall()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO usage(day,calls) VALUES($day,1) ON CONFLICT(day) DO UPDATE SET calls=calls+1 WHERE calls<$limit";
        cmd.Parameters.AddWithValue("$day", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$limit", settings.DailyCalls);
        if (cmd.ExecuteNonQuery() != 1)
            throw new AppError(429, "The server's daily model-call budget is exhausted. Try the labeled sample walkthrough or return later.");
    }
}
public sealed class CleanupService(CaseStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        store.Cleanup();
        while (await timer.WaitForNextTickAsync(token))
            store.Cleanup();
    }
}
