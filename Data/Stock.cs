namespace Amadban.Data;

public sealed record Product(long Id, string Code, string Name, string Unit, decimal Stock, decimal Minimum);
public sealed record StockLine(string Product, string Kind, decimal Delta, string? Department, DateOnly Date, long? Invoice);
public sealed record PettyBalance(long Staff, string Name, long Opening, long Received, long Spent);
public sealed record PettyEntry(string Kind, string Reference, string Description, string Person, DateOnly Date, long Amount, long? Invoice);

public sealed partial class Store
{
    public async Task<List<Product>> ProductsAsync(string search, bool low, int page)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT id,code,name,unit,stock,minimum FROM products
            WHERE ($1='' OR strpos(lower(name || ' ' || code),lower($1))>0) AND (NOT $2 OR stock<=minimum)
            ORDER BY name,id LIMIT 26 OFFSET $3
            """, search, low, (page - 1) * 25);
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<Product>();
        while (await r.ReadAsync()) result.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4), r.GetDecimal(5)));
        return result;
    }

    public async Task<Product?> GetProductAsync(long id)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT id,code,name,unit,stock,minimum FROM products WHERE id=$1", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDecimal(4), r.GetDecimal(5)) : null;
    }

    public async Task<string> NewProductAsync(string code, string name, string unit, decimal minimum)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Exec(conn, null, "INSERT INTO products(code,name,unit,minimum) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING",
            code, name, unit, minimum) == 1 ? "کالا ثبت شد." : "کد کالا قبلاً ثبت شده است.";
    }

    public async Task<string> MoveStockAsync(Guid submission, long id, long invoice, string kind, decimal quantity,
        string department, DateOnly date, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var stock = await Scalar(conn, tx, "SELECT stock FROM products WHERE id=$1 FOR UPDATE", id);
        if (stock is not decimal current) return "کالا پیدا نشد.";
        if (await Scalar(conn, tx, "SELECT id FROM stock_events WHERE submission_id=$1", submission) is not null)
            return "این گردش قبلاً ثبت شده است.";
        if (kind == "opening" && await Scalar(conn, tx, "SELECT id FROM stock_events WHERE product_id=$1 LIMIT 1", id) is not null)
            return "برای کالایی که گردش ثبت‌شده دارد، از ورود یا خروج استفاده کنید.";
        if (invoice > 0 && (kind != "in" ||
            await Scalar(conn, tx, "SELECT id FROM invoices WHERE id=$1 AND warehouse_required", invoice) is null))
            return "پرونده انباری معتبر را انتخاب کنید.";
        var delta = kind == "out" ? -quantity : quantity;
        if (current + delta < 0) return "موجودی برای این خروج کافی نیست.";
        if (current + delta >= 1_000_000_000) return "مقدار موجودی از دامنه قابل ثبت بیشتر می‌شود.";
        await Exec(conn, tx, "UPDATE products SET stock=stock+$2 WHERE id=$1", id, delta);
        await Exec(conn, tx, """
            INSERT INTO stock_events(submission_id,product_id,invoice_id,kind,delta,department,event_date,actor_id)
            VALUES($1,$2,NULLIF($3,0),$4,$5,NULLIF($6,''),$7,$8)
            """, submission, id, invoice, kind, delta, department, date, actor);
        if (invoice > 0) await Event(conn, tx, invoice, actor, "ثبت ورود انبار");
        await tx.CommitAsync();
        return "گردش کالا ثبت شد.";
    }

    public async Task<List<StockLine>> StockHistoryAsync(long id, DateOnly from, DateOnly to)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT p.name,s.kind,s.delta,s.department,s.event_date,s.invoice_id
            FROM stock_events s JOIN products p ON s.product_id=p.id
            WHERE s.product_id=$1 AND event_date BETWEEN $2 AND $3 ORDER BY s.id DESC LIMIT 50
            """, id, from, to);
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<StockLine>();
        while (await r.ReadAsync()) result.Add(new(r.GetString(0), r.GetString(1), r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetString(3),
            r.GetFieldValue<DateOnly>(4), r.IsDBNull(5) ? null : r.GetInt64(5)));
        return result;
    }

    public async Task<List<(long Id, string Name)>> AgentsAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT id,display_name FROM staff WHERE role='agent' AND active ORDER BY id LIMIT 100");
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<(long, string)>();
        while (await r.ReadAsync()) result.Add((r.GetInt64(0), r.GetString(1)));
        return result;
    }

    public async Task<string> ReceivePettyAsync(Guid submission, long recipient, string number, DateOnly date, long amount, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Exec(conn, null, """
            INSERT INTO petty_receipts(submission_id,recipient_id,receipt_number,receipt_date,amount,actor_id)
            SELECT $1,id,$3,$4,$5,$6 FROM staff WHERE id=$2 AND role='agent' AND active ON CONFLICT DO NOTHING
            """, submission, recipient, number, date, amount, actor) == 1 ? "دریافت تنخواه ثبت شد." : "دریافت ثبت نشده یا قبلاً وارد شده است.";
    }

    public async Task<(List<PettyBalance> Balances, List<PettyEntry> Entries)> PettyReportAsync(long agent, DateOnly from, DateOnly to, int page)
    {
        await using var conn = await source.OpenConnectionAsync();
        const string view = """
            WITH activity AS (
                SELECT recipient_id AS person,receipt_date AS day,'receive' AS movement,
                    receipt_number AS reference,'دریافت تنخواه' AS description, amount,NULL::bigint AS invoice
                FROM petty_receipts
                UNION ALL
                SELECT paid_by, invoice_date, 'purchase', document_number::text,
                    subject || ' · ' || vendor, total, id
                FROM invoices WHERE kind='cash' AND paid_by IS NOT NULL
            )
            """;
        var balances = new List<PettyBalance>();
        await using (var cmd = Command(conn, null, view + """
            SELECT u.id,u.display_name,
                coalesce(sum(CASE WHEN a.movement='receive' THEN a.amount ELSE -a.amount END) FILTER (WHERE day<$2),0)::bigint,
                coalesce(sum(a.amount) FILTER(WHERE day BETWEEN $2 AND $3 AND a.movement='receive'),0)::bigint,
                coalesce(sum(a.amount) FILTER(WHERE day BETWEEN $2 AND $3 AND a.movement='purchase'),0)::bigint
            FROM staff u LEFT JOIN activity a ON u.id=a.person
            WHERE ($1=0 OR $1=u.id) AND u.role='agent' GROUP BY u.id,u.display_name ORDER BY u.display_name
            """, agent, from, to))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) balances.Add(new(r.GetInt64(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4)));
        }
        var entries = new List<PettyEntry>();
        await using (var cmd = Command(conn, null, view + """
            SELECT movement,reference,description,s.display_name,day,amount,invoice
            FROM activity a JOIN staff s ON s.id=a.person
            WHERE ($1=0 OR $1=s.id) AND day BETWEEN $2 AND $3
            ORDER BY day DESC, movement, reference LIMIT 26 OFFSET $4
            """, agent, from, to, (page - 1) * 25))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) entries.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetFieldValue<DateOnly>(4), r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6)));
        }
        return (balances, entries);
    }
}
