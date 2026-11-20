using Npgsql;

namespace Amadban.Data;

public sealed record ActivityItem(long Id, string Title, string Summary, string Actor, string Role,
    DateTime At, string TargetKind, long? TargetId)
{
    public string TargetUrl => TargetKind switch
    {
        "invoice" when TargetId is long id => $"/Invoices/Detail/{id}",
        "stock" when TargetId is long id => $"/Stock?ProductId={id}",
        "dispatch" when TargetId is long id => $"/PrintDispatch?number={id}",
        "credit" => "/Credit", "petty" => "/Petty", "bank" => "/Bank",
        "staff" => "/Staff", "supplier" when TargetId is long id => $"/Suppliers?Id={id}",
        "supplier" => "/Suppliers", "organization" => "/Organization", _ => "/Index"
    };
    public string Icon => TargetKind switch
    {
        "invoice" => "file-text", "stock" => "bag", "dispatch" => "folder", "credit" => "money-bag",
        "petty" => "wallet", "bank" => "card", "staff" => "key", "supplier" => "boy",
        _ => "setting"
    };
}

public sealed partial class Store
{
    private static Task<int> Activity(NpgsqlConnection conn, NpgsqlTransaction? tx, long actor,
        string title, string summary, string kind, long? target = null) =>
        Exec(conn, tx, """
            INSERT INTO activity_feed(actor_id,title,summary,target_kind,target_id) VALUES($1,$2,$3,$4,$5)
            """, actor, title, summary, kind, target is long id ? id : DBNull.Value);

    public async Task<List<ActivityItem>> ActivityAsync(int page, int limit = 25)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT a.id,a.title,a.summary,s.display_name,s.role,a.occurred_at,a.target_kind,a.target_id
            FROM activity_feed a JOIN staff s ON s.id=a.actor_id
            ORDER BY a.id DESC LIMIT $1 OFFSET $2
            """, limit, (page - 1) * 25);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<ActivityItem>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),
            reader.GetString(3),reader.GetString(4),reader.GetDateTime(5),reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7)));
        return result;
    }

    public async Task<ActivityItem?> ActivityDetailAsync(long id)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var cmd=Command(conn,null,"""
            SELECT a.id,a.title,a.summary,s.display_name,s.role,a.occurred_at,a.target_kind,a.target_id
            FROM activity_feed a JOIN staff s ON s.id=a.actor_id WHERE a.id=$1
            """,id);
        await using var reader=await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync()?new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),
            reader.GetString(3),reader.GetString(4),reader.GetDateTime(5),reader.GetString(6),
            reader.IsDBNull(7)?null:reader.GetInt64(7)):null;
    }

    public async Task<long> UnreadActivityAsync(long chief)
    {
        await using var conn = await source.OpenConnectionAsync();
        return (long)(await Scalar(conn, null, """
            SELECT count(*) FROM activity_feed
            WHERE id > coalesce((SELECT last_seen_id FROM activity_reads WHERE staff_id=$1),0)
            """, chief))!;
    }

    public async Task MarkActivityReadAsync(long chief)
    {
        await using var conn = await source.OpenConnectionAsync();
        await Exec(conn, null, """
            INSERT INTO activity_reads(staff_id,last_seen_id)
            VALUES($1,coalesce((SELECT max(id) FROM activity_feed),0))
            ON CONFLICT(staff_id) DO UPDATE SET last_seen_id=excluded.last_seen_id
            """, chief);
    }
}
