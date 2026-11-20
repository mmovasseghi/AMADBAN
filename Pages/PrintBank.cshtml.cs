using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "agent,chief")]
public sealed class PrintBankModel(Store store) : PageModel
{
    public bool Slip { get; private set; }
    public List<BankCheck> Checks { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(long[] ids, string? kind)
    {
        Slip = kind == "slip";
        if (ids.Length == 0 || ids.Length > 25 || (Slip && ids.Length != 1))
        {
            Message = "برای چاپ فرم، چک‌های موردنظر را انتخاب کنید.";
            return RedirectToPage("/Bank");
        }
        Checks = await store.ChecksAsync(ids);
        return Checks.Count == 0 ? NotFound() : Page();
    }
}
