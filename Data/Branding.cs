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

    public async Task SaveBrandImageAsync(string code, DocumentUpload file)
    {
        await using var conn = await source.OpenConnectionAsync();
        await Exec(conn, null, """
            INSERT INTO branding(code,content_type,content) VALUES($1,$2,$3)
            ON CONFLICT(code) DO UPDATE SET content_type=excluded.content_type,content=excluded.content,updated_at=now()
            """, code, file.Type, file.Content);
    }

    public async Task<Dictionary<string, string>> SignersAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT role,name FROM signers");
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new Dictionary<string, string>();
        while (await r.ReadAsync()) result.Add(r.GetString(0), r.GetString(1));
        return result;
    }

    public async Task SaveSignerAsync(string role, string name)
    {
        await using var conn = await source.OpenConnectionAsync();
        await Exec(conn, null, """
            INSERT INTO signers(role,name) VALUES($1,$2) ON CONFLICT(role) DO UPDATE SET name=excluded.name
            """, role, name);
    }
}
