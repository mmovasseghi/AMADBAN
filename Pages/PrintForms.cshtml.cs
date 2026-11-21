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
    public List<ProxyPrintRow> Payments { get; private set; } = [];
    public string Title => Kind switch { "meeting" => "صورت‌جلسه انجام کار یا مصرف اقلام خریداری شده", _ => "فرم نیابتی" };
    public async Task<IActionResult> OnGetAsync(string? kind, long id, long[]? ids)
    {
        Kind = kind ?? "";
        switch (Kind)
        {
            case "meeting":
                Invoice = await store.DetailAsync(id);
                return Invoice?.Summary.Document is null ? NotFound() : Page();
            case "proxy" when User.IsInRole("agent") || User.IsInRole("chief"):
                if (ids is not { Length: > 0 and <= 16 }) return RedirectToPage("/Bank");
                Payments = await store.ProxyPrintRowsAsync(ids.Distinct().ToArray());
                return Payments.Count == 0 ? NotFound() : Page();
            case "petty": return RedirectToPage("/Petty");
            default:
                return Forbid();
        }
    }
}
