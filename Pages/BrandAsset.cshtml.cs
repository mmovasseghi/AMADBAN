using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[AllowAnonymous]
public sealed class BrandAssetModel(Store store, IWebHostEnvironment environment) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string code)
    {
        if (code is not ("faraja" or "health")) return NotFound();
        Response.Headers.CacheControl = "public,no-cache";
        if (await store.BrandImageAsync(code) is { } file)
            return new FileContentResult(file.Content, file.Type) { LastModified = file.Updated };
        return PhysicalFile(Path.Combine(environment.WebRootPath, "brand", code + ".png"), "image/png");
    }
}
