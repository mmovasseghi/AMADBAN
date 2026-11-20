using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace Amadban.Data;

public sealed record Staff(long Id, string Username, string Name, string Role, string Hash, Guid SecurityStamp);
public sealed record InvoiceSummary(long Id, long? Document, string Number, string Vendor, string Subject,
    DateOnly Date, string Kind, long Total, bool Paid, string Delivery, bool Warehouse, string? Kardex,
    string ReviewStage);
public sealed record InvoiceLine(string Title, string Unit, decimal Quantity, long Price, long Amount);
public sealed record InvoiceEntry(Guid Submission, string Number, string Vendor, string Subject, DateOnly Date,
    string Kind, bool Warehouse, bool Meeting, bool Account, bool Tax, IReadOnlyList<InvoiceLine> Lines, long CashCredit);
public sealed record Attachment(long Id, string Kind, string Name);
public sealed record InvoiceEvent(string Action, string Actor, DateTime At);
public sealed record InvoiceDetail(InvoiceSummary Summary, bool Meeting, bool Account, bool Tax, bool Proxy, bool PosInInvoice,
    DateOnly? KardexDate, DateOnly? WarehouseReceiptDate, bool HasManualStock,
    IReadOnlyList<InvoiceLine> Lines, IReadOnlyList<Attachment> Attachments, IReadOnlyList<InvoiceEvent> Events);

public sealed partial class Store(NpgsqlDataSource source)
{
    private static readonly PasswordHasher<string> Passwords = new();
    private const string SummaryColumns = """
        id, document_number, number, vendor, subject, invoice_date, kind, total, paid_at IS NOT NULL,
        delivery_state, warehouse_required, kardex_number, review_stage
        """;
    private const string InvoiceCriteria = """
            FROM invoices i WHERE invoice_date BETWEEN $1 AND $2
              AND ($3 = '' OR strpos(lower(vendor || ' ' || subject || ' ' || number || ' ' || coalesce(document_number::text, '')), lower($3)) > 0)
              AND ($4 = '' OR strpos(lower(subject), lower($4)) > 0)
              AND ($5 = '' OR i.kind=$5)
              AND ($6 = '' OR EXISTS (SELECT 1 FROM credit_codes c WHERE c.id=i.cash_credit_code AND c.code=$6)
                            OR EXISTS (SELECT 1 FROM prepayments p JOIN credit_codes c ON c.id=p.code_id
                                      WHERE p.invoice_id=i.id AND p.state<>'void' AND c.code=$6))
            """;

    private static string InvoiceStatusFilter(string status) => status switch
    {
        "due" => " AND kind='credit' AND paid_at IS NULL AND invoice_date+45 <= $7",
        "returned" => " AND delivery_state='returned'",
        "returned-finance" => " AND delivery_state='returned' AND review_stage='finance'",
        "returned-deputy" => " AND delivery_state='returned' AND review_stage='deputy'",
        "kardex" => " AND warehouse_required AND kardex_number IS NULL",
        "kardex-late" => " AND warehouse_required AND kardex_number IS NULL AND invoice_date < $7",
        "kardex-current" => " AND warehouse_required AND kardex_number IS NULL AND invoice_date >= $7",
        "document" => " AND document_number IS NULL",
        "paid" => " AND paid_at IS NOT NULL",
        "tax-pending" => " AND document_number IS NOT NULL AND NOT EXISTS (SELECT 1 FROM invoice_attachments a WHERE a.invoice_id=i.id AND a.kind='tax')",
        "ready" => " AND delivery_state IN ('open','returned') AND " + ReadyConditions,
        "delivered" => " AND delivery_state='delivered'",
        "closed" => " AND delivery_state='closed'",
        _ => ""
    };

    private static DateOnly? StatusDateArgument(string status) => status switch
    {
        "due" => Persian.Today.AddDays(7),
        "kardex-late" or "kardex-current" => Persian.ParseDate(Persian.Date(Persian.Today)[..7] + "/01"),
        _ => null
    };

