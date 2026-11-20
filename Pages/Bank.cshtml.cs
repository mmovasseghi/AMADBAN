using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "agent,chief")]
public sealed class BankModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    public List<Prepayment> Paid { get; private set; } = [];
    public List<BankCheck> Checks { get; private set; } = [];
    [TempData] public string? Message { get; set; }
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        P = Math.Clamp(P, 1, 4000);
        Checks = await store.ChecksAsync(null, P);
        Paid = await store.RecentPaidPrepaymentsAsync();
    }

    public async Task<IActionResult> OnPostAsync(string? sayad, string? account, string? bank, string? branch,
        string? date, string? amount, string? recipient)
    {
        sayad = Persian.Latin(sayad).Replace(" ", "");
        account = Persian.Latin(account);
        bank = bank?.Trim() ?? "";
        branch = branch?.Trim() ?? "";
        recipient = recipient?.Trim() ?? "";
        Message = sayad.Length != 16 || sayad.Any(c => !char.IsAsciiDigit(c)) || account.Length is < 1 or > 50 ||
            bank.Length is < 1 or > 80 || branch.Length is < 1 or > 80 || recipient.Length is < 1 or > 150 ||
            Persian.ParseDate(date) is not DateOnly day ||
            !long.TryParse(Persian.Latin(amount).Replace(",", "").Replace("٬", ""), out var value) || value is <= 0 or > 1_000_000_000_000_000L
            ? "شناسه صیاد ۱۶ رقمی و اطلاعات چک را بررسی کنید."
            : await store.SaveCheckAsync(sayad, account, bank, branch, day, value, recipient, Actor);
        return RedirectToPage();
    }
}
