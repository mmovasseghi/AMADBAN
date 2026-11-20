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
    public List<PettyAdvance> Advances { get; private set; } = [];
    public long NextInvoiceNumber { get; private set; }
    public Guid Submission { get; } = Guid.NewGuid();
    [TempData] public string? Message { get; set; }
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public bool IsChief => User.IsInRole("chief");

    public async Task OnGetAsync()
    {
        P = Math.Clamp(P, 1, 4000);
        Advances = await store.RecentPettyAdvancesAsync();
        NextInvoiceNumber = await store.NextPettyInvoiceAsync();
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

    public async Task<IActionResult> OnPostAsync(Guid submission, long recipient, string? number, string? date, string? amount, long advanceNumber)
    {
        if (!IsChief) recipient = Actor;
        number = Persian.Latin(number);
        Message = recipient <= 0 || submission == Guid.Empty || number.Length is < 1 or > 80 || Persian.ParseDate(date) is not DateOnly day ||
            !long.TryParse(Persian.Latin(amount).Replace(",", "").Replace("٬", ""), out var value) || value is <= 0 or > 1_000_000_000_000_000L
            ? "دریافت‌کننده، شماره، تاریخ و مبلغ را بررسی کنید."
            : await store.ReceivePettyAsync(submission, recipient, number, day, value, Actor, advanceNumber);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAdvanceAsync(Guid submission,string? invoiceStart,string? date,string? amount,
        string? owner,string? bank,string? sheba,string? description)
    {
        if (IsChief) return Forbid();
        owner=owner?.Trim()??"";bank=bank?.Trim()??"";description=description?.Trim()??"";
        sheba=Persian.Latin(sheba).Replace(" ","").ToUpperInvariant();
        if(!sheba.StartsWith("IR",StringComparison.Ordinal))sheba="IR"+sheba;
        var valid=long.TryParse(Persian.Latin(amount).Replace(",","").Replace("٬",""),out var value)
            && value is >0 and <=1_000_000_000_000_000L;
        var start=long.TryParse(Persian.Latin(invoiceStart),out var initial)?initial:0;
        if(submission==Guid.Empty || !valid || Persian.ParseDate(date) is not DateOnly day ||
            owner.Length is <1 or >140 || bank.Length is <1 or >100 || sheba.Length!=26 ||
            sheba[2..].Any(c=>!char.IsAsciiDigit(c)) || description.Length is <1 or >250)
        {
            Message="تاریخ، مبلغ، نام صاحب حساب، بانک، شبا و بابت تنخواه را بررسی کنید.";
            return RedirectToPage();
        }
        var result=await store.CreatePettyAdvanceAsync(submission,start,day,value,owner,bank,sheba,description,Actor);
        Message=result.Message;
        return result.Number is long number ? RedirectToPage("/PrintPettyAdvance",new{number}) : RedirectToPage();
    }

    public async Task<IActionResult> OnPostCorrectAdvanceAsync(long number,string? date,string? amount,
        string? owner,string? bank,string? sheba,string? description)
    {
        if (IsChief) return Forbid();
        var valid=long.TryParse(Persian.Latin(amount).Replace(",","").Replace("٬",""),out var value)
            && value is >0 and <=1_000_000_000_000_000L;
        sheba=Persian.Latin(sheba).Replace(" ","").ToUpperInvariant();
        if(!sheba.StartsWith("IR",StringComparison.Ordinal))sheba="IR"+sheba;
        Message=!valid || Persian.ParseDate(date) is not DateOnly day || (owner?.Trim().Length ?? 0) is <1 or >140 ||
            (bank?.Trim().Length ?? 0) is <1 or >100 || sheba.Length!=26 || sheba[2..].Any(c=>!char.IsAsciiDigit(c)) ||
            (description?.Trim().Length ?? 0) is <1 or >250
            ? "مشخصات برگه تنخواه را بررسی کنید."
            : await store.CorrectPettyAdvanceAsync(number,day,value,owner!.Trim(),bank!.Trim(),sheba,description!.Trim(),Actor,IsChief);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostVoidAdvanceAsync(long number)
    {
        if (IsChief) return Forbid();
        Message=await store.VoidPettyAdvanceAsync(number,Actor,IsChief);
        return RedirectToPage();
    }
}
