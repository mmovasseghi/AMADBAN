using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

public sealed class PrintSettingsModel(Store store) : PageModel
{
    public static readonly Dictionary<string, string> Documents = new()
    {
        ["readable"] = "خوانا", ["rows7"] = "۷ خطی", ["rows9"] = "۹ خطی", ["rows12"] = "۱۲ خطی"
    };
    public static readonly Dictionary<string, string> Prepayments = new()
    {
        ["readable"] = "خوانا", ["permit"] = "با فیلد مجوز", ["tax"] = "با فیلد مالیات"
    };
    public async Task<IActionResult> OnPostAsync(string? form, string? template, long id)
    {
        var options = form == "document" ? Documents : form == "prepayment" ? Prepayments : null;
        if (options is null || template is null || id <= 0 || !options.ContainsKey(template)) return BadRequest();
        await store.SavePrintPreferenceAsync(long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), form!, template);
        return form == "document" ? RedirectToPage("/Invoices/Print", new { id, template })
            : RedirectToPage("/PrintPrepayment", new { number = id, template });
    }
}
