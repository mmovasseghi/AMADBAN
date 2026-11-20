namespace Amadban.Data;

public sealed partial class Store
{
    private const string ReadyConditions = """
        document_number IS NOT NULL AND paid_at IS NOT NULL
          AND (NOT warehouse_required OR kardex_number IS NOT NULL)
          AND (NOT warehouse_required OR EXISTS(
              SELECT 1 FROM warehouse_receipts wr WHERE wr.invoice_id=i.id AND wr.state='active'
          ) OR EXISTS(
              SELECT 1 FROM stock_events se WHERE se.invoice_id=i.id AND se.receipt_id IS NULL
          ))
          AND NOT EXISTS (
            SELECT 1 FROM (VALUES
                ('invoice',true), ('pos',i.kind='cash' AND NOT i.pos_in_invoice), ('taxpayer',tax_required AND i.kind='credit'),
                ('tax',true), ('proxy',proxy_required), ('meeting',meeting_required), ('account',account_required)
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
                message = "ثبت تحویل یا ارسال مجدد سند";
                break;
            case "send-deputy":
                sql = "UPDATE invoices SET review_stage='deputy' WHERE id=$1 AND delivery_state='delivered' AND review_stage='finance'";
                message = "ارسال سند از مالی به معاونت";
                break;
            case "require-proxy":
            case "clear-proxy":
                sql = action == "require-proxy"
                    ? "UPDATE invoices SET proxy_required=true WHERE id=$1 AND kind='credit' AND paid_at IS NOT NULL AND delivery_state IN ('open','returned')"
                    : "UPDATE invoices SET proxy_required=false WHERE id=$1 AND kind='credit' AND paid_at IS NOT NULL AND delivery_state IN ('open','returned')";
                message = action == "require-proxy" ? "ثبت نیاز به نیابتی" : "رفع نیاز به نیابتی";
                break;
            case "return-finance":
                sql = "UPDATE invoices SET delivery_state='returned',review_stage='finance' WHERE id=$1 AND delivery_state='delivered' AND review_stage='finance'";
                message = "ثبت برگشت سند از مالی";
                break;
            case "return-deputy":
                sql = "UPDATE invoices SET delivery_state='returned',review_stage='deputy' WHERE id=$1 AND delivery_state='delivered' AND review_stage='deputy'";
                message = "ثبت برگشت سند از معاونت";
                break;
            case "close":
                sql = "UPDATE invoices SET delivery_state='closed' WHERE id=$1 AND delivery_state='delivered' AND review_stage='deputy'";
                message = "تأیید نهایی و بستن سند";
                break;
            default: return "اقدام برای این حساب فعال نیست.";
        }
        var values = action == "pay" ? new object[] { id, actor } : [id];
        if (await Exec(conn, tx, sql, values) == 0) return "اقدام انجام نشد. وضعیت سند و مدارک را بررسی کنید.";
        await Event(conn, tx, id, actor, message);
        await tx.CommitAsync();
        return message + " انجام شد.";
    }

    public async Task<string> SetPosInInvoiceAsync(long id, bool embedded, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Exec(conn, tx, """
            UPDATE invoices SET pos_in_invoice=$2
            WHERE id=$1 AND kind='cash' AND delivery_state IN ('open','returned')
            """, id, embedded) != 1) return "این تغییر برای پرونده نقدی باز ممکن نیست.";
        await Event(conn, tx, id, actor, embedded ? "تأیید پرفراژ در اسکن فاکتور" : "لغو تأیید پرفراژ در اسکن فاکتور");
        await tx.CommitAsync();
        return embedded ? "پرفراژ داخل اسکن فاکتور ثبت شد." : "پرفراژ دوباره به‌صورت مدرک جدا لازم است.";
    }

    public async Task<List<(long Id, string Username, string Name, string Role, bool Active)>> StaffAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT id,username,display_name,role,active FROM staff ORDER BY id LIMIT 100");
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<(long, string, string, string, bool)>();
        while (await r.ReadAsync()) result.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4)));
        return result;
    }

    public async Task<bool> AddStaffAsync(string username, string name, string role, string password, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var id = await Scalar(conn, tx, """
            INSERT INTO staff(username,display_name,role,password_hash) VALUES($1,$2,$3,$4)
            ON CONFLICT (username) DO NOTHING RETURNING id
            """, username, name, role, Passwords.HashPassword(username, password));
        if (id is not long person) return false;
        await Activity(conn, tx, actor, "حساب کاربری ساخته شد", name + " · " + username, "staff", person);
        await tx.CommitAsync();
        return true;
    }
}
