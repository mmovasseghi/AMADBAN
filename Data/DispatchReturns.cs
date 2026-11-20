namespace Amadban.Data;

public sealed partial class Store
{
    public async Task<string> ReturnDispatchAsync(long number, int position, decimal quantity,
        Guid submission, DateOnly date, long actor)
    {
        if (submission == Guid.Empty || number <= 0 || position < 0 || quantity < 0)
            return "شماره برگه و مقدار برگشتی را بررسی کنید.";
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Scalar(conn, tx, "SELECT id FROM stock_issue_returns WHERE submission_id=$1", submission) is not null)
            return "این برگشت قبلاً ثبت شده است.";
        var issue = await Scalar(conn, tx,
            "SELECT id FROM stock_issues WHERE number=$1 AND state='active' FOR UPDATE", number) as long?;
        if (issue is null) return "برگه فعال برای برگشت پیدا نشد.";
        var lines = new List<(int Position,long Product,decimal Remaining,string Name)>();
        await using (var cmd = Command(conn, tx, """
            SELECT l.position,l.product_id,l.quantity-coalesce(sum(r.quantity),0),p.name
            FROM stock_issue_lines l JOIN products p ON p.id=l.product_id
            LEFT JOIN stock_issue_returns r ON r.issue_id=l.issue_id AND r.position=l.position
            WHERE l.issue_id=$1 GROUP BY l.position,l.product_id,l.quantity,p.name ORDER BY l.position
            """, issue.Value))
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) lines.Add((reader.GetInt32(0),reader.GetInt64(1),
                reader.GetDecimal(2),reader.GetString(3)));
        var selected = position == 0 ? lines.Where(x => x.Remaining > 0).ToList()
            : lines.Where(x => x.Position == position).ToList();
        if (selected.Count == 0 || (position > 0 && (quantity <= 0 || quantity > selected[0].Remaining)))
            return "مقدار برگشتی از مانده همین برگه بیشتر است یا قلم پیدا نشد.";
        foreach (var line in selected)
        {
            var returned = position == 0 ? line.Remaining : quantity;
            var before = (decimal)(await Scalar(conn, tx,
                "SELECT stock FROM products WHERE id=$1 FOR UPDATE", line.Product))!;
            if (before + returned >= 1_000_000_000)
                return "موجودی کالا از دامنه قابل ثبت بیشتر می‌شود.";
            await Exec(conn, tx, """
                INSERT INTO stock_issue_returns(submission_id,issue_id,position,quantity,return_date,actor_id)
                VALUES($1,$2,$3,$4,$5,$6)
                """, line.Position == selected[0].Position ? submission : Guid.NewGuid(), issue.Value,
                line.Position, returned, date, actor);
            await Exec(conn, tx, "UPDATE products SET stock=stock+$2 WHERE id=$1", line.Product, returned);
            await Exec(conn, tx, """
                INSERT INTO stock_events(submission_id,product_id,kind,delta,department,event_date,actor_id,before_quantity,issue_id)
                VALUES($1,$2,'in',$3,'برگشت از بخش',$4,$5,$6,$7)
                """, Guid.NewGuid(), line.Product, returned, date, actor, before, issue.Value);
        }
        if (position == 0 || lines.All(line => line.Remaining -
            (line.Position == position ? quantity : 0) == 0))
            await Exec(conn, tx, "UPDATE stock_issues SET state='void' WHERE id=$1", issue.Value);
        var title = position == 0 ? "ابطال حواله و برگشت همه اقلام" : "برگشت کالا از بخش";
        await Activity(conn, tx, actor, title,
            $"برگه {number} · " + (position == 0 ? "همه مانده اقلام" : $"{selected[0].Name} · {quantity:0.###}"),
            "dispatch", number);
        await tx.CommitAsync();
        return position == 0 ? "برگه با گردش برگشتی باطل شد. می‌توانید برگه درست را دوباره ثبت کنید."
            : "برگشت کالا ثبت شد و موجودی انبار افزایش یافت.";
    }
}
