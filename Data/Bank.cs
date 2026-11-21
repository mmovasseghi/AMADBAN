namespace Amadban.Data;

public sealed record BankCheck(long Id, string Sayad, string Account, string Bank, string Branch,
    DateOnly Date, long Amount, string Recipient, string RecipientIdentity, long? Prepayment);

public sealed partial class Store
{
    public async Task<string> SaveCheckAsync(string sayad, string account, string bank, string branch, DateOnly date,
        long amount, string recipient, string recipientIdentity, long actor, long prepayment)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if(prepayment>0 && await Scalar(conn,tx,"SELECT number FROM prepayments WHERE number=$1 AND state IN ('reserved','paid')",prepayment) is null)
            return "پیش‌پرداخت مرتبط پیدا نشد.";
        var changed=await Exec(conn, tx, """
            INSERT INTO bank_checks(sayad,account,bank,branch,check_date,amount,recipient,recipient_identity,created_by,prepayment_number)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,NULLIF($10,0)) ON CONFLICT DO NOTHING
            """, sayad, account, bank, branch, date, amount, recipient, recipientIdentity, actor,prepayment);
        if(changed!=1)return "شناسه صیاد قبلاً ثبت شده است.";
        await Activity(conn,tx,actor,"چک ثبت شد",$"صیاد {sayad} · {Persian.Money(amount)} ریال"+
            (prepayment>0?$" · پیش‌پرداخت {prepayment}":""),"bank");
        await tx.CommitAsync();
        return "چک در فهرست آماد ثبت شد.";
    }

    public async Task<List<BankCheck>> ChecksAsync(long[]? ids, int page = 1)
    {
        await using var conn = await source.OpenConnectionAsync();
        const string columns = "SELECT b.id,b.sayad,b.account,b.bank,b.branch,b.check_date,b.amount,b.recipient,b.recipient_identity,b.prepayment_number FROM bank_checks b ";
        await using var cmd = ids is null
            ? Command(conn, null, columns + " ORDER BY b.check_date DESC,b.id DESC LIMIT 26 OFFSET $1", (page - 1) * 25)
            : Command(conn, null, columns + " WHERE b.id=ANY($1) ORDER BY b.check_date DESC,b.id DESC", ids);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<BankCheck>();
        while (await r.ReadAsync())
            list.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetFieldValue<DateOnly>(5), r.GetInt64(6), r.GetString(7),r.GetString(8),r.IsDBNull(9)?null:r.GetInt64(9)));
        return list;
    }

    public async Task<string> CorrectCheckAsync(long id,string sayad,string account,string bank,string branch,
        DateOnly date,long amount,string recipient,string recipientIdentity,long prepayment,long actor)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        if(prepayment>0 && await Scalar(conn,tx,"SELECT number FROM prepayments WHERE number=$1 AND state IN ('reserved','paid')",prepayment) is null)
            return "پیش‌پرداخت مرتبط پیدا نشد.";
        if(await Scalar(conn,tx,"SELECT id FROM bank_checks WHERE sayad=$1 AND id<>$2",sayad,id) is not null)
            return "شناسه صیاد برای چک دیگری ثبت شده است.";
        if(await Exec(conn,tx,"""
            UPDATE bank_checks SET sayad=$2,account=$3,bank=$4,branch=$5,check_date=$6,amount=$7,
                recipient=$8,recipient_identity=$9,prepayment_number=NULLIF($10,0) WHERE id=$1
            """,id,sayad,account,bank,branch,date,amount,recipient,recipientIdentity,prepayment)!=1)
            return "چک پیدا نشد.";
        await Activity(conn,tx,actor,"مشخصات چک اصلاح شد",$"صیاد {sayad} · {Persian.Money(amount)} ریال","bank");
        await tx.CommitAsync();
        return "مشخصات چک اصلاح شد؛ نسخه تازه فرم را چاپ کنید.";
    }
}
