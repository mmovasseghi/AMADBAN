using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[AllowAnonymous]
public sealed class BrandAssetModel(Store store) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string code)
    {
        if (code is not ("faraja" or "health") || await store.BrandImageAsync(code) is not { } file) return NotFound();
        Response.Headers.CacheControl = "public,max-age=600";
        return new FileContentResult(file.Content, file.Type) { LastModified = file.Updated };
    }
}
