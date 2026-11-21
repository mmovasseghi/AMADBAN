using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Amadban.Pages;

[Authorize(Roles = "chief")]
[RequestSizeLimit(3 * 1024 * 1024)]
public sealed class OrganizationModel(Store store) : PageModel
{
    public static readonly Dictionary<string, string> Roles = new()
    {
        ["agent"] = "افسرعامل / مسئول خرید", ["expert"] = "کارشناس / رئیس قسمت", ["warehouse"] = "انباردار",
        ["chief"] = "رئیس / دارنده مجوز ماده ۵۳", ["auditor"] = "ممیز اسناد هزینه",
        ["finance"] = "مسئول مالی / عامل ذی‌حساب", ["requester"] = "مسئول قسمت درخواست‌کننده",
        ["kardex"] = "کارمند کارتکس", ["credit_officer"] = "مسئول اعتبارات", ["accounting"] = "حسابداری",
        ["petty_authorizer"] = "گواهی تقاضای هزینه"
    };
    public Dictionary<string, string> Signers { get; private set; } = [];
    public List<Person> People { get; private set; } = [];
    [TempData] public string? Message { get; set; }
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public bool DemoSeedAvailable { get; private set; }

    public async Task OnGetAsync()
    {
        Signers = await store.SignersAsync();
        People = await store.PeopleAsync();
        DemoSeedAvailable = await store.IsDemoSeedAvailableAsync();
    }

    public async Task<IActionResult> OnPostSeedDemoAsync()
    {
        Message = await store.SeedPresentationAsync(Actor);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPersonAsync(string? name, string? position, string? rank)
    {
        name = name?.Trim() ?? "";
        position = position?.Trim() ?? "";
        rank = rank?.Trim() ?? "";
        Message = name.Length is < 2 or > 120 || position.Length > 120 || rank.Length > 80
            ? "نام و سمت شخص را بررسی کنید."
            : await store.AddPersonAsync(name, position, rank, 0, Actor);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCorrectPersonAsync(long id,string? name,string? position,string? rank,long department)
    {
        name=name?.Trim()??"";position=position?.Trim()??"";rank=rank?.Trim()??"";
        Message=id<=0 || name.Length is <2 or >120 || position.Length>120 || rank.Length>80
            ? "نام و سمت شخص را بررسی کنید."
            : await store.CorrectPersonAsync(id,name,position,rank,department,Actor);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAssignAsync(string? role, long person)
    {
        Message = role is null || !Roles.ContainsKey(role) || person <= 0
            ? "جایگاه و شخص را انتخاب کنید."
            : await store.AssignSignerAsync(role, person, Actor) ? "نام شخص به محل امضا متصل شد." : "شخص فعال پیدا نشد.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLogoAsync(string? code, IFormFile? file)
    {
        if (code is not ("faraja" or "health") || file?.Length is not (> 0 and <= 2 * 1024 * 1024) ||
            await DocumentUpload.ReadAsync(file) is not { Type: "image/png" or "image/jpeg" } upload)
            Message = "نشان اصلی PNG یا JPG تا ۲ مگابایت را انتخاب کنید.";
        else
        {
            await store.SaveBrandImageAsync(code, upload, Actor);
            Message = "نشان اصلی ثبت شد.";
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSignerAsync(string? role, string? name)
    {
        name = name?.Trim() ?? "";
        if (role is null || !Roles.ContainsKey(role) || name.Length > 120)
            Message = "جایگاه و نام امضاکننده را بررسی کنید.";
        else
        {
            await store.SaveSignerAsync(role, name, Actor);
            Message = "نام چاپی ثبت شد.";
        }
        return RedirectToPage();
    }
}
