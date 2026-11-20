using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "chief,agent")]
public sealed class PettyModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public long Recipient { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string From { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string To { get; set; } = "";
    public List<(long Id, string Name)> Agents { get; private set; } = [];
    public List<PettyBalance> Balances { get; private set; } = [];
    public List<PettyEntry> Entries { get; private set; } = [];
    public Guid Submission { get; } = Guid.NewGuid();
    [TempData] public string? Message { get; set; }
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public bool IsChief => User.IsInRole("chief");

    public async Task OnGetAsync()
    {
        P = Math.Clamp(P, 1, 4000);
        Agents = IsChief ? await store.AgentsAsync() : [(Actor, User.Identity!.Name!)];
        if (!IsChief) Recipient = Actor;
        var f = Persian.ParseDate(From);
        var t = Persian.ParseDate(To);
        if ((!string.IsNullOrEmpty(From) && f is null) || (!string.IsNullOrEmpty(To) && t is null) ||
            (f is not null && t is not null && f > t))
        {
            ModelState.AddModelError("", "بازه تاریخ شمسی را بررسی کنید.");
            return;
        }
        (Balances, Entries) = await store.PettyReportAsync(Recipient, f ?? new DateOnly(1900, 1, 1),
            t ?? new DateOnly(2200, 1, 1), P);
    }

    public async Task<IActionResult> OnPostAsync(Guid submission, long recipient, string? number, string? date, string? amount)
    {
        if (!IsChief) recipient = Actor;
        number = Persian.Latin(number);
        Message = recipient <= 0 || submission == Guid.Empty || number.Length is < 1 or > 80 || Persian.ParseDate(date) is not DateOnly day ||
            !long.TryParse(Persian.Latin(amount).Replace(",", "").Replace("٬", ""), out var value) || value is <= 0 or > 1_000_000_000_000_000L
            ? "دریافت‌کننده، شماره، تاریخ و مبلغ را بررسی کنید."
            : await store.ReceivePettyAsync(submission, recipient, number, day, value, Actor);
        return RedirectToPage();
    }
}
