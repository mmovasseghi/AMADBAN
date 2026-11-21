namespace Amadban.Data;

public sealed record ReceiptMatch(int Position, string Title, string Unit, decimal Quantity, string? Code, string? Product, bool PrefixSuggested);

public sealed partial class Store
{
    const string ReceiptProductId = """
        coalesce(m.product_id,a.product_id,old_alias.product_id,
            (SELECT exact.id FROM products exact WHERE lower(trim(exact.name))=lower(trim(l.title))
             AND exact.unit=l.unit ORDER BY exact.id LIMIT 1),
            (SELECT prefix.id FROM products prefix
             WHERE prefix.unit=l.unit AND length(trim(prefix.name))>=2
               AND lower(trim(l.title)) LIKE lower(trim(prefix.name)) || '%'
             ORDER BY length(trim(prefix.name)) DESC, prefix.id LIMIT 1))
        """;

    async Task<long?> ResolveReceiptProductIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string title, string unit,
        string vendor, string selectedCode)
    {
        if (selectedCode.Length > 0)
            return await Scalar(conn, tx, "SELECT id FROM products WHERE code=$1 AND unit=$2 FOR UPDATE", selectedCode, unit) as long?;
        return await Scalar(conn, tx, $"""
            SELECT p.id FROM products p
            LEFT JOIN product_aliases a ON a.product_id=p.id AND a.invoice_title=$1 AND a.invoice_unit=$2
                AND a.vendor IN ($3,'')
            WHERE p.unit=$2 AND length(trim(p.name))>=2 AND (
                a.product_id IS NOT NULL
                OR lower(trim(p.name))=lower(trim($1))
                OR lower(trim($1)) LIKE lower(trim(p.name)) || '%')
            ORDER BY (a.vendor=$3) DESC NULLS LAST,(a.product_id IS NOT NULL) DESC,
                CASE WHEN lower(trim(p.name))=lower(trim($1)) THEN 2
                     WHEN lower(trim($1)) LIKE lower(trim(p.name)) || '%' THEN 1 ELSE 0 END DESC,
                length(trim(p.name)) DESC,p.id LIMIT 1 FOR UPDATE OF p
            """, title, unit, vendor) as long?;
    }

    public async Task<List<ReceiptMatch>> ReceiptMatchesAsync(long invoice)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, $"""
            SELECT l.position,l.title,l.unit,l.quantity,p.code,p.name,
                p.id IS NOT NULL AND lower(trim(l.title))<>lower(trim(p.name))
                    AND lower(trim(l.title)) LIKE lower(trim(p.name)) || '%'
            FROM invoice_lines l JOIN invoices i ON i.id=l.invoice_id
            LEFT JOIN invoice_product_matches m
                ON m.invoice_id=l.invoice_id AND m.position=l.position
            LEFT JOIN product_aliases a ON a.invoice_title=l.title AND a.invoice_unit=l.unit AND a.vendor=i.vendor
            LEFT JOIN product_aliases old_alias ON old_alias.invoice_title=l.title
                AND old_alias.invoice_unit=l.unit AND old_alias.vendor=''
            LEFT JOIN products p ON p.id={ReceiptProductId}
            WHERE l.invoice_id=$1 ORDER BY l.position
            """, invoice);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<ReceiptMatch>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetInt32(0),reader.GetString(1),
            reader.GetString(2),reader.GetDecimal(3),reader.IsDBNull(4)?null:reader.GetString(4),
            reader.IsDBNull(5)?null:reader.GetString(5),!reader.IsDBNull(6) && reader.GetBoolean(6)));
        return result;
    }

    public async Task<string> ReceiveInvoiceAsync(long invoice, DateOnly day, IReadOnlyList<string?> codes,
        IReadOnlyList<bool> createNew, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var vendor = await Scalar(conn, tx, """
            SELECT vendor FROM invoices WHERE id=$1 AND warehouse_required AND document_number IS NOT NULL
              AND delivery_state IN ('open','returned') FOR UPDATE
            """, invoice) as string;
        if (vendor is null) return "سند انباری باز و شماره‌دار پیدا نشد.";
        if (await Scalar(conn, tx, "SELECT id FROM warehouse_receipts WHERE invoice_id=$1 AND state='active'", invoice) is not null)
            return "ورود اقلام این سند قبلاً ثبت شده است.";
        if (await Scalar(conn, tx, "SELECT id FROM stock_events WHERE invoice_id=$1 AND receipt_id IS NULL LIMIT 1", invoice) is not null)
            return "برای این سند گردش دستی انبار ثبت شده است. از ثبت تکراری اقلام خودداری کنید.";

        var lines = new List<(int Position, string Title, string Unit, decimal Quantity)>();
        await using (var cmd = Command(conn, tx,
            "SELECT position,title,unit,quantity FROM invoice_lines WHERE invoice_id=$1 ORDER BY position", invoice))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) lines.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3)));
        }
        if (lines.Count == 0) return "اقلام فاکتور پیدا نشدند.";
        await Exec(conn, tx, "SELECT pg_advisory_xact_lock(782541998)");
        var receipt = (long)(await Scalar(conn, tx, """
            INSERT INTO warehouse_receipts(invoice_id,state,receipt_date,actor_id)
            VALUES($1,'active',$2,$3) RETURNING id
            """, invoice, day, actor))!;
        foreach (var line in lines)
        {
            var selectedCode = line.Position-1 < codes.Count ? (codes[line.Position-1] ?? "").Trim() : "";
            var allowNew = line.Position - 1 < createNew.Count && createNew[line.Position - 1];
            long? productId = allowNew ? null : await ResolveReceiptProductIdAsync(conn, tx, line.Title, line.Unit, vendor, selectedCode);
            if (!allowNew && selectedCode.Length > 0 && productId is null)
                return $"کد انبار «{selectedCode}» برای قلم «{line.Title}» با همین واحد پیدا نشد.";
            decimal before;
            if (productId is null)
            {
                if (!allowNew)
                    return $"برای قلم «{line.Title}» کالای انبار مشخص نشد. کد کالای موجود (مثلاً سیب) را وارد کنید، یا در صورت نیاز به تعریف قلم جدید، تیک «کالای جدید» را بزنید.";
                var number = (long)(await Scalar(conn, tx,
                    "UPDATE counters SET last_value=last_value+1 WHERE name='warehouse-product' RETURNING last_value"))!;
                productId = (long)(await Scalar(conn, tx, """
                    INSERT INTO products(code,name,unit,stock) VALUES($1,$2,$3,0) RETURNING id
                    """, $"AM-{number:000000}", line.Title, line.Unit))!;
                before = 0;
            }
            else before = (decimal)(await Scalar(conn, tx, "SELECT stock FROM products WHERE id=$1 FOR UPDATE", productId.Value))!;
            if (before + line.Quantity >= 1_000_000_000) return "موجودی این کالا از دامنه قابل ثبت بیشتر می‌شود.";
            await Exec(conn, tx, "UPDATE products SET stock=stock+$2 WHERE id=$1", productId.Value, line.Quantity);
            await Exec(conn, tx, """
                INSERT INTO invoice_product_matches(invoice_id,position,product_id) VALUES($1,$2,$3)
                ON CONFLICT(invoice_id,position) DO UPDATE SET product_id=excluded.product_id
                """, invoice,line.Position,productId.Value);
            await Exec(conn, tx, """
                INSERT INTO product_aliases(invoice_title,invoice_unit,vendor,product_id,actor_id)
                VALUES($1,$2,$3,$4,$5) ON CONFLICT(invoice_title,invoice_unit,vendor)
                DO UPDATE SET product_id=excluded.product_id,actor_id=excluded.actor_id,updated_at=now()
                """,line.Title,line.Unit,vendor,productId.Value,actor);
            await Exec(conn, tx, """
                INSERT INTO stock_events(submission_id,product_id,invoice_id,kind,delta,department,event_date,actor_id,before_quantity,receipt_id)
                VALUES($1,$2,$3,'in',$4,'ورود اقلام سند',$5,$6,$7,$8)
                """, Guid.NewGuid(), productId.Value, invoice, line.Quantity, day, actor, before, receipt);
        }
        await Event(conn, tx, invoice, actor, "ورود همه اقلام سند به انبار");
        await tx.CommitAsync();
        return $"{lines.Count} قلم سند به موجودی انبار افزوده شد.";
    }

    public async Task<string> VoidInvoiceReceiptAsync(long invoice, DateOnly day, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Scalar(conn, tx, """
            SELECT id FROM invoices WHERE id=$1 AND delivery_state IN ('open','returned') FOR UPDATE
            """, invoice) is null) return "فقط ورود انبار پرونده باز یا برگشتی قابل اصلاح است.";
        var receipt = await Scalar(conn, tx,
            "SELECT id FROM warehouse_receipts WHERE invoice_id=$1 AND state='active' FOR UPDATE", invoice) as long?;
        if (receipt is null) return "ورود انبار فعال برای این سند پیدا نشد.";
        var movements = new List<(long Product, decimal Quantity)>();
        await using (var cmd = Command(conn, tx,
            "SELECT product_id,delta FROM stock_events WHERE receipt_id=$1 AND kind='in' ORDER BY id", receipt.Value))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) movements.Add((reader.GetInt64(0), reader.GetDecimal(1)));
        }
        foreach (var movement in movements)
        {
            var before = (decimal)(await Scalar(conn, tx,
                "SELECT stock FROM products WHERE id=$1 FOR UPDATE", movement.Product))!;
            if (before < movement.Quantity)
                return "موجودی بعضی اقلام پس از ورود مصرف شده است. ابتدا آن اقلام را به انبار برگردانید.";
            await Exec(conn, tx, "UPDATE products SET stock=stock-$2 WHERE id=$1", movement.Product, movement.Quantity);
            await Exec(conn, tx, """
                INSERT INTO stock_events(submission_id,product_id,invoice_id,kind,delta,department,event_date,actor_id,before_quantity,receipt_id)
                VALUES($1,$2,$3,'out',$4,'اصلاح ورود اقلام سند',$5,$6,$7,$8)
                """, Guid.NewGuid(), movement.Product, invoice, -movement.Quantity, day, actor, before, receipt.Value);
        }
        await Exec(conn, tx,
            "UPDATE warehouse_receipts SET state='void',void_at=now(),void_by=$2 WHERE id=$1", receipt.Value, actor);
        await Event(conn, tx, invoice, actor, "برگشت ورود اقلام سند از موجودی");
        await tx.CommitAsync();
        return "ورود انبار با گردش برگشتی اصلاح شد. اکنون می‌توانید اقلام سند را ویرایش و دوباره وارد کنید.";
    }
}
