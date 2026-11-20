namespace Amadban.Data;

public sealed partial class Store
{
    public async Task<string?> CorrectInvoiceAsync(long id, InvoiceEntry entry, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await LockCredit(conn, tx);
        long previous;
        bool paid, issued;
        string kind;
        await using (var cmd = Command(conn, tx, """
            SELECT total,kind,paid_at IS NOT NULL,document_number IS NOT NULL FROM invoices
            WHERE id=$1 AND delivery_state IN ('open','returned') FOR UPDATE
            """, id))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            if (!await r.ReadAsync()) return "فقط پرونده باز یا برگشتی قابل اصلاح است.";
            previous = r.GetInt64(0);
            kind = r.GetString(1);
            paid = r.GetBoolean(2);
            issued = r.GetBoolean(3);
        }

        var total = entry.Lines.Sum(x => x.Amount);
        if (issued && kind != entry.Kind) return "نوع پرداخت سند صادرشده ثابت است.";
        var prepayment = await FindPrepayment(conn, tx, id, byInvoice: true);
        if ((paid || prepayment is not null) && total != previous)
            return "تغییر مبلغ مستلزم تعیین تکلیف پرداخت یا رزرو قبلی است.";
        if ((paid || prepayment is not null) && kind != entry.Kind)
            return "نوع پرداخت پرونده دارای پیش‌پرداخت یا پرداخت ثابت است.";
        if (entry.CashCredit > 0 && await Available(conn, tx, entry.CashCredit) is null)
            return "کد اعتباری انتخاب‌شده پیدا نشد.";
        await Exec(conn, tx, """
            UPDATE invoices SET number=$2,vendor=$3,subject=$4,invoice_date=$5,kind=$6,
                warehouse_required=$7,meeting_required=$8,account_required=$9,tax_required=$10,total=$11,
                cash_credit_code=CASE WHEN $6='cash' THEN NULLIF($12,0) ELSE NULL END
            WHERE id=$1
            """, id, entry.Number, entry.Vendor, entry.Subject, entry.Date, entry.Kind,
            entry.Warehouse, entry.Meeting, entry.Account, entry.Tax, total, entry.CashCredit);
        await Exec(conn, tx, "DELETE FROM invoice_lines WHERE invoice_id=$1", id);
        await AddLines(conn, tx, id, entry.Lines);
        await Event(conn, tx, id, actor, "اصلاح فاکتور و مدارک لازم");
        await tx.CommitAsync();
        return null;
    }

    public async Task<long> CashCodeAsync(long id)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null, "SELECT cash_credit_code FROM invoices WHERE id=$1", id) is long code ? code : 0;
    }
}
