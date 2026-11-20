namespace Amadban.Data;

public sealed record BankCheck(long Id, string Sayad, string Account, string Bank, string Branch,
    DateOnly Date, long Amount, string Recipient);

public sealed partial class Store
{
    public async Task<string> SaveCheckAsync(string sayad, string account, string bank, string branch, DateOnly date,
        long amount, string recipient, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Exec(conn, null, """
            INSERT INTO bank_checks(sayad,account,bank,branch,check_date,amount,recipient,created_by)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8) ON CONFLICT DO NOTHING
            """, sayad, account, bank, branch, date, amount, recipient, actor) == 1
            ? "چک در فهرست آماد ثبت شد." : "شناسه صیاد قبلاً ثبت شده است.";
    }

    public async Task<List<BankCheck>> ChecksAsync(long[]? ids, int page = 1)
    {
        await using var conn = await source.OpenConnectionAsync();
        const string columns = "SELECT id,sayad,account,bank,branch,check_date,amount,recipient FROM bank_checks ";
        await using var cmd = ids is null
            ? Command(conn, null, columns + "ORDER BY check_date DESC,id DESC LIMIT 26 OFFSET $1", (page - 1) * 25)
            : Command(conn, null, columns + "WHERE id=ANY($1) ORDER BY check_date DESC,id DESC", ids);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<BankCheck>();
        while (await r.ReadAsync())
            list.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetFieldValue<DateOnly>(5), r.GetInt64(6), r.GetString(7)));
        return list;
    }
}
