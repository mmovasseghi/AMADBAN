using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles="agent,chief")]
[RequestSizeLimit(11*1024*1024)]
public sealed class TaxBatchModel(Store store):PageModel
{
    [BindProperty(SupportsGet=true)] public string From { get; set; }="";
    [BindProperty(SupportsGet=true)] public string To { get; set; }="";
    public List<InvoiceSummary> Pending { get; private set; }=[];
    public List<TaxBatchSummary> Recent { get; private set; }=[];
    public Guid Submission { get; }=Guid.NewGuid();
    [TempData] public string? Message { get; set; }
    private long Actor=>long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        var from=Persian.ParseDate(From);
        var to=Persian.ParseDate(To);
        if((From.Length>0 && from is null)||(To.Length>0 && to is null)||(from is not null && to is not null && from>to))
        { ModelState.AddModelError("","بازه تاریخ فاکتورها را بررسی کنید.");return; }
        Pending=await store.TaxPendingAsync(from??Persian.Today.AddMonths(-6),to??Persian.Today);
        Recent=await store.RecentTaxBatchesAsync();
    }

    public async Task<IActionResult> OnPostAsync(Guid submission,string? date,string? reference,List<long>? ids,IFormFile? file)
    {
        reference=reference?.Trim()??"";
        var upload=file?.Length is >0 and <=5*1024*1024?await DocumentUpload.ReadAsync(file):null;
        if(submission==Guid.Empty || Persian.ParseDate(date) is not DateOnly day || reference.Length>100 ||
           ids is null || ids.Count is <1 or >50 || upload is null)
            Message="تاریخ، سندهای انتخاب‌شده و برگه PDF یا تصویر تا ۵ مگابایت را بررسی کنید.";
        else
        {
            var result=await store.CreateTaxBatchAsync(submission,day,reference,ids,upload,Actor);
            Message=result.Message;
        }
        return RedirectToPage(new{From,To});
    }

    public async Task<IActionResult> OnGetFileAsync(long id,long invoice=0)
    {
        var file=await store.TaxBatchFileAsync(id,invoice);
        if(file is null)return NotFound();
        Response.Headers.CacheControl="private, no-store";
        return File(file.Content,file.Type);
    }
}
