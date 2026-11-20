using System.Text.RegularExpressions;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "chief")]
public sealed class StaffModel(Store store) : PageModel
{
    public List<(string Username, string Name, string Role)> Users { get; private set; } = [];
    public static readonly Dictionary<string, string> Roles = new()
    {
        ["chief"] = "رئیس آماد", ["agent"] = "افسرعامل و مسئول خرید", ["warehouse"] = "انباردار", ["kardex"] = "مسئول کارتکس"
    };
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync() => Users = await store.StaffAsync();

    public async Task<IActionResult> OnPostAsync(string? username, string? name, string? role, string? password)
    {
        username = (username ?? "").Trim().ToLowerInvariant();
        name = name?.Trim() ?? "";
        if (!Regex.IsMatch(username, @"\A[a-z0-9_.-]{3,50}\z") || name.Length is < 2 or > 120 ||
            role is null || !Roles.ContainsKey(role) || password?.Length is not (>= 12 and <= 256))
            Message = "نام، نام کاربری و رمز حداقل ۱۲ حرفی را بررسی کنید.";
        else
            Message = await store.AddStaffAsync(username, name, role, password) ? "حساب ساخته شد." : "این نام کاربری قبلاً ثبت شده است.";
        return RedirectToPage();
    }
}
