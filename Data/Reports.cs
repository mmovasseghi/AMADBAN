using System.Globalization;

namespace Amadban.Data;

public sealed record ReportGroup(string Label, long Count, long Total, long Paid, long Returned);
public sealed record ReportOverview(long Count, long Total, long Paid, long Returned);
public sealed record OperationalAlerts(long LateKardex, long CurrentKardex, long DuePayments, long Returned, long Unnumbered);

public sealed partial class Store
{
    public async Task<OperationalAlerts> OperationalAlertsAsync()
    {
        var today = Persian.Today;
        var firstOfMonth = Persian.ParseDate(Persian.Date(today)[..7] + "/01")!.Value;
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT count(*) FILTER(WHERE warehouse_required AND kardex_number IS NULL AND invoice_date < $1),
                   count(*) FILTER(WHERE warehouse_required AND kardex_number IS NULL AND invoice_date >= $1),
                   count(*) FILTER(WHERE kind='credit' AND paid_at IS NULL AND invoice_date+45 <= $2),
                   count(*) FILTER(WHERE delivery_state='returned'),
                   count(*) FILTER(WHERE document_number IS NULL)
            FROM invoices
            """, firstOfMonth, today.AddDays(7));
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4));
    }

    public async Task<(ReportOverview Overview, List<ReportGroup> Groups)> GroupedReportAsync(
        string group, string status, string search, string subject, string kind, string credit, DateOnly from, DateOnly to)
    {
        var expression = group switch
        {
            "subject" => "i.subject",
            "credit" => "coalesce((SELECT c.code FROM prepayments p JOIN credit_codes c ON c.id=p.code_id WHERE p.invoice_id=i.id AND p.state<>'void' LIMIT 1),(SELECT c.code FROM credit_codes c WHERE c.id=i.cash_credit_code),'بدون کد')",
            "kind" => "CASE WHEN i.kind='cash' THEN 'نقدی' ELSE 'اعتباری' END",
            _ => "i.vendor"
        };
        var filter = InvoiceCriteria + InvoiceStatusFilter(status);
        var args = StatusDateArgument(status) is DateOnly cutoff
            ? new object[] { from, to, search, subject, kind, credit, cutoff }
            : [from, to, search, subject, kind, credit];
        await using var conn = await source.OpenConnectionAsync();
        ReportOverview overview;
        await using (var cmd = Command(conn, null, """
            SELECT count(*),coalesce(sum(i.total),0)::bigint,
                coalesce(sum(i.total) FILTER(WHERE i.paid_at IS NOT NULL),0)::bigint,
                count(*) FILTER(WHERE i.delivery_state='returned')
            """ + " " + filter, args))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            overview = new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        }
        var groups = new List<ReportGroup>();
        await using (var cmd = Command(conn, null, $"""
            SELECT {expression},count(*),coalesce(sum(i.total),0)::bigint,
                coalesce(sum(i.total) FILTER(WHERE i.paid_at IS NOT NULL),0)::bigint,
                count(*) FILTER(WHERE i.delivery_state='returned')
            """ + " " + filter + " GROUP BY 1 ORDER BY 3 DESC,1 LIMIT 30", args))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) groups.Add(new(reader.GetString(0), reader.GetInt64(1),
                reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)));
        }
        return (overview, groups);
    }

    public async Task ExportInvoicesAsync(TextWriter output, string status, string search, string subject,
        string kind, string credit, DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        await using var conn = await source.OpenConnectionAsync(cancellation);
        var args = StatusDateArgument(status) is DateOnly cutoff
            ? new object[] { from, to, search, subject, kind, credit, cutoff }
            : [from, to, search, subject, kind, credit];
        await using var cmd = Command(conn, null, """
            SELECT i.id,i.document_number,i.number,i.vendor,i.subject,i.invoice_date,i.kind,i.total,
                coalesce((SELECT c.code FROM prepayments p JOIN credit_codes c ON c.id=p.code_id
                          WHERE p.invoice_id=i.id AND p.state<>'void' LIMIT 1),
                         (SELECT c.code FROM credit_codes c WHERE c.id=i.cash_credit_code),''),
                i.paid_at IS NOT NULL,i.delivery_state,i.warehouse_required,i.kardex_number,i.kardex_date
            """ + " " + InvoiceCriteria + InvoiceStatusFilter(status) + " ORDER BY i.invoice_date DESC,i.id DESC", args);
        await using var reader = await cmd.ExecuteReaderAsync(cancellation);
        await output.WriteLineAsync("شناسه پرونده,شماره سند,شماره فاکتور,فروشنده,موضوع,تاریخ فاکتور,نوع خرید,مبلغ ریال,کد اعتباری,مهلت پرداخت,پرداخت شده,وضعیت تحویل,انبار,مهلت کارتکس,شماره کارتکس,تاریخ کارتکس".AsMemory(), cancellation);
        while (await reader.ReadAsync(cancellation))
        {
            var invoiceDate = reader.GetFieldValue<DateOnly>(5);
            var warehouse = reader.GetBoolean(11);
            var cells = new[]
            {
                reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? "" : reader.GetInt64(1).ToString(CultureInfo.InvariantCulture),
                reader.GetString(2), reader.GetString(3), reader.GetString(4), Persian.Date(invoiceDate),
                reader.GetString(6) == "cash" ? "نقدی" : "اعتباری",
                reader.GetInt64(7).ToString(CultureInfo.InvariantCulture), reader.GetString(8),
                reader.GetString(6) == "credit" ? Persian.Date(invoiceDate.AddDays(45)) : "",
                reader.GetBoolean(9) ? "بله" : "خیر",
                reader.GetString(10) switch { "closed" => "بسته", "returned" => "برگشتی", "delivered" => "تحویل مالی", _ => "باز" },
                warehouse ? "بله" : "خیر", warehouse ? Persian.Date(Persian.MonthEnd(invoiceDate)) : "",
                reader.IsDBNull(12) ? "" : reader.GetString(12),
                reader.IsDBNull(13) ? "" : Persian.Date(reader.GetFieldValue<DateOnly>(13))
            };
            await output.WriteLineAsync(string.Join(',', cells.Select(CsvCell)).AsMemory(), cancellation);
        }
    }

    private static string CsvCell(string value)
    {
        value = value.Replace("\0", "").Replace("\r", " ").Replace("\n", " ");
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t') value = "'" + value;
        return '"' + value.Replace("\"", "\"\"") + '"';
    }
}
