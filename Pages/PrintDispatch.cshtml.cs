using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Globalization;
using System.Security.Claims;

namespace Amadban.Pages;

[Authorize(Roles = "warehouse,chief")]
public sealed class PrintDispatchModel(Store store) : PageModel
{
    public DispatchRecord Issue { get; private set; } = null!;
    public Guid Submission { get; } = Guid.NewGuid();
    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(long number)
    {
        var issue = await store.DispatchAsync(number);
        if (issue is null) return NotFound();
        Issue = issue;
        return Page();
    }

    public async Task<IActionResult> OnPostReturnAsync(long number, int position, string? quantity,
        string? date, Guid submission)
    {
        var parsed = decimal.TryParse(Persian.Latin(quantity).Replace('٫','.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount);
        Message = Persian.ParseDate(date) is not DateOnly day || (position > 0 && !parsed)
            ? "تاریخ و مقدار برگشتی را بررسی کنید."
            : await store.ReturnDispatchAsync(number, position, position == 0 ? 0 : amount,
                submission, day, long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return RedirectToPage(new { number });
    }
}
