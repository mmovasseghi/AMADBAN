using System.Globalization;
using Npgsql;

namespace Amadban.Data;

public sealed record DepartmentUsage(string Department, string Product, string Code, string Unit,
    decimal Quantity, long DispatchCount);
public sealed record WarehouseReportOverview(long Dispatches, long Departments, long Recipients);

public sealed partial class Store
{
    private const string DispatchFilter = """
        WHERE i.state='active' AND i.issue_date BETWEEN $1 AND $2 AND ($3::bigint=0 OR i.department_id=$3)
          AND ($4='' OR EXISTS(SELECT 1 FROM stock_issue_lines found
              JOIN products match_product ON match_product.id=found.product_id
              WHERE found.issue_id=i.id AND strpos(lower(match_product.name || ' ' || match_product.code),lower($4))>0))
        """;

    public async Task<(WarehouseReportOverview Overview, List<DepartmentUsage> Usage, List<DispatchRecord> Issues, bool HasNext)>
        WarehouseReportAsync(DateOnly from, DateOnly to, long department, string search, int page)
    {
        await using var conn = await source.OpenConnectionAsync();
        var args = new object[] { from, to, department, search };
        WarehouseReportOverview overview;
        await using (var cmd = Command(conn, null, """
            SELECT count(*),count(DISTINCT i.department_id),count(DISTINCT i.recipient_id)
            FROM stock_issues i
            """ + " " + DispatchFilter, args))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            overview = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
        }
        var usage = new List<DepartmentUsage>();
        await using (var cmd = Command(conn, null, """
            SELECT d.name,p.name,p.code,p.unit,sum(l.quantity-coalesce(returned.quantity,0)),count(DISTINCT i.id)
            FROM stock_issues i JOIN departments d ON d.id=i.department_id
            JOIN stock_issue_lines l ON l.issue_id=i.id JOIN products p ON p.id=l.product_id
            LEFT JOIN LATERAL (SELECT sum(r.quantity) quantity FROM stock_issue_returns r
                WHERE r.issue_id=l.issue_id AND r.position=l.position) returned ON true
            """ + " " + DispatchFilter + """
              AND ($4='' OR strpos(lower(p.name || ' ' || p.code),lower($4))>0)
              AND l.quantity>coalesce(returned.quantity,0)
            GROUP BY d.name,p.name,p.code,p.unit ORDER BY d.name,p.name,p.code LIMIT 150
            """, args))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) usage.Add(new(reader.GetString(0),reader.GetString(1),
                reader.GetString(2),reader.GetString(3),reader.GetDecimal(4),reader.GetInt64(5)));
        }
        var issues = new List<DispatchRecord>();
        await using (var cmd = Command(conn, null, """
            SELECT i.id,i.number,d.name,p.name,p.position,s.display_name,i.issue_date,i.note
            FROM stock_issues i JOIN departments d ON d.id=i.department_id
            JOIN people p ON p.id=i.recipient_id JOIN staff s ON s.id=i.warehouse_person_id
            """ + " " + DispatchFilter + " ORDER BY i.issue_date DESC,i.id DESC LIMIT 26 OFFSET $5",
            [.. args, (page - 1) * 25]))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) issues.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetFieldValue<DateOnly>(6),
                reader.GetString(7),[]));
        }
        var hasNext = issues.Count > 25;
        return (overview, usage, issues.Take(25).ToList(), hasNext);
    }

    public async Task ExportDispatchesAsync(TextWriter output, DateOnly from, DateOnly to, long department,
        string search, CancellationToken cancellation)
    {
        await using var conn = await source.OpenConnectionAsync(cancellation);
        await using var cmd = Command(conn, null, """
            SELECT i.number,i.issue_date,d.name,recipient.name,warehouse.display_name,
                   p.code,p.name,p.unit,l.quantity-coalesce(returned.quantity,0),i.note
            FROM stock_issues i JOIN departments d ON d.id=i.department_id
            JOIN people recipient ON recipient.id=i.recipient_id
            JOIN staff warehouse ON warehouse.id=i.warehouse_person_id
            JOIN stock_issue_lines l ON l.issue_id=i.id JOIN products p ON p.id=l.product_id
            LEFT JOIN LATERAL (SELECT sum(r.quantity) quantity FROM stock_issue_returns r
                WHERE r.issue_id=l.issue_id AND r.position=l.position) returned ON true
            """ + " " + DispatchFilter + """
              AND ($4='' OR strpos(lower(p.name || ' ' || p.code),lower($4))>0)
              AND l.quantity>coalesce(returned.quantity,0)
            ORDER BY i.issue_date DESC,i.id DESC,l.position
            """, from, to, department, search);
        await using var reader = await cmd.ExecuteReaderAsync(cancellation);
        await output.WriteLineAsync("شماره حواله,تاریخ,بخش,تحویل‌گیرنده,انباردار,کد کالا,نام کالا,واحد,خالص تعداد نزد بخش,یادداشت".AsMemory(), cancellation);
        while (await reader.ReadAsync(cancellation))
        {
            var values = new[] { reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                Persian.Date(reader.GetFieldValue<DateOnly>(1)), reader.GetString(2),reader.GetString(3),
                reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),
                reader.GetDecimal(8).ToString(CultureInfo.InvariantCulture),reader.GetString(9) };
            await output.WriteLineAsync(string.Join(',',values.Select(CsvCell)).AsMemory(),cancellation);
        }
    }
}
