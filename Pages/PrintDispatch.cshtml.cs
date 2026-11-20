using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "warehouse,chief")]
public sealed class PrintDispatchModel(Store store) : PageModel
{
    public DispatchRecord Issue { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long number)
    {
        var issue = await store.DispatchAsync(number);
        if (issue is null) return NotFound();
        Issue = issue;
        return Page();
    }
}
