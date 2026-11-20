using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "chief")]
[RequestSizeLimit(3 * 1024 * 1024)]
public sealed class OrganizationModel(Store store) : PageModel
{
    public static readonly Dictionary<string, string> Roles = new()
    {
        ["agent"] = "افسرعامل / مسئول خرید", ["expert"] = "کارشناس / رئیس قسمت", ["warehouse"] = "انباردار",
        ["chief"] = "رئیس / دارنده مجوز ماده ۵۳", ["auditor"] = "ممیز اسناد هزینه",
        ["finance"] = "مسئول مالی / عامل ذی‌حساب", ["recipient"] = "گیرنده وجه"
    };
    public Dictionary<string, string> Signers { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync() => Signers = await store.SignersAsync();

    public async Task<IActionResult> OnPostLogoAsync(string? code, IFormFile? file)
    {
        if (code is not ("faraja" or "health") || file?.Length is not (> 0 and <= 2 * 1024 * 1024) ||
            await DocumentUpload.ReadAsync(file) is not { Type: "image/png" or "image/jpeg" } upload)
            Message = "نشان اصلی PNG یا JPG تا ۲ مگابایت را انتخاب کنید.";
        else
        {
            await store.SaveBrandImageAsync(code, upload);
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
            await store.SaveSignerAsync(role, name);
            Message = "نام چاپی ثبت شد.";
        }
        return RedirectToPage();
    }
}
