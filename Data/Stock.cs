namespace Amadban.Data;

public sealed record Product(long Id, string Code, string Name, string Unit, decimal Stock, decimal Minimum);
public sealed record StockLine(string Product, string Kind, decimal Delta, string? Department, DateOnly Date, long? Invoice,
    decimal? Before, decimal? Counted, string Actor);
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

    public async Task<string> NewProductAsync(string code, string name, string unit, decimal minimum, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var id = await Scalar(conn, tx,
            "INSERT INTO products(code,name,unit,minimum) VALUES($1,$2,$3,$4) ON CONFLICT DO NOTHING RETURNING id",
            code, name, unit, minimum);
        if (id is not long product) return "کد کالا قبلاً ثبت شده است.";
        await Activity(conn, tx, actor, "کالای انبار تعریف شد", name + " · کد " + code, "stock", product);
        await tx.CommitAsync();
        return "کالا ثبت شد.";
    }

    public async Task<string> CorrectProductAsync(long id,string code,string name,string unit,decimal minimum,long actor)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        if(await Scalar(conn,tx,"SELECT id FROM products WHERE code=$1 AND id<>$2",code,id) is not null)
            return "کد کالا برای کالای دیگری استفاده شده است.";
        if(await Exec(conn,tx,"UPDATE products SET code=$2,name=$3,unit=$4,minimum=$5 WHERE id=$1",id,code,name,unit,minimum)!=1)
            return "کالا پیدا نشد.";
        await Activity(conn,tx,actor,"مشخصات کالا اصلاح شد",$"{name} · کد {code}","stock",id);
        await tx.CommitAsync();
        return "مشخصات کالا اصلاح شد؛ موجودی و سابقه آن حفظ شد.";
    }

    public async Task<string> MoveStockAsync(Guid submission, long id, long invoice, string kind, decimal quantity,
        string department, DateOnly date, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (invoice > 0)
        {
            if (kind != "in" || await Scalar(conn, tx, """
                SELECT id FROM invoices WHERE id=$1 AND warehouse_required AND delivery_state IN ('open','returned')
                FOR UPDATE
                """, invoice) is null) return "پرونده انباری باز و معتبر را انتخاب کنید.";
            if (await Scalar(conn, tx, "SELECT id FROM warehouse_receipts WHERE invoice_id=$1 LIMIT 1", invoice) is not null)
                return "برای این سند از ورود اقلام در صفحه پرونده استفاده کنید؛ گردش دستی با آن ترکیب نمی‌شود.";
        }
        var stock = await Scalar(conn, tx, "SELECT stock FROM products WHERE id=$1 FOR UPDATE", id);
        if (stock is not decimal current) return "کالا پیدا نشد.";
        if (await Scalar(conn, tx, "SELECT id FROM stock_events WHERE submission_id=$1", submission) is not null)
            return "این گردش قبلاً ثبت شده است.";
        if (kind == "opening" && await Scalar(conn, tx, "SELECT id FROM stock_events WHERE product_id=$1 LIMIT 1", id) is not null)
            return "برای کالایی که گردش ثبت‌شده دارد، از ورود یا خروج استفاده کنید.";
        var delta = kind == "out" ? -quantity : quantity;
        if (current + delta < 0) return "موجودی برای این خروج کافی نیست.";
        if (current + delta >= 1_000_000_000) return "مقدار موجودی از دامنه قابل ثبت بیشتر می‌شود.";
        await Exec(conn, tx, "UPDATE products SET stock=stock+$2 WHERE id=$1", id, delta);
        await Exec(conn, tx, """
            INSERT INTO stock_events(submission_id,product_id,invoice_id,kind,delta,department,event_date,actor_id,before_quantity,counted_quantity)
            VALUES($1,$2,NULLIF($3,0),$4,$5,NULLIF($6,''),$7,$8,$9,CASE WHEN $4='opening' THEN $5 ELSE NULL END)
            """, submission, id, invoice, kind, delta, department, date, actor, current);
        if (invoice > 0) await Event(conn, tx, invoice, actor, "ثبت ورود انبار");
        else await Activity(conn,tx,actor,"گردش دستی انبار ثبت شد",$"کالا {id} · {kind} · {delta:0.###}","stock",id);
        await tx.CommitAsync();
        return "گردش کالا ثبت شد.";
    }

    public async Task<string> CountStockAsync(Guid submission, long id, decimal counted, string note, DateOnly date, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var stock = await Scalar(conn, tx, "SELECT stock FROM products WHERE id=$1 FOR UPDATE", id);
        if (stock is not decimal current) return "کالا پیدا نشد.";
        if (await Scalar(conn, tx, "SELECT id FROM stock_events WHERE submission_id=$1", submission) is not null)
            return "این شمارش قبلاً ثبت شده است.";
        if (await Scalar(conn, tx, "SELECT id FROM stock_events WHERE product_id=$1 LIMIT 1", id) is null)
            return "ابتدا موجودی افتتاحیه این کالا را ثبت کنید.";
        var difference = counted - current;
        await Exec(conn, tx, "UPDATE products SET stock=$2 WHERE id=$1", id, counted);
        await Exec(conn, tx, """
            INSERT INTO stock_events(submission_id,product_id,kind,delta,department,event_date,actor_id,before_quantity,counted_quantity)
            VALUES($1,$2,'count',$3,NULLIF($4,''),$5,$6,$7,$8)
            """, submission, id, difference, note, date, actor, current, counted);
        await Activity(conn,tx,actor,"انبارگردانی ثبت شد",$"کالا {id} · شمارش {counted:0.###} · اختلاف {difference:0.###}","stock",id);
        await tx.CommitAsync();
        return difference == 0 ? "شمارش ثبت شد؛ مغایرتی وجود ندارد." :
            $"شمارش ثبت شد؛ مغایرت {difference:0.###} { (difference > 0 ? "افزایش" : "کاهش") } ثبت شد.";
    }

    public async Task<List<StockLine>> StockHistoryAsync(long id, DateOnly from, DateOnly to)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT p.name,s.kind,s.delta,s.department,s.event_date,s.invoice_id,s.before_quantity,s.counted_quantity,u.display_name
            FROM stock_events s JOIN products p ON s.product_id=p.id JOIN staff u ON u.id=s.actor_id
            WHERE s.product_id=$1 AND event_date BETWEEN $2 AND $3 ORDER BY s.id DESC LIMIT 50
            """, id, from, to);
        await using var r = await cmd.ExecuteReaderAsync();
        var result = new List<StockLine>();
        while (await r.ReadAsync()) result.Add(new(r.GetString(0), r.GetString(1), r.GetDecimal(2), r.IsDBNull(3) ? null : r.GetString(3),
            r.GetFieldValue<DateOnly>(4), r.IsDBNull(5) ? null : r.GetInt64(5),
            r.IsDBNull(6) ? null : r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetDecimal(7), r.GetString(8)));
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

    public async Task<string> ReceivePettyAsync(Guid submission, long recipient, string number, DateOnly date, long amount, long actor, long advanceNumber)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (advanceNumber > 0 && await Scalar(conn, tx, """
            SELECT number FROM petty_advances WHERE number=$1 AND state='active' AND amount=$2 AND created_by=$3
            FOR UPDATE
            """, advanceNumber, amount, recipient) is null)
            return "برگه تنخواه فعال با همین مبلغ و افسرعامل پیدا نشد.";
        var changed = await Exec(conn, tx, """
            INSERT INTO petty_receipts(submission_id,recipient_id,receipt_number,receipt_date,amount,actor_id,advance_number)
            SELECT $1,id,$3,$4,$5,$6,NULLIF($7,0) FROM staff WHERE id=$2 AND role='agent' AND active ON CONFLICT DO NOTHING
            """, submission, recipient, number, date, amount, actor, advanceNumber);
        if (changed != 1) return "دریافت ثبت نشده یا برگه تنخواه قبلاً به دریافت دیگری پیوند داده شده است.";
        await Activity(conn, tx, actor, "دریافت تنخواه ثبت شد", $"شماره رسید {number} · {Persian.Money(amount)} ریال" +
            (advanceNumber > 0 ? $" · پیش‌پرداخت {advanceNumber}" : ""), "petty");
        await tx.CommitAsync();
        return "دریافت تنخواه ثبت شد.";
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
