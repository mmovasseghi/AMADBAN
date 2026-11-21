using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "agent,chief")]
public sealed class PrintBankModel(Store store) : PageModel
{
    public string Operation { get; private set; } = "ثبت";
    public List<BankCheck> Checks { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(long[] ids, string? operation)
    {
        Operation = operation is "تأیید" or "انتقال" or "ابطال" or "لغو ثبت" or "رفع انسداد" ? operation : "ثبت";
        if (ids.Length == 0 || ids.Length > 11)
        {
            Message = "برای چاپ فرم، یک تا یازده چک را انتخاب کنید.";
            return RedirectToPage("/Bank");
        }
        Checks = await store.ChecksAsync(ids);
        return Checks.Count == 0 ? NotFound() : Page();
    }
}
