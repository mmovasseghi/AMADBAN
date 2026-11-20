using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "agent,chief,warehouse")]
public sealed class PrintFormsModel(Store store) : PageModel
{
    public string Kind { get; private set; } = "";
    public InvoiceDetail? Invoice { get; private set; }
    public List<Prepayment> Payments { get; private set; } = [];
    public string Title => Kind switch { "meeting" => "صورت‌جلسه انجام کار یا مصرف اقلام خریداری شده", "proxy" => "فرم نیابتی", _ => "پیش‌پرداخت تنخواه" };
    public string Receipt { get; private set; } = "";
    public string Recipient { get; private set; } = "";
    public long Amount { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? kind, long id, long[]? ids, string? receipt, string? amount)
    {
        Kind = kind ?? "";
        switch (Kind)
        {
            case "meeting":
                Invoice = await store.DetailAsync(id);
                return Invoice?.Summary.Document is null ? NotFound() : Page();
            case "proxy" when User.IsInRole("agent") || User.IsInRole("chief"):
                if (ids is not { Length: > 0 and <= 25 }) return RedirectToPage("/Bank");
                var available = await store.RecentBankPrepaymentsAsync();
                Payments = available.Where(p => ids.Contains(p.Number)).ToList();
                return Payments.Count == 0 ? NotFound() : Page();
            case "petty" when User.IsInRole("agent"):
                Receipt = Persian.Latin(receipt);
                if (Receipt.Length is < 1 or > 80 || !long.TryParse(Persian.Latin(amount).Replace(",", ""), out var value) ||
                    value is <= 0 or > 1_000_000_000_000L) return RedirectToPage("/Petty");
                Amount = value;
                Recipient = User.FindFirstValue(ClaimTypes.Name) ?? "";
                return Page();
            default:
                return Forbid();
        }
    }
}
