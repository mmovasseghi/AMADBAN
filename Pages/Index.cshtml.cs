using Amadban.Data;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

public sealed class IndexModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Search { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Subject { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Credit { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string From { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string To { get; set; } = "";
    [BindProperty(SupportsGet = true)] public bool Report { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    public List<InvoiceSummary> Rows { get; private set; } = [];
    public bool HasNext { get; private set; }
    public long Count { get; private set; }
    public long Total { get; private set; }
    public OperationalAlerts? Alerts { get; private set; }
    public List<ActivityItem> Activity { get; private set; } = [];
    public SetupHints? Setup { get; private set; }

    public async Task OnGetAsync()
    {
        P = Math.Clamp(P, 1, 4000);
        Search = Persian.Latin(Search);
        if (Search.Length > 100) Search = Search[..100];
        Subject = (Subject ?? "").Trim();
        if (Subject.Length > 100) Subject = Subject[..100];
        Credit = Persian.Latin(Credit);
        if (Credit.Length > 40) Credit = Credit[..40];
        if (Kind is not ("cash" or "credit")) Kind = "";
        From ??= "";
        To ??= "";
        Status ??= "";
        var from = Persian.ParseDate(From);
        var to = Persian.ParseDate(To);
        if ((From.Length > 0 && from is null) || (To.Length > 0 && to is null) ||
            (from != null && to != null && from > to))
        {
            ModelState.AddModelError("", "بازه تاریخ را به‌صورت شمسی و با ترتیب درست وارد کنید.");
            return;
        }
        var roles = User.IsInRole("warehouse") ? "document" : User.IsInRole("kardex") ? "kardex" : "";
        if (!Request.Query.ContainsKey(nameof(Status)) && !Report) Status = roles;
        if (!Report && (User.IsInRole("chief") || User.IsInRole("agent")))
            Setup = await store.SetupHintsAsync();
        var results = await store.ListAsync(Status, Search, Subject, Kind, Credit, from ?? new DateOnly(1900, 1, 1),
            to ?? new DateOnly(2200, 12, 31), P, Report);
        HasNext = results.Rows.Count > 25;
        Rows = results.Rows.Take(25).ToList();
        Count = results.Count;
        Total = results.Total;
        if (!Report && P == 1)
        {
            Alerts = await store.OperationalAlertsAsync();
            if (User.IsInRole("chief")) Activity = await store.ActivityAsync(1,4);
        }
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        Search = Persian.Latin(Search);
        if (Search.Length > 100) Search = Search[..100];
        Subject = (Subject ?? "").Trim();
        if (Subject.Length > 100) Subject = Subject[..100];
        Credit = Persian.Latin(Credit);
        if (Credit.Length > 40) Credit = Credit[..40];
        if (Kind is not ("cash" or "credit")) Kind = "";
        var from = Persian.ParseDate(From);
        var to = Persian.ParseDate(To);
        if ((!string.IsNullOrEmpty(From) && from is null) || (!string.IsNullOrEmpty(To) && to is null) ||
            (from is not null && to is not null && from > to)) return BadRequest("بازه تاریخ شمسی را بررسی کنید.");
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=amadban-invoices-{DateTime.UtcNow:yyyyMMdd}.csv";
        Response.Headers.CacheControl = "private, no-store";
        await using var writer = new StreamWriter(Response.Body, new UTF8Encoding(true), leaveOpen: true);
        await store.ExportInvoicesAsync(writer, Status ?? "", Search, Subject, Kind, Credit,
            from ?? new DateOnly(1900, 1, 1), to ?? new DateOnly(2200, 12, 31), HttpContext.RequestAborted);
        await writer.FlushAsync(HttpContext.RequestAborted);
        return new EmptyResult();
    }

    public static string Badge(InvoiceSummary item) => item.Delivery switch
    {
        "closed" => "بسته‌شده", "returned" => item.ReviewStage == "deputy" ? "برگشتی از معاونت" : "برگشتی از مالی",
        "delivered" => item.ReviewStage == "deputy" ? "در معاونت" : "تحویل مالی",
        _ when item.Paid => "پرداخت‌شده و باز",
        _ when item.Document is null => "در انتظار صدور سند",
        _ => "در انتظار پرداخت"
    };
}
