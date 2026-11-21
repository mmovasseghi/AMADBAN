using Npgsql;

namespace Amadban.Data;

public sealed record CreditBalance(long Id, string Code, string Title, long Allocated, long Reserved, long Paid)
{
    public long Available => Allocated - Reserved - Paid;
}

public sealed record Prepayment(long Number, long? Invoice, string? Proforma, string Vendor, long CodeId,
    string Code, string CodeTitle, string Owner, string Bank, string Sheba, long Amount, string State, string Permit, string PermitDate, long Tax,
    DateTime CreatedAt);
public sealed record CreditChange(string? Source, string Destination, long Amount, string Actor, DateTime At, string Operation, long? SourceBefore, long? DestinationBefore);

public sealed partial class Store
{
    public async Task<List<(long Id, string Code, string Title)>> CreditLabelsAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT id,code,title FROM credit_codes ORDER BY code LIMIT 200");
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<(long, string, string)>();
        while (await r.ReadAsync()) list.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public async Task<List<CreditBalance>> CreditBalancesAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT id,code,title,allocated,reserved,paid FROM credit_balance ORDER BY code LIMIT 200");
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<CreditBalance>();
        while (await r.ReadAsync())
            list.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5)));
        return list;
    }

    public async Task<List<CreditChange>> CreditHistoryAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT src,dst,amount,actor,occurred_at,operation,source_before,destination_before FROM (
                SELECT s.code src,d.code dst,m.amount,u.display_name actor,m.occurred_at,m.id,
                    CASE WHEN m.source_id IS NULL THEN 'تخصیص' ELSE 'انتقال' END operation,
                    m.source_before,m.destination_before
                FROM credit_changes m LEFT JOIN credit_codes s ON s.id=m.source_id
                JOIN credit_codes d ON d.id=m.destination_id JOIN staff u ON u.id=m.actor_id
                UNION ALL
                SELECT s.code,d.code,m.amount,u.display_name,m.occurred_at,m.id,'اصلاح کد',NULL,NULL
                FROM credit_reclassifications m JOIN credit_codes s ON s.id=m.source_id
                JOIN credit_codes d ON d.id=m.destination_id JOIN staff u ON u.id=m.actor_id
            ) changes ORDER BY occurred_at DESC,id DESC LIMIT 30
            """);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<CreditChange>();
        while (await r.ReadAsync())
            list.Add(new(r.IsDBNull(0) ? null : r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetString(3),
                r.GetDateTime(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetInt64(6), r.IsDBNull(7) ? null : r.GetInt64(7)));
        return list;
    }

    public async Task<List<string>> NoticesAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT s.display_name || ' · ' || n.message FROM notices n JOIN staff s ON s.id=n.actor_id
            ORDER BY n.id DESC LIMIT 10
            """);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    public async Task<string> AddCreditCodeAsync(string code, string title, long actor, string role)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Exec(conn, tx, "INSERT INTO credit_codes(code,title) VALUES($1,$2) ON CONFLICT DO NOTHING", code, title) == 0)
            return "این شماره کد اعتباری قبلاً ثبت شده است.";
        await Activity(conn, tx, actor, "کد اعتباری تعریف شد", code + " · " + title, "credit");
        await NotifyChief(conn, tx, actor, role, "کد اعتباری " + code + " ساخته شد.");
        await tx.CommitAsync();
        return "کد اعتباری ثبت شد.";
    }

    public async Task<string> ChangeCreditAsync(long from, long to, long amount, long actor, string role)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await LockCredit(conn, tx);
        var destinationBefore = await Available(conn, tx, to);
        if (from == to || destinationBefore is null) return "کد مقصد را بررسی کنید.";
        long sourceBefore = 0;
        if (from > 0)
        {
            var available = await Available(conn, tx, from);
            if (available is null || available < amount) return "اعتبار قابل استفاده مبدأ کافی نیست.";
            sourceBefore = available.Value;
        }
        await Exec(conn, tx, """
            INSERT INTO credit_changes(source_id,destination_id,amount,actor_id,source_before,destination_before)
            VALUES(NULLIF($1,0),$2,$3,$4,CASE WHEN $1=0 THEN NULL ELSE $5 END,$6)
            """, from, to, amount, actor, sourceBefore, destinationBefore.Value);
        var verb = from > 0 ? "انتقال اعتبار" : "افزایش اعتبار";
        await Activity(conn,tx,actor,verb,$"مبلغ {Persian.Money(amount)} ریال · مبدأ {from} · مقصد {to}","credit");
        await NotifyChief(conn, tx, actor, role, verb + " به مبلغ " + Persian.Money(amount) + " ریال");
        await tx.CommitAsync();
        return verb + " ثبت شد.";
    }

    public async Task<Prepayment?> ReadPrepaymentAsync(long key, bool byInvoice)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await FindPrepayment(conn, null, key, byInvoice);
    }

    public async Task<(long? Number, string Message)> ReserveAsync(Guid submission, long invoice, string proforma, string vendor, long code, string owner,
        string bank, string sheba, long amount, string permit, string permitDate, long tax, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await LockCredit(conn, tx);
        var existing = await Scalar(conn, tx,
            "SELECT number FROM prepayments WHERE submission_id=$1 AND created_by=$2", submission, actor);
        if (existing is long previousNumber) return (previousNumber, $"پیش‌پرداخت {previousNumber} قبلاً با همین درخواست ثبت شده است.");
        if (invoice > 0)
        {
            var recordedVendor = await Scalar(conn, tx, """
                SELECT vendor FROM invoices WHERE id=$1 AND kind='credit' AND document_number IS NOT NULL
                AND total=$2 AND paid_at IS NULL AND delivery_state IN ('open','returned') FOR UPDATE
                """, invoice, amount) as string;
            if (recordedVendor is null) return (null, "سند اعتباری و مبلغ پرونده را بررسی کنید.");
            if (await FindPrepayment(conn, tx, invoice, byInvoice: true) is not null) return (null, "این سند پیش‌پرداخت دارد.");
            vendor = recordedVendor;
        }
        var available = await Available(conn, tx, code);
        if (available is null) return (null, "کد اعتباری را انتخاب کنید.");
        if (available < amount)
        {
            var codeName = await Scalar(conn, tx, "SELECT code FROM credit_codes WHERE id=$1", code) as string ?? code.ToString();
            var shortage = amount - available.Value;
            await Activity(conn, tx, actor, "کسری اعتبار برای پیش‌پرداخت",
                $"کد {codeName} · کسری {Persian.Money(shortage)} ریال · {vendor}", "credit");
            await tx.CommitAsync();
            return (null, $"مانده قابل استفاده {Persian.Money(available.Value)} ریال و کسری {Persian.Money(shortage)} ریال است. رئیس آماد از کسری مطلع شد؛ می‌توانید اعتبار کد را افزایش دهید یا منتقل کنید.");
        }
        var number = (long)(await Scalar(conn, tx, "UPDATE counters SET last_value=last_value+1 WHERE name='prepayment' RETURNING last_value"))!;
        await Exec(conn, tx, """
            INSERT INTO prepayments(number,invoice_id,proforma_number,vendor,code_id,owner,bank,sheba,amount,state,created_by,permit_number,permit_date,tax_amount,submission_id)
            VALUES($1,NULLIF($2,0),NULLIF($3,''),$4,$5,$6,$7,$8,$9,'reserved',$10,$11,$12,$13,$14)
            """, number, invoice, proforma, vendor, code, owner, bank, sheba, amount, actor, permit, permitDate, tax, submission);
        if (invoice > 0) await Event(conn, tx, invoice, actor, "ثبت پیش‌پرداخت");
        await tx.CommitAsync();
        return (number, $"پیش‌پرداخت {number} ثبت شد و اعتبار رزرو شد.");
    }

    public async Task<string> PrepaymentActionAsync(long number, string action, long code, bool proxy, long actor, string role)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await LockCredit(conn, tx);
        var p = await FindPrepayment(conn, tx, number, byInvoice: false);
        if (p is null || p.State == "void") return "پیش‌پرداخت فعال پیدا نشد.";
        if (action == "pay" && role == "agent" && p.State == "reserved")
        {
            if (p.Invoice is long id)
            {
                if (await Exec(conn, tx, """
                    UPDATE invoices SET paid_at=now(),paid_by=$3,proxy_required=$2
                    WHERE id=$1 AND paid_at IS NULL AND delivery_state IN ('open','returned')
                    """, id, proxy, actor) != 1) return "سند برای ثبت پرداخت آماده نیست.";
                await Event(conn, tx, id, actor, "ثبت پرداخت اعتباری");
            }
            else if (proxy) return "پرداخت گروهی فقط وقتی پیش‌پرداخت به همین پرونده ثبت شده باشد.";
            await Exec(conn, tx, "UPDATE prepayments SET state='paid' WHERE number=$1", number);
        }
        else if (action == "void" && role == "agent" && p.State == "reserved")
        {
            await Exec(conn, tx, "UPDATE prepayments SET state='void' WHERE number=$1", number);
            if (p.Invoice is long id) await Event(conn, tx, id, actor, $"ابطال پیش‌پرداخت {number}");
        }
        else if (action == "code" && code != p.CodeId)
        {
            var available = await Available(conn, tx, code);
            if (available is null || available < p.Amount) return "اعتبار کد مقصد برای انتقال کافی نیست.";
            await Exec(conn, tx, "UPDATE prepayments SET code_id=$2 WHERE number=$1", number, code);
            var destination = await Scalar(conn, tx, "SELECT code FROM credit_codes WHERE id=$1", code);
            var description = $"اصلاح کد پیش‌پرداخت {number} از {p.Code} به {destination}، {Persian.Money(p.Amount)} ریال";
            await Exec(conn, tx, """
                INSERT INTO credit_reclassifications(prepayment_number,source_id,destination_id,amount,actor_id)
                VALUES($1,$2,$3,$4,$5)
                """, number, p.CodeId, code, p.Amount, actor);
            if (p.Invoice is long id) await Event(conn, tx, id, actor, description);
            else await Activity(conn, tx, actor, "کد پیش‌پرداخت اصلاح شد", description, "credit");
            await NotifyChief(conn, tx, actor, role, description);
        }
        else return "این اقدام در وضعیت فعلی قابل انجام نیست.";
        await tx.CommitAsync();
        return "تغییر ثبت شد.";
    }


    private const string PrepaymentSelect = """
        SELECT p.number,p.invoice_id,p.proforma_number,p.vendor,p.code_id,c.code,c.title,p.owner,p.bank,p.sheba,p.amount,p.state,p.permit_number,p.permit_date,p.tax_amount,p.created_at
        FROM prepayments p JOIN credit_codes c ON c.id=p.code_id
        """;

    private static async Task<Prepayment?> FindPrepayment(NpgsqlConnection conn, NpgsqlTransaction? tx, long key, bool byInvoice)
    {
        await using var cmd = Command(conn, tx, PrepaymentSelect + (byInvoice ? " WHERE p.invoice_id=$1 AND p.state<>'void'" : " WHERE p.number=$1"), key);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadPrepayment(r) : null;
    }

    private static Prepayment ReadPrepayment(NpgsqlDataReader r) => new(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1),
        r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetString(5), r.GetString(6),
        r.GetString(7), r.GetString(8), r.GetString(9), r.GetInt64(10), r.GetString(11), r.GetString(12), r.GetString(13), r.GetInt64(14),r.GetDateTime(15));

    private static Task<int> LockCredit(NpgsqlConnection conn, NpgsqlTransaction tx) =>
        Exec(conn, tx, "SELECT pg_advisory_xact_lock(782541996)");

    private static async Task<long?> Available(NpgsqlConnection conn, NpgsqlTransaction tx, long id) =>
        await Scalar(conn, tx, "SELECT allocated-reserved-paid FROM credit_balance WHERE id=$1", id) as long?;

    private static Task<int> NotifyChief(NpgsqlConnection conn, NpgsqlTransaction tx, long actor, string role, string message) =>
        role == "agent" ? Exec(conn, tx, "INSERT INTO notices(actor_id,message) VALUES($1,$2)", actor, message) : Task.FromResult(0);
}
