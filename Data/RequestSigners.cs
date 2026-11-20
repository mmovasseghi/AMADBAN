namespace Amadban.Data;

public sealed record RequestSigner(long? DepartmentId, long? ExpertId, string? Department, string? Expert);

public sealed partial class Store
{
    public async Task<(long? Id, string? Name)> MeetingExtraAsync(long invoice)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn,null,"""
            SELECT p.id,concat_ws(' ',NULLIF(p.rank,''),p.name) FROM meeting_extra_signers m
            JOIN people p ON p.id=m.person_id WHERE m.invoice_id=$1
            """,invoice);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetInt64(0),reader.GetString(1)) : (null,null);
    }

    public async Task<string> SetMeetingExtraAsync(long invoice,long person,long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Scalar(conn,tx,"SELECT id FROM invoices WHERE id=$1 AND meeting_required AND delivery_state IN ('open','returned') FOR UPDATE",invoice) is null)
            return "صورت‌جلسه پرونده باز پیدا نشد.";
        if (person == 0) await Exec(conn,tx,"DELETE FROM meeting_extra_signers WHERE invoice_id=$1",invoice);
        else
        {
            if (await Scalar(conn,tx,"SELECT id FROM people WHERE id=$1 AND active",person) is null)
                return "شخص اضافه‌شونده را بررسی کنید.";
            await Exec(conn,tx,"""
                INSERT INTO meeting_extra_signers(invoice_id,person_id,actor_id) VALUES($1,$2,$3)
                ON CONFLICT(invoice_id) DO UPDATE SET person_id=excluded.person_id,actor_id=excluded.actor_id,updated_at=now()
                """,invoice,person,actor);
        }
        await Event(conn,tx,invoice,actor,"امضاکننده اضافه صورت‌جلسه تغییر کرد");
        await tx.CommitAsync();
        return "امضاکننده صورت‌جلسه ثبت شد.";
    }

    public async Task<RequestSigner> RequestSignerAsync(long invoice)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT i.request_department_id,i.expert_person_id,d.name,
                CASE WHEN p.id IS NULL THEN NULL ELSE concat_ws(' ',NULLIF(p.rank,''),p.name) END
            FROM invoices i LEFT JOIN departments d ON d.id=i.request_department_id
            LEFT JOIN people p ON p.id=i.expert_person_id WHERE i.id=$1
            """, invoice);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new(null,null,null,null);
        return new(reader.IsDBNull(0)?null:reader.GetInt64(0),reader.IsDBNull(1)?null:reader.GetInt64(1),
            reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3));
    }

    public async Task<string> SetRequestSignerAsync(long invoice, long department, long expert, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (department > 0 && await Scalar(conn, tx, "SELECT id FROM departments WHERE id=$1 AND active", department) is null)
            return "بخش درخواست‌کننده را بررسی کنید.";
        if (expert > 0 && (department <= 0 || await Scalar(conn, tx,
            "SELECT id FROM people WHERE id=$1 AND department_id=$2 AND active", expert, department) is null))
            return "کارشناس مرتبط با همین بخش را انتخاب کنید. شخص تحویل‌گیرنده انبار می‌تواند فرد دیگری باشد.";
        if (await Exec(conn, tx, """
            UPDATE invoices SET request_department_id=NULLIF($2,0),expert_person_id=NULLIF($3,0)
            WHERE id=$1 AND delivery_state IN ('open','returned')
            """, invoice, department, expert) != 1) return "پرونده باز برای اصلاح پیدا نشد.";
        await Event(conn, tx, invoice, actor, "بخش درخواست‌کننده و کارشناس سند تعیین شد");
        await tx.CommitAsync();
        return "نام کارشناس این بخش در سند و صورت‌جلسه درج می‌شود.";
    }
}
