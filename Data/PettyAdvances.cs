namespace Amadban.Data;

public sealed record PettyAdvance(long Number,long InvoiceNumber,DateOnly Date,long Amount,string Owner,string Bank,
    string Sheba,string Description,string Actor,string State,bool Received);

public sealed partial class Store
{
    public async Task<long> NextPettyInvoiceAsync()
    {
        await using var conn=await source.OpenConnectionAsync();
        return (long)(await Scalar(conn,null,"SELECT last_value FROM counters WHERE name='petty-invoice'"))!+1;
    }

    public async Task<(long? Number,string Message)> CreatePettyAdvanceAsync(Guid submission,long start,DateOnly day,
        long amount,string owner,string bank,string sheba,string description,long actor)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        await Exec(conn,tx,"SELECT pg_advisory_xact_lock(782542040)");
        var existing=await Scalar(conn,tx,"SELECT number FROM petty_advances WHERE submission_id=$1 AND created_by=$2",submission,actor);
        if(existing is long previous) return (previous,$"پیش‌پرداخت تنخواه {previous} قبلاً ثبت شده است.");
        var last=(long)(await Scalar(conn,tx,"SELECT last_value FROM counters WHERE name='petty-invoice' FOR UPDATE"))!;
        if(last==0)
        {
            if(start<=0) return (null,"شماره شروع فاکتور تنخواه را یک‌بار وارد کنید.");
            if(await Scalar(conn,tx,"SELECT manual_invoice_number FROM petty_advances WHERE manual_invoice_number=$1",start) is not null)
                return (null,"شماره شروع فاکتور قبلاً استفاده شده است.");
            await Exec(conn,tx,"UPDATE counters SET last_value=$1-1 WHERE name='petty-invoice'",start);
        }
        var invoiceNumber=(long)(await Scalar(conn,tx,"UPDATE counters SET last_value=last_value+1 WHERE name='petty-invoice' RETURNING last_value"))!;
        var number=(long)(await Scalar(conn,tx,"UPDATE counters SET last_value=last_value+1 WHERE name='prepayment' RETURNING last_value"))!;
        await Exec(conn,tx,"""
            INSERT INTO petty_advances(number,submission_id,manual_invoice_number,advance_date,amount,owner,bank,sheba,description,created_by)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
            """,number,submission,invoiceNumber,day,amount,owner,bank,sheba,description,actor);
        await Activity(conn,tx,actor,"پیش‌پرداخت تنخواه شماره "+number,
            $"شماره فاکتور {invoiceNumber} · {Persian.Money(amount)} ریال · {owner}","petty");
        await tx.CommitAsync();
        return(number,$"پیش‌پرداخت تنخواه {number} با شماره فاکتور {invoiceNumber} ثبت شد.");
    }

    public async Task<PettyAdvance?> PettyAdvanceAsync(long number)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var cmd=Command(conn,null,"""
            SELECT p.number,p.manual_invoice_number,p.advance_date,p.amount,p.owner,p.bank,p.sheba,p.description,s.display_name,p.state,
              EXISTS(SELECT 1 FROM petty_receipts r WHERE r.advance_number=p.number)
            FROM petty_advances p JOIN staff s ON s.id=p.created_by WHERE p.number=$1
            """,number);
        await using var r=await cmd.ExecuteReaderAsync();
        return await r.ReadAsync()?new(r.GetInt64(0),r.GetInt64(1),r.GetFieldValue<DateOnly>(2),r.GetInt64(3),
            r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetBoolean(10)):null;
    }

    public async Task<List<PettyAdvance>> RecentPettyAdvancesAsync()
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var cmd=Command(conn,null,"""
            SELECT p.number,p.manual_invoice_number,p.advance_date,p.amount,p.owner,p.bank,p.sheba,p.description,s.display_name,p.state,
              EXISTS(SELECT 1 FROM petty_receipts r WHERE r.advance_number=p.number)
            FROM petty_advances p JOIN staff s ON s.id=p.created_by ORDER BY p.number DESC LIMIT 20
            """);
        await using var r=await cmd.ExecuteReaderAsync();
        var rows=new List<PettyAdvance>();
        while(await r.ReadAsync()) rows.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetFieldValue<DateOnly>(2),r.GetInt64(3),
            r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetBoolean(10)));
        return rows;
    }

    public async Task<string> CorrectPettyAdvanceAsync(long number,DateOnly day,long amount,string owner,
        string bank,string sheba,string description,long actor,bool chief)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        var changed=await Exec(conn,tx,"""
            UPDATE petty_advances SET advance_date=$2,amount=$3,owner=$4,bank=$5,sheba=$6,description=$7
            WHERE number=$1 AND state='active' AND ($9 OR created_by=$8)
              AND NOT EXISTS(SELECT 1 FROM petty_receipts r WHERE r.advance_number=$1)
            """,number,day,amount,owner,bank,sheba,description,actor,chief);
        if(changed!=1)return "برگه فعال و بدون دریافت مرتبط برای اصلاح پیدا نشد.";
        await Activity(conn,tx,actor,"اصلاح پیش‌پرداخت تنخواه",$"شماره {number} · {Persian.Money(amount)} ریال", "petty");
        await tx.CommitAsync();
        return "پیش‌پرداخت تنخواه اصلاح شد؛ نسخه تازه را چاپ کنید.";
    }

    public async Task<string> VoidPettyAdvanceAsync(long number,long actor,bool chief)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        var changed=await Exec(conn,tx,"""
            UPDATE petty_advances SET state='void' WHERE number=$1 AND state='active' AND ($3 OR created_by=$2)
              AND NOT EXISTS(SELECT 1 FROM petty_receipts r WHERE r.advance_number=$1)
            """,number,actor,chief);
        if(changed!=1)return "برگه فعال و بدون دریافت مرتبط برای ابطال پیدا نشد.";
        await Activity(conn,tx,actor,"ابطال پیش‌پرداخت تنخواه",$"شماره {number}","petty");
        await tx.CommitAsync();
        return "پیش‌پرداخت تنخواه باطل شد. شماره آن در سابقه می‌ماند.";
    }
}
