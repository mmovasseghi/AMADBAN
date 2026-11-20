using System.Globalization;
using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "warehouse,chief")]
public sealed class StockModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public long ProductId { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string Search { get; set; } = "";
    [BindProperty(SupportsGet = true)] public bool Low { get; set; }
    [BindProperty(SupportsGet = true)] public string From { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string To { get; set; } = "";
    public List<Product> Products { get; private set; } = [];
    public Product? Selected { get; private set; }
    public List<StockLine> History { get; private set; } = [];
    public Guid Submission { get; } = Guid.NewGuid();
    [TempData] public string? Message { get; set; }
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Search = Persian.Latin(Search);
        if (Search.Length > 100) Search = Search[..100];
        P = Math.Clamp(P, 1, 4000);
        Products = await store.ProductsAsync(Search, Low, P);
        if (ProductId == 0) return;
        Selected = await store.GetProductAsync(ProductId);
        if (Selected is null) return;
        var from = Persian.ParseDate(From);
        var to = Persian.ParseDate(To);
        if ((!string.IsNullOrEmpty(From) && from is null) || (!string.IsNullOrEmpty(To) && to is null) ||
            (from is not null && to is not null && from > to))
        {
            ModelState.AddModelError("", "بازه تاریخ شمسی را بررسی کنید.");
            return;
        }
        History = await store.StockHistoryAsync(ProductId, from ?? new DateOnly(1900, 1, 1), to ?? new DateOnly(2200, 1, 1));
    }

    public async Task<IActionResult> OnPostProductAsync(string? code, string? name, string? unit, string? minimum)
    {
        code = Persian.Latin(code);
        name = name?.Trim() ?? "";
        unit = unit?.Trim() ?? "";
        Message = code.Length is < 1 or > 40 || name.Length is < 1 or > 150 || unit.Length is < 1 or > 30 || Quantity(minimum) is not decimal min
            ? "کد، نام، واحد و حداقل موجودی را بررسی کنید."
            : await store.NewProductAsync(code, name, unit, min);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMoveAsync(long productId, Guid submission, long invoice, string? kind,
        string? quantity, string? department, string? date)
    {
        department = department?.Trim() ?? "";
        Message = submission == Guid.Empty || productId < 1 || invoice < 0 || kind is not ("opening" or "in" or "out") ||
            Quantity(quantity) is not decimal qty || qty <= 0 || Persian.ParseDate(date) is not DateOnly day ||
            department.Length > 120 || (kind == "out" && department.Length == 0)
            ? "نوع گردش، مقدار، تاریخ و بخش دریافت‌کننده را بررسی کنید."
            : await store.MoveStockAsync(submission, productId, invoice, kind, qty, department, day, Actor);
        return RedirectToPage(new { productId });
    }

    private static decimal? Quantity(string? value) => decimal.TryParse(Persian.Latin(value).Replace('٫', '.'),
        NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result) && result is >= 0 and <= 1000000 &&
        Math.Round(result, 3) == result ? result : null;
}
