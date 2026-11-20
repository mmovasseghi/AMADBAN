namespace Amadban.Data;

public sealed partial class Store
{
    public async Task<List<InvoiceSummary>> KardexQueueAsync(int page)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var cmd=Command(conn,null,"SELECT "+SummaryColumns+" "+"""
            FROM invoices WHERE warehouse_required AND kardex_number IS NULL AND document_number IS NOT NULL
              AND delivery_state IN ('open','returned')
            ORDER BY invoice_date,id LIMIT 26 OFFSET $1
            """,(page-1)*25);
        await using var reader=await cmd.ExecuteReaderAsync();
        var result=new List<InvoiceSummary>();
        while(await reader.ReadAsync())result.Add(ReadSummary(reader));
        return result;
    }
}
