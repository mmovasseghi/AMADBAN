using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages.Invoices;

public sealed class PrintModel(Store store) : PageModel
{
    public InvoiceDetail Entry { get; private set; } = null!;
    public string Template { get; private set; } = "readable";
    public int Rows => Template switch { "rows7" => 7, "rows9" => 9, "rows12" => 12, "rows20" => 20, "excel11" => 11, _ => 0 };
    public async Task<IActionResult> OnGetAsync(long id, string? template)
    {
        var invoice = await store.DetailAsync(id);
        if (invoice?.Summary.Document is null) return NotFound();
        Entry = invoice;
        template ??= await store.PrintPreferenceAsync(long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), "document");
        if (template is not null && Amadban.Pages.PrintSettingsModel.Documents.ContainsKey(template))
            Template = template;
        return Page();
    }
}
