using System.Text;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "warehouse,chief")]
public sealed class WarehouseReportModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public string From { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string To { get; set; } = "";
    [BindProperty(SupportsGet = true)] public long Department { get; set; }
    [BindProperty(SupportsGet = true)] public string Search { get; set; } = "";
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    public List<Department> Departments { get; private set; } = [];
    public WarehouseReportOverview Overview { get; private set; } = new(0,0,0);
    public List<DepartmentUsage> Usage { get; private set; } = [];
    public List<DispatchRecord> Issues { get; private set; } = [];
    public bool HasNext { get; private set; }

    private (DateOnly? From, DateOnly? To) Dates()
    {
        if (string.IsNullOrWhiteSpace(From)) From = Persian.Date(Persian.Today)[..7] + "/01";
        if (string.IsNullOrWhiteSpace(To)) To = Persian.Date(Persian.Today);
        return (Persian.ParseDate(From), Persian.ParseDate(To));
    }

    private bool Valid(out DateOnly from, out DateOnly to)
    {
        (var start,var end) = Dates();
        from = start ?? default; to = end ?? default;
        if (start is null || end is null || start > end || Department < 0)
        {
            ModelState.AddModelError("", "بازه تاریخ شمسی و بخش را بررسی کنید.");
            return false;
        }
        Search = Persian.Latin(Search);
        if (Search.Length > 100) Search = Search[..100];
        return true;
    }

    public async Task OnGetAsync()
    {
        Departments = await store.DepartmentsAsync();
        if (!Valid(out var from,out var to)) return;
        P = Math.Clamp(P,1,4000);
        (Overview,Usage,Issues,HasNext) = await store.WarehouseReportAsync(from,to,Department,Search,P);
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        if (!Valid(out var from,out var to)) return BadRequest("بازه تاریخ شمسی و بخش را بررسی کنید.");
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=amadban-warehouse-{DateTime.UtcNow:yyyyMMdd}.csv";
        Response.Headers.CacheControl = "private, no-store";
        await using var writer = new StreamWriter(Response.Body,new UTF8Encoding(true),leaveOpen:true);
        await store.ExportDispatchesAsync(writer,from,to,Department,Search,HttpContext.RequestAborted);
        await writer.FlushAsync(HttpContext.RequestAborted);
        return new EmptyResult();
    }
}
