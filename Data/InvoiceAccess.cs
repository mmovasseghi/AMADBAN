namespace Amadban.Data;

public static class InvoiceAccess
{
    public static bool CanUseWorkflow(string role) =>
        role is "chief" or "agent" or "warehouse" or "kardex";

    public static bool CanViewDetail(string role, InvoiceSummary summary) => role switch
    {
        "chief" or "agent" => true,
        "warehouse" => true,
        "kardex" => summary.Warehouse,
        _ => false
    };
}