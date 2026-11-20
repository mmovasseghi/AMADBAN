using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles="chief")]
public sealed class ActivityDetailModel(Store store):PageModel
{
    public ActivityItem Item { get; private set; }=null!;
    public async Task<IActionResult> OnGetAsync(long id)
    {
        var item=await store.ActivityDetailAsync(id);
        if(item is null)return NotFound();
        Item=item;
        return Page();
    }
}
