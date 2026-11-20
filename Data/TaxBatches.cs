namespace Amadban.Data;

public sealed record TaxBatchSummary(long Id,DateOnly Date,string Reference,string Actor,int Documents);

public sealed partial class Store
{
    public async Task<List<InvoiceSummary>> TaxPendingAsync(DateOnly from,DateOnly to)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var cmd=Command(conn,null,"SELECT "+SummaryColumns+" "+"""
            FROM invoices i WHERE i.document_number IS NOT NULL AND i.delivery_state IN ('open','returned')
              AND i.invoice_date BETWEEN $1 AND $2
              AND NOT EXISTS(SELECT 1 FROM invoice_attachments a WHERE a.invoice_id=i.id AND a.kind='tax')
              AND NOT EXISTS(SELECT 1 FROM tax_batch_invoices t WHERE t.invoice_id=i.id)
            ORDER BY i.invoice_date DESC,i.id DESC LIMIT 50
            """,from,to);
        await using var reader=await cmd.ExecuteReaderAsync();
        var rows=new List<InvoiceSummary>();
        while(await reader.ReadAsync())rows.Add(ReadSummary(reader));
        return rows;
    }

    public async Task<List<TaxBatchSummary>> RecentTaxBatchesAsync()
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var cmd=Command(conn,null,"""
            SELECT b.id,b.batch_date,b.reference,s.display_name,count(t.invoice_id)::integer
            FROM tax_batches b JOIN staff s ON s.id=b.actor_id
            JOIN tax_batch_invoices t ON t.batch_id=b.id
            GROUP BY b.id,b.batch_date,b.reference,s.display_name
            ORDER BY b.id DESC LIMIT 30
            """);
        await using var reader=await cmd.ExecuteReaderAsync();
        var result=new List<TaxBatchSummary>();
        while(await reader.ReadAsync())result.Add(new(reader.GetInt64(0),reader.GetFieldValue<DateOnly>(1),
            reader.GetString(2),reader.GetString(3),reader.GetInt32(4)));
        return result;
    }

    public async Task<(long? Id,string Message)> CreateTaxBatchAsync(Guid submission,DateOnly date,string reference,
        IReadOnlyList<long> selected,DocumentUpload file,long actor)
    {
        var ids=selected.Distinct().ToArray();
        if(ids.Length is <1 or >50 || ids.Any(id=>id<=0))return(null,"یک تا پنجاه سند را انتخاب کنید.");
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        await Exec(conn,tx,"SELECT pg_advisory_xact_lock(782542055)");
        if(await Scalar(conn,tx,"SELECT id FROM tax_batches WHERE submission_id=$1",submission) is long previous)
            return(previous,"این برگه مالیات قبلاً ثبت شده است.");
        var valid=new List<long>();
        await using(var cmd=Command(conn,tx,"""
            SELECT i.id FROM invoices i WHERE i.id=ANY($1) AND i.document_number IS NOT NULL
              AND i.delivery_state IN ('open','returned')
              AND NOT EXISTS(SELECT 1 FROM invoice_attachments a WHERE a.invoice_id=i.id AND a.kind='tax')
              AND NOT EXISTS(SELECT 1 FROM tax_batch_invoices t WHERE t.invoice_id=i.id)
            ORDER BY i.id FOR UPDATE OF i
            """,ids))
        await using(var reader=await cmd.ExecuteReaderAsync())
            while(await reader.ReadAsync())valid.Add(reader.GetInt64(0));
        if(valid.Count!=ids.Length)return(null,"برخی سندها قبلاً مالیات دارند یا برای ثبت باز نیستند. فهرست را تازه کنید.");
        var id=(long)(await Scalar(conn,tx,"""
            INSERT INTO tax_batches(submission_id,batch_date,reference,filename,content_type,content,actor_id)
            VALUES($1,$2,$3,$4,$5,$6,$7) RETURNING id
            """,submission,date,reference,file.Name,file.Type,file.Content,actor))!;
        foreach(var invoice in valid)
        {
            await Exec(conn,tx,"INSERT INTO tax_batch_invoices(invoice_id,batch_id) VALUES($1,$2)",invoice,id);
            await Event(conn,tx,invoice,actor,$"ثبت برگه مالیات گروهی {id}");
        }
        await tx.CommitAsync();
        return(id,$"برگه مالیات {id} برای {valid.Count} سند ثبت شد.");
    }

    public async Task<DocumentUpload?> TaxBatchFileAsync(long id,long invoice)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var cmd=Command(conn,null,"""
            SELECT b.filename,b.content_type,b.content FROM tax_batches b
            JOIN tax_batch_invoices t ON t.batch_id=b.id
            WHERE b.id=$1 AND ($2=0 OR t.invoice_id=$2) LIMIT 1
            """,id,invoice);
        await using var reader=await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync()?new(reader.GetString(0),reader.GetString(1),reader.GetFieldValue<byte[]>(2)):null;
    }

    public async Task<string> UnlinkTaxBatchAsync(long invoice,long actor)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        if(await Scalar(conn,tx,"SELECT id FROM invoices WHERE id=$1 AND delivery_state IN ('open','returned') FOR UPDATE",invoice) is null)
            return "فقط پیوند مالیات سند باز یا برگشتی قابل اصلاح است.";
        if(await Exec(conn,tx,"DELETE FROM tax_batch_invoices WHERE invoice_id=$1",invoice)!=1)
            return "برگه مالیات گروهی برای این سند پیدا نشد.";
        await Event(conn,tx,invoice,actor,"اصلاح ارتباط برگه مالیات گروهی");
        await tx.CommitAsync();
        return "پیوند برگه گروهی از این سند برداشته شد؛ مالیات آن دوباره در فهرست انتظار است.";
    }
}
