namespace Amadban.Data;

public sealed record ProxyPrintRow(long Number,string Account,string Bank,long Amount,string Owner,string Purpose);

public sealed partial class Store
{
    public async Task<string?> PrintPreferenceAsync(long staff, string form)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null, "SELECT template FROM print_settings WHERE staff_id=$1 AND form=$2", staff, form) as string;
    }

    public async Task SavePrintPreferenceAsync(long staff, string form, string template)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Exec(conn, tx, """
            INSERT INTO print_settings(staff_id,form,template) VALUES($1,$2,$3)
            ON CONFLICT(staff_id,form) DO UPDATE SET template=excluded.template
            """, staff, form, template);
        await Activity(conn, tx, staff, "قالب پیش‌فرض چاپ تغییر کرد", form + " · " + template, "organization");
        await tx.CommitAsync();
    }

    public async Task<List<Prepayment>> RecentBankPrepaymentsAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, PrepaymentSelect +
            " WHERE p.state IN ('reserved','paid') ORDER BY p.number DESC LIMIT 50");
        await using var r = await cmd.ExecuteReaderAsync();
        var results = new List<Prepayment>();
        while (await r.ReadAsync()) results.Add(ReadPrepayment(r));
        return results;
    }

    public async Task<List<ProxyPrintRow>> ProxyPrintRowsAsync(long[] numbers)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT p.number,p.sheba,p.bank,p.amount,p.owner,
              coalesce((SELECT CASE WHEN count(*)=1 THEN min(l.title)
                ELSE (array_agg(l.title ORDER BY l.position))[1] || ' - ' || (array_agg(l.title ORDER BY l.position DESC))[1] END
                FROM invoice_lines l WHERE l.invoice_id=p.invoice_id),p.proforma_number,p.vendor)
            FROM prepayments p WHERE p.number=ANY($1) AND p.state IN ('reserved','paid')
            ORDER BY p.number
            """, numbers);
        await using var r=await cmd.ExecuteReaderAsync();
        var rows=new List<ProxyPrintRow>();
        while(await r.ReadAsync()) rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetString(4),r.GetString(5)));
        return rows;
    }
}
