using Amadban.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

public sealed class IndexModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Search { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string From { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string To { get; set; } = "";
    [BindProperty(SupportsGet = true)] public bool Report { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    public List<InvoiceSummary> Rows { get; private set; } = [];
    public bool HasNext { get; private set; }
    public long Count { get; private set; }
    public long Total { get; private set; }

    public async Task OnGetAsync()
    {
        P = Math.Clamp(P, 1, 4000);
        Search = Persian.Latin(Search);
        if (Search.Length > 100) Search = Search[..100];
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
        var results = await store.ListAsync(Status, Search, from ?? new DateOnly(1900, 1, 1),
            to ?? new DateOnly(2200, 12, 31), P, Report);
        HasNext = results.Rows.Count > 25;
        Rows = results.Rows.Take(25).ToList();
        Count = results.Count;
        Total = results.Total;
    }

    public static string Badge(InvoiceSummary item) => item.Delivery switch
    {
        "closed" => "بسته‌شده", "returned" => "برگشتی از مالی", "delivered" => "تحویل‌شده",
        _ when item.Paid => "پرداخت‌شده و باز",
        _ when item.Document is null => "در انتظار صدور سند",
        _ => "در انتظار پرداخت"
    };
}
