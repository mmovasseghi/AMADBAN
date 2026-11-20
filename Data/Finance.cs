namespace Amadban.Data;

public sealed partial class Store
{
    private const string ReadyConditions = """
        document_number IS NOT NULL AND paid_at IS NOT NULL
          AND (NOT warehouse_required OR kardex_number IS NOT NULL)
          AND NOT EXISTS (
            SELECT 1 FROM (VALUES
                ('invoice',true), ('pos',i.kind='cash'), ('taxpayer',tax_required AND i.kind='credit'),
                ('tax',tax_required), ('proxy',proxy_required), ('meeting',meeting_required), ('account',account_required)
            ) required(name,needed)
            WHERE needed AND NOT EXISTS (
                SELECT 1 FROM invoice_attachments a WHERE a.invoice_id=i.id AND a.kind=required.name
            )
          )
        """;

    public async Task<string> UpdateFinanceAsync(long id, long actor, string role, string action)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Scalar(conn, tx,
            "SELECT id FROM invoices WHERE id=$1 AND document_number IS NOT NULL FOR UPDATE", id) is null)
            return "سند پیدا نشد.";
        string sql, message;
        switch (action)
        {
            case "pay" when role == "agent":
                sql = """
                    UPDATE invoices SET paid_at=now(), paid_by=$2
                    WHERE id=$1 AND kind='cash' AND paid_at IS NULL AND delivery_state IN ('open','returned')
                    """;
                message = "ثبت پرداخت نقدی";
                break;
            case "deliver":
                sql = "UPDATE invoices i SET delivery_state='delivered' WHERE id=$1 AND delivery_state IN ('open','returned') AND " + ReadyConditions;
                message = "ثبت تحویل به مالی";
                break;
            case "require-proxy":
            case "clear-proxy":
                sql = action == "require-proxy"
                    ? "UPDATE invoices SET proxy_required=true WHERE id=$1 AND kind='credit' AND paid_at IS NOT NULL AND delivery_state IN ('open','returned')"
                    : "UPDATE invoices SET proxy_required=false WHERE id=$1 AND kind='credit' AND paid_at IS NOT NULL AND delivery_state IN ('open','returned')";
                message = action == "require-proxy" ? "ثبت نیاز به نیابتی" : "رفع نیاز به نیابتی";
                break;
            case "return":
                sql = "UPDATE invoices SET delivery_state='returned' WHERE id=$1 AND delivery_state IN ('delivered','closed')";
                message = "ثبت برگشت از مالی";
                break;
            case "close":
                sql = "UPDATE invoices SET delivery_state='closed' WHERE id=$1 AND delivery_state='delivered'";
                message = "تأیید مالی و بستن سند";
                break;
            default: return "اقدام برای این حساب فعال نیست.";
        }
        var values = action == "pay" ? new object[] { id, actor } : [id];
        if (await Exec(conn, tx, sql, values) == 0) return "اقدام انجام نشد. وضعیت سند و مدارک را بررسی کنید.";
        await Event(conn, tx, id, actor, message);
        await tx.CommitAsync();
        return message + " انجام شد.";
    }

    public async Task<List<(string Username, string Name, string Role)>> StaffAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT username,display_name,role FROM staff ORDER BY id LIMIT 100");
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<(string, string, string)>();
        while (await r.ReadAsync()) result.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
        return result;
    }

    public async Task<bool> AddStaffAsync(string username, string name, string role, string password)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Exec(conn, null, """
            INSERT INTO staff(username,display_name,role,password_hash) VALUES($1,$2,$3,$4)
            ON CONFLICT (username) DO NOTHING
            """, username, name, role, Passwords.HashPassword(username, password)) == 1;
    }
}
