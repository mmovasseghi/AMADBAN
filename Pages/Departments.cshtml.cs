using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "chief,agent,warehouse")]
public sealed class DepartmentsModel(Store store) : PageModel
{
    public List<Department> Departments { get; private set; } = [];
    public List<DepartmentRoster> Roster { get; private set; } = [];
    [TempData] public string? Message { get; set; }
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostDepartmentAsync(string? name)
    {
        name = name?.Trim() ?? "";
        Message = name.Length is < 2 or > 120 ? "نام بخش را بررسی کنید." : await store.AddDepartmentAsync(name, Actor);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPersonAsync(string? name, string? position, string? rank, long department)
    {
        name = name?.Trim() ?? "";
        position = position?.Trim() ?? "";
        rank = rank?.Trim() ?? "";
        Message = name.Length is < 2 or > 120 || position.Length > 120 || rank.Length > 80 || department < 1
            ? "بخش، نام و سمت تحویل‌گیرنده را بررسی کنید."
            : await store.AddPersonAsync(name, position, rank, department, Actor);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Departments = await store.DepartmentsAsync();
        Roster = await store.DepartmentRosterAsync();
    }
}