    public async Task InitializeAsync(string directory, string? adminPassword)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Exec(conn, tx, "SELECT pg_advisory_xact_lock(782541995)");
        await Exec(conn, tx, await File.ReadAllTextAsync(Path.Combine(directory, "database", "schema.sql")));
        if (await Scalar(conn, tx, "SELECT count(*) FROM staff") is 0L)
        {
            if (string.IsNullOrWhiteSpace(adminPassword) || adminPassword.Length < 12)
                throw new InvalidOperationException("AMADBAN_ADMIN_PASSWORD must be at least 12 characters on first run.");
            await Exec(conn, tx, """
                INSERT INTO staff (username,display_name,password_hash,role) VALUES ('admin','رئیس آماد',$1,'chief')
                """, Passwords.HashPassword("admin", adminPassword));
        }
        await tx.CommitAsync();
    }

    public async Task<Staff?> AuthenticateAsync(string username, string password)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null,
            "SELECT id, username, display_name, role, password_hash, security_stamp FROM staff WHERE username=$1 AND active", username);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        var staff = new Staff(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetGuid(5));
        return Passwords.VerifyHashedPassword(username, staff.Hash, password) == PasswordVerificationResult.Failed ? null : staff;
    }

    public async Task<(List<InvoiceSummary> Rows, long Count, long Total)> ListAsync(
        string status, string search, string subject, string kind, string credit, DateOnly from, DateOnly to, int page, bool totals = false)
    {
        await using var conn = await source.OpenConnectionAsync();
        var filter = InvoiceStatusFilter(status);
        var rows = new List<InvoiceSummary>();
        var offset = (page - 1) * 25;
        var listArgs = StatusDateArgument(status) is DateOnly cutoff
            ? new object[] { from, to, search, subject, kind, credit, cutoff, offset }
            : [from, to, search, subject, kind, credit, offset];
        await using (var cmd = Command(conn, null, "SELECT " + SummaryColumns + " " + InvoiceCriteria + " " + filter +
            " ORDER BY invoice_date DESC, id DESC LIMIT 26 OFFSET $" + listArgs.Length, listArgs))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) rows.Add(ReadSummary(r));
        }
        if (!totals) return (rows, 0, 0);
        var args = StatusDateArgument(status) is DateOnly summaryCutoff
            ? new object[] { from, to, search, subject, kind, credit, summaryCutoff }
            : [from, to, search, subject, kind, credit];
        await using var summary = Command(conn, null, "SELECT count(*), coalesce(sum(total),0)::bigint " + InvoiceCriteria + " " + filter, args);
        await using var sr = await summary.ExecuteReaderAsync();
        await sr.ReadAsync();
        return (rows, sr.GetInt64(0), sr.GetInt64(1));
    }

    public async Task<long> AddInvoiceAsync(InvoiceEntry entry, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var inserted = await Scalar(conn, tx, """
            INSERT INTO invoices(submission_id,number,vendor,subject,invoice_date,kind,warehouse_required,meeting_required,
                                 account_required,tax_required,total,created_by,cash_credit_code)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,CASE WHEN $6='cash' THEN NULLIF($13,0) ELSE NULL END)
            ON CONFLICT (submission_id) DO NOTHING RETURNING id
            """, entry.Submission, entry.Number, entry.Vendor, entry.Subject, entry.Date, entry.Kind,
            entry.Warehouse, entry.Meeting, entry.Account, entry.Tax, entry.Lines.Sum(x => x.Amount), actor, entry.CashCredit);
        if (inserted is not long id)
        {
            var known = await Scalar(conn, tx, "SELECT id FROM invoices WHERE submission_id=$1 AND created_by=$2", entry.Submission, actor);
            return known is long existing ? existing : throw new InvalidOperationException("Invalid submission.");
        }
        await AddLines(conn, tx, id, entry.Lines);
        await Event(conn, tx, id, actor, "ثبت فاکتور");
        await tx.CommitAsync();
        return id;
    }

    private static async Task AddLines(NpgsqlConnection conn, NpgsqlTransaction tx, long id, IReadOnlyList<InvoiceLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            await Exec(conn, tx, """
                INSERT INTO invoice_lines(invoice_id,position,title,unit,quantity,unit_price,amount) VALUES($1,$2,$3,$4,$5,$6,$7)
                """, id, i + 1, line.Title, line.Unit, line.Quantity, line.Price, line.Amount);
        }
    }

    public async Task<InvoiceDetail?> DetailAsync(long id)
    {
        await using var conn = await source.OpenConnectionAsync();
        InvoiceSummary summary;
        bool meeting, account, tax, proxy, posInInvoice;
        DateOnly? kardexDate;
        await using (var cmd = Command(conn, null, "SELECT " + SummaryColumns +
            ", meeting_required, account_required, tax_required, kardex_date, proxy_required, pos_in_invoice FROM invoices WHERE id=$1", id))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return null;
            summary = ReadSummary(r);
            meeting = r.GetBoolean(13);
            account = r.GetBoolean(14);
            tax = r.GetBoolean(15);
            kardexDate = r.IsDBNull(16) ? null : r.GetFieldValue<DateOnly>(16);
            proxy = r.GetBoolean(17);
            posInInvoice = r.GetBoolean(18);
        }
        var lines = new List<InvoiceLine>();
        await using (var cmd = Command(conn, null,
            "SELECT title,unit,quantity,unit_price,amount FROM invoice_lines WHERE invoice_id=$1 ORDER BY position", id))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) lines.Add(new(r.GetString(0), r.GetString(1), r.GetDecimal(2), r.GetInt64(3), r.GetInt64(4)));
        }
        var attachments = new List<Attachment>();
        await using (var cmd = Command(conn, null,
            "SELECT id,kind,filename FROM invoice_attachments WHERE invoice_id=$1 ORDER BY id DESC", id))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) attachments.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        }
        var events = new List<InvoiceEvent>();
        await using (var cmd = Command(conn, null, """
            SELECT e.action,s.display_name,e.occurred_at FROM invoice_events e JOIN staff s ON s.id=e.actor_id
            WHERE e.invoice_id=$1 ORDER BY e.id DESC LIMIT 25
            """, id))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) events.Add(new(r.GetString(0), r.GetString(1), r.GetDateTime(2)));
        }
        DateOnly? warehouseReceiptDate;
        bool hasManualStock;
        await using (var cmd = Command(conn, null, """
            SELECT (SELECT receipt_date FROM warehouse_receipts WHERE invoice_id=$1 AND state='active'),
                   EXISTS(SELECT 1 FROM stock_events WHERE invoice_id=$1 AND receipt_id IS NULL)
            """, id))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            await r.ReadAsync();
            warehouseReceiptDate = r.IsDBNull(0) ? null : r.GetFieldValue<DateOnly>(0);
            hasManualStock = r.GetBoolean(1);
        }
        return new(summary, meeting, account, tax, proxy, posInInvoice, kardexDate, warehouseReceiptDate,
            hasManualStock, lines, attachments, events);
    }

    public async Task<bool> IssueDocumentAsync(long id, long actor, string role)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var kind = await Scalar(conn, tx, "SELECT kind FROM invoices WHERE id=$1 AND document_number IS NULL FOR UPDATE", id) as string;
        if (kind is null || (kind == "cash" && role != "agent") ||
            (kind == "credit" && role is not ("warehouse" or "agent" or "chief"))) return false;
        var number = (long)(await Scalar(conn, tx,
            "UPDATE counters SET last_value=last_value+1 WHERE name='document' RETURNING last_value"))!;
        if (kind == "cash")
        {
            await Exec(conn, tx, "UPDATE invoices SET document_number=$2,paid_at=now(),paid_by=$3 WHERE id=$1", id, number, actor);
            await Event(conn, tx, id, actor, "صدور سند و ثبت پرداخت نقدی");
        }
        else
        {
            await Exec(conn, tx, "UPDATE invoices SET document_number=$2 WHERE id=$1", id, number);
            await Event(conn, tx, id, actor, "صدور سند");
        }
        await tx.CommitAsync();
        return true;
    }

    public async Task<bool> SaveKardexAsync(long id, long actor, string number, DateOnly date)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var updated = await Exec(conn, tx, """
            UPDATE invoices SET kardex_number=$2, kardex_date=$3
            WHERE id=$1 AND warehouse_required AND document_number IS NOT NULL
              AND delivery_state NOT IN ('closed','delivered')
            """, id, number, date);
        if (updated == 0) return false;
        await Event(conn, tx, id, actor, "ثبت کارتکس");
        await tx.CommitAsync();
        return true;
    }

    public async Task<bool> AddAttachmentAsync(long invoice, long actor, string kind, string name, string type, byte[] bytes)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Scalar(conn, tx,
            "SELECT id FROM invoices WHERE id=$1 AND delivery_state NOT IN ('closed','delivered') FOR UPDATE", invoice) is null) return false;
        await Exec(conn, tx, """
            INSERT INTO invoice_attachments(invoice_id,kind,filename,content_type,content,uploaded_by)
            VALUES ($1,$2,$3,$4,$5,$6)
            """, invoice, kind, name, type, bytes, actor);
        await Event(conn, tx, invoice, actor, "پیوست مدرک");
        await tx.CommitAsync();
        return true;
    }

    public async Task<(byte[] Content, string Type, string Name)?> ReadAttachmentAsync(long id, long attachment)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null,
            "SELECT content,content_type,filename FROM invoice_attachments WHERE invoice_id=$1 AND id=$2", id, attachment);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.GetFieldValue<byte[]>(0), r.GetString(1), r.GetString(2)) : null;
    }

    public static IReadOnlyList<string> Missing(InvoiceDetail entry)
    {
        var own = entry.Attachments.Select(a => a.Kind).ToHashSet();
        var missing = new List<string>();
        if (entry.Summary.Document is null) missing.Add("شماره سند");
        if (!entry.Summary.Paid) missing.Add("ثبت پرداخت");
        if (!own.Contains("invoice")) missing.Add("اسکن فاکتور");
        if (entry.Summary.Kind == "cash" && !entry.PosInInvoice && !own.Contains("pos")) missing.Add("پرفراژ کارت‌خوان");
        if (entry.Meeting && !own.Contains("meeting")) missing.Add("صورت‌جلسه");
        if (entry.Account && !own.Contains("account")) missing.Add("تأییدیه حساب");
        if (entry.Tax && entry.Summary.Kind == "credit" && !own.Contains("taxpayer")) missing.Add("برگه مؤدیان");
        if (!own.Contains("tax")) missing.Add("برگه ثبت مالیات");
        if (entry.Proxy && !own.Contains("proxy")) missing.Add("برگه نیابتی");
        if (entry.Summary.Warehouse && entry.WarehouseReceiptDate is null && !entry.HasManualStock) missing.Add("ورود اقلام به انبار");
        if (entry.Summary.Warehouse && entry.Summary.Kardex is null) missing.Add("ثبت کارتکس");
        return missing;
    }

    private static InvoiceSummary ReadSummary(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4),
        r.GetFieldValue<DateOnly>(5), r.GetString(6), r.GetInt64(7), r.GetBoolean(8), r.GetString(9), r.GetBoolean(10),
        r.IsDBNull(11) ? null : r.GetString(11),r.GetString(12));

    private static async Task<int> Event(NpgsqlConnection conn, NpgsqlTransaction tx, long invoice, long actor, string action)
    {
        var count=await Exec(conn,tx,"INSERT INTO invoice_events(invoice_id,actor_id,action) VALUES($1,$2,$3)",invoice,actor,action);
        var document=await Scalar(conn,tx,"SELECT document_number FROM invoices WHERE id=$1",invoice);
        await Activity(conn,tx,actor,action,document is long number?$"سند {number} · پرونده {invoice}":$"پرونده {invoice}","invoice",invoice);
        return count;
    }

    private static NpgsqlCommand Command(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params object[] values)
    {
        var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var value in values) cmd.Parameters.Add(new NpgsqlParameter { Value = value });
        return cmd;
    }

    private static async Task<object?> Scalar(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params object[] values)
    {
        await using var cmd = Command(conn, tx, sql, values);
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task<int> Exec(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params object[] values)
    {
        await using var cmd = Command(conn, tx, sql, values);
        return await cmd.ExecuteNonQueryAsync();
    }
}
