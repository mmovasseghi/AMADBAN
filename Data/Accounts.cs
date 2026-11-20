namespace Amadban.Data;

public sealed record StaffEvent(string Actor, string Target, string Action, DateTime At);

public sealed partial class Store
{
    public async Task<bool> SessionValidAsync(long id, Guid stamp)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null,
            "SELECT 1 FROM staff WHERE id=$1 AND active AND security_stamp=$2", id, stamp) is int;
    }

    public async Task<string> ResetStaffPasswordAsync(long id, string password, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var username = await Scalar(conn, tx, "SELECT username FROM staff WHERE id=$1 FOR UPDATE", id) as string;
        if (username is null) return "حساب پیدا نشد.";
        await Exec(conn, tx, "UPDATE staff SET password_hash=$2,security_stamp=gen_random_uuid() WHERE id=$1",
            id, Passwords.HashPassword(username, password));
        await Exec(conn, tx, "INSERT INTO staff_events(actor_id,target_id,action) VALUES($1,$2,'reset-password')", actor, id);
        await Activity(conn,tx,actor,"رمز حساب کاربری بازنشانی شد",$"شناسه کاربر {id}","staff",id);
        await tx.CommitAsync();
        return "رمز تازه ثبت شد؛ نشست‌های قبلی این حساب پایان یافتند.";
    }

    public async Task<string> SetStaffActiveAsync(long id, bool active, long actor)
    {
        if (id == actor) return "غیرفعال‌کردن یا فعال‌کردن حساب خودتان از این بخش مجاز نیست.";
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Exec(conn, tx, "SELECT pg_advisory_xact_lock(782541997)");
        string? role = null;
        bool previous = false;
        await using (var cmd = Command(conn, tx, "SELECT role,active FROM staff WHERE id=$1 FOR UPDATE", id))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync()) { role = reader.GetString(0); previous = reader.GetBoolean(1); }
        }
        if (role is null) return "حساب پیدا نشد.";
        if (previous == active) return "وضعیت حساب از قبل همین است.";
        if (!active && role == "chief" &&
            await Scalar(conn, tx, "SELECT count(*) FROM staff WHERE role='chief' AND active") is 1L)
            return "دست‌کم یک حساب فعال رئیس آماد باید باقی بماند.";
        await Exec(conn, tx, "UPDATE staff SET active=$2,security_stamp=gen_random_uuid() WHERE id=$1", id, active);
        await Exec(conn, tx, "INSERT INTO staff_events(actor_id,target_id,action) VALUES($1,$2,$3)",
            actor, id, active ? "activate" : "deactivate");
        await Activity(conn,tx,actor,active?"حساب کاربری فعال شد":"حساب کاربری غیرفعال شد",$"شناسه کاربر {id}","staff",id);
        await tx.CommitAsync();
        return active ? "حساب فعال شد." : "حساب غیرفعال شد و نشست‌های آن پایان یافتند.";
    }

    public async Task<List<StaffEvent>> StaffEventsAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT actor.display_name,target.display_name,e.action,e.occurred_at
            FROM staff_events e JOIN staff actor ON actor.id=e.actor_id JOIN staff target ON target.id=e.target_id
            ORDER BY e.id DESC LIMIT 20
            """);
        await using var reader = await cmd.ExecuteReaderAsync();
        var events = new List<StaffEvent>();
        while (await reader.ReadAsync()) events.Add(new(reader.GetString(0), reader.GetString(1),
            reader.GetString(2), reader.GetDateTime(3)));
        return events;
    }
}
