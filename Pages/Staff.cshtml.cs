using System.Text.RegularExpressions;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace Amadban.Pages;

[Authorize(Roles = "chief")]
public sealed class StaffModel(Store store) : PageModel
{
    public List<(long Id, string Username, string Name, string Role, bool Active)> Users { get; private set; } = [];
    public List<StaffEvent> Events { get; private set; } = [];
    public static readonly Dictionary<string, string> Roles = new()
    {
        ["chief"] = "رئیس آماد", ["agent"] = "افسرعامل و مسئول خرید", ["warehouse"] = "انباردار", ["kardex"] = "مسئول کارتکس"
    };
    [TempData] public string? Message { get; set; }

    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Users = await store.StaffAsync();
        Events = await store.StaffEventsAsync();
    }

    public async Task<IActionResult> OnPostAsync(string? username, string? name, string? role, string? password)
    {
        username = (username ?? "").Trim().ToLowerInvariant();
        name = name?.Trim() ?? "";
        if (!Regex.IsMatch(username, @"\A[a-z0-9_.-]{3,50}\z") || name.Length is < 2 or > 120 ||
            role is null || !Roles.ContainsKey(role) || password?.Length is not (>= 12 and <= 256))
            Message = "نام، نام کاربری و رمز حداقل ۱۲ حرفی را بررسی کنید.";
        else
            Message = await store.AddStaffAsync(username, name, role, password, Actor) ? "حساب ساخته شد." : "این نام کاربری قبلاً ثبت شده است.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync(long id, string? password)
    {
        Message = id <= 0 || password?.Length is not (>= 12 and <= 256)
            ? "شناسه حساب یا رمز تازه با حداقل ۱۲ نویسه را بررسی کنید."
            : await store.ResetStaffPasswordAsync(id, password, Actor);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStateAsync(long id, string? action)
    {
        Message = id <= 0 || action is not ("activate" or "deactivate")
            ? "اقدام حساب معتبر نیست."
            : await store.SetStaffActiveAsync(id, action == "activate", Actor);
        return RedirectToPage();
    }
}
