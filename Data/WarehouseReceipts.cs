namespace Amadban.Data;

public sealed partial class Store
{
    public async Task<string> ReceiveInvoiceAsync(long invoice, DateOnly day, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Scalar(conn, tx, """
            SELECT id FROM invoices WHERE id=$1 AND warehouse_required AND document_number IS NOT NULL
              AND delivery_state IN ('open','returned') FOR UPDATE
            """, invoice) is null) return "سند انباری باز و شماره‌دار پیدا نشد.";
        if (await Scalar(conn, tx, "SELECT id FROM warehouse_receipts WHERE invoice_id=$1 AND state='active'", invoice) is not null)
            return "ورود اقلام این سند قبلاً ثبت شده است.";
        if (await Scalar(conn, tx, "SELECT id FROM stock_events WHERE invoice_id=$1 AND receipt_id IS NULL LIMIT 1", invoice) is not null)
            return "برای این سند گردش دستی انبار ثبت شده است. از ثبت تکراری اقلام خودداری کنید.";

        var lines = new List<(string Title, string Unit, decimal Quantity)>();
        await using (var cmd = Command(conn, tx,
            "SELECT title,unit,quantity FROM invoice_lines WHERE invoice_id=$1 ORDER BY position", invoice))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) lines.Add((reader.GetString(0), reader.GetString(1), reader.GetDecimal(2)));
        }
        if (lines.Count == 0) return "اقلام فاکتور پیدا نشدند.";
        await Exec(conn, tx, "SELECT pg_advisory_xact_lock(782541998)");
        var receipt = (long)(await Scalar(conn, tx, """
            INSERT INTO warehouse_receipts(invoice_id,state,receipt_date,actor_id)
            VALUES($1,'active',$2,$3) RETURNING id
            """, invoice, day, actor))!;
        foreach (var line in lines)
        {
            var productId = await Scalar(conn, tx, """
                SELECT id FROM products WHERE lower(name)=lower($1) AND unit=$2
                ORDER BY id LIMIT 1 FOR UPDATE
                """, line.Title, line.Unit) as long?;
            decimal before;
            if (productId is null)
            {
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
