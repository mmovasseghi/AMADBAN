namespace Amadban.Data;

public sealed partial class Store
{
    public async Task<(byte[] Content, string Type, DateTime Updated)?> BrandImageAsync(string code)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT content,content_type,updated_at FROM branding WHERE code=$1", code);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.GetFieldValue<byte[]>(0), r.GetString(1), r.GetDateTime(2)) : null;
    }

    public async Task SaveBrandImageAsync(string code, DocumentUpload file, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Exec(conn, tx, """
            INSERT INTO branding(code,content_type,content) VALUES($1,$2,$3)
            ON CONFLICT(code) DO UPDATE SET content_type=excluded.content_type,content=excluded.content,updated_at=now()
            """, code, file.Type, file.Content);
        await Activity(conn, tx, actor, "نشان سازمانی ثبت شد", code == "faraja" ? "نشان فراجا" : "نشان معاونت بهداشت، امداد و درمان", "organization");
        await tx.CommitAsync();
    }

    public async Task<Dictionary<string, string>> SignersAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT s.role,coalesce(CASE WHEN p.rank='' THEN '' ELSE p.rank || ' ' END || p.name,s.name)
            FROM signers s LEFT JOIN people p ON p.id=s.person_id
            """);
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new Dictionary<string, string>();
        while (await r.ReadAsync()) result.Add(r.GetString(0), r.GetString(1));
        return result;
    }

    public async Task SaveSignerAsync(string role, string name, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Exec(conn, tx, """
            INSERT INTO signers(role,name) VALUES($1,$2) ON CONFLICT(role) DO UPDATE SET name=excluded.name,person_id=NULL
            """, role, name);
        await Activity(conn, tx, actor, "نام امضاکننده تغییر کرد", role + " · " + name, "organization");
        await tx.CommitAsync();
    }

    public async Task<bool> AssignSignerAsync(string role, long person, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var personName = await Scalar(conn, tx, "SELECT name FROM people WHERE id=$1 AND active", person) as string;
        if (personName is null) return false;
        var updated = await Exec(conn, tx, """
            INSERT INTO signers(role,name,person_id)
            SELECT $1,p.name,p.id FROM people p WHERE p.id=$2 AND p.active
            ON CONFLICT(role) DO UPDATE SET name=excluded.name,person_id=excluded.person_id
            """, role, person);
        if (updated != 1) return false;
        await Activity(conn, tx, actor, "شخص امضاکننده تعیین شد", role + " · " + personName, "organization");
        await tx.CommitAsync();
        return true;
    }
}
