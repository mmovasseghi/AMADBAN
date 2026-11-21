using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles="kardex,chief")]
public sealed class KardexQueueModel(Store store):PageModel
{
    [BindProperty(SupportsGet=true)] public int P { get; set; }=1;
    public List<InvoiceSummary> Rows { get; private set; }=[];
    public bool HasNext { get; private set; }
    [TempData] public string? Message { get; set; }
    private long Actor=>long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public async Task OnGetAsync()
    {
        P=Math.Clamp(P,1,4000);
        Rows=await store.KardexQueueAsync(P);
        HasNext=Rows.Count>25;
        Rows=Rows.Take(25).ToList();
    }
    public async Task<IActionResult> OnPostAsync(long id,string? number,string? date)
    {
        if (!User.IsInRole("kardex") && !User.IsInRole("chief")) return Forbid();
        number=Persian.Latin(number);
        Message=number.Length is <1 or >80 || Persian.ParseDate(date) is not DateOnly day
            ? "شماره‌ای را که روی سند نوشته‌اید و تاریخ ثبت شمسی را وارد کنید."
            : await store.SaveKardexAsync(id,Actor,number,day)?"کارتکس ثبت شد؛ سند از فهرست انتظار برداشته شد.":"ثبت کارتکس این سند ممکن نیست.";
        return RedirectToPage();
    }
}
