using System.Globalization;
using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "warehouse,chief")]
public sealed class DispatchModel(Store store) : PageModel
{
    public List<Department> Departments { get; private set; } = [];
    public List<DepartmentRoster> Roster { get; private set; } = [];
    public List<(long Id, string Name)> WarehouseStaff { get; private set; } = [];
    public List<DispatchRecord> Recent { get; private set; } = [];
    public Guid Submission { get; private set; } = Guid.NewGuid();
    [TempData] public string? Message { get; set; }
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        Departments = await store.DepartmentsAsync();
        Roster = await store.DepartmentRosterAsync();
        WarehouseStaff = await store.WarehouseStaffAsync();
        Recent = await store.RecentDispatchesAsync();
    }

    public async Task<IActionResult> OnGetRecipientsAsync(long department)
    {
        if (department <= 0) return new JsonResult(Array.Empty<object>());
        var people = await store.RecipientsByDepartmentAsync(department);
        return new JsonResult(people.Select(p => new
        {
            id = p.Id,
            label = $"{p.Department} · {p.Rank} {p.Name}{(p.Position.Length > 0 ? " · " + p.Position : "")}"
        }));
    }

    public async Task<IActionResult> OnGetProductsAsync(string? search)
    {
        search = Persian.Latin(search);
        if (search.Length is < 2 or > 80) return new JsonResult(Array.Empty<object>());
        var products = await store.ProductHintsAsync(search);
        return new JsonResult(products.Select(p => new { code = p.Code, name = p.Name, stock = p.Stock, unit = p.Unit }));
    }

    public async Task<IActionResult> OnPostIssueAsync(Guid submission, long department, long recipient,
        long warehouse, string? date, string? note, List<LineInput>? lines)
    {
        note = note?.Trim() ?? "";
        var day = Persian.ParseDate(date);
        if (submission == Guid.Empty || day is null || department <= 0 || recipient <= 0 || warehouse <= 0 ||
            note.Length > 250 || lines is null || lines.Count > 30)
        {
            Message = "بخش، تحویل‌گیرنده، انباردار، تاریخ و اقلام را بررسی کنید.";
            return RedirectToPage();
        }
        var items = new List<DispatchLineInput>();
        foreach (var line in lines)
        {
            var code = Persian.Latin(line.Code);
            if (code.Length == 0 && string.IsNullOrWhiteSpace(line.Quantity)) continue;
            if (code.Length is < 1 or > 40 || !decimal.TryParse(Persian.Latin(line.Quantity).Replace('٫','.'),
                    NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity) ||
                quantity is <= 0 or > 1000000 || Math.Round(quantity,3) != quantity)
            {
                Message = "کد کالا و تعداد هر ردیف را بررسی کنید.";
                return RedirectToPage();
            }
            items.Add(new(code, quantity));
        }
        var result = await store.CreateDispatchAsync(submission, department, recipient, warehouse, day.Value, note, items, Actor);
        Message = result.Message;
        return result.Number is long number ? RedirectToPage("/PrintDispatch", new { number }) : RedirectToPage();
    }

    public sealed class LineInput
    {
        public string? Code { get; set; }
        public string? Quantity { get; set; }
    }
}
