namespace Amadban.Data;

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
            " WHERE p.state IN ('reserved','paid') AND p.invoice_id IS NOT NULL ORDER BY p.number DESC LIMIT 50");
        await using var r = await cmd.ExecuteReaderAsync();
        var results = new List<Prepayment>();
        while (await r.ReadAsync()) results.Add(ReadPrepayment(r));
        return results;
    }
}
