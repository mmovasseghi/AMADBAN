using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles="agent,chief")]
public sealed class PrintPettyAdvanceModel(Store store):PageModel
{
    public PettyAdvance Advance { get; private set; }=null!;
    public async Task<IActionResult> OnGetAsync(long number)
    {
        var found=await store.PettyAdvanceAsync(number);
        if(found is null)return NotFound();
        Advance=found;
        return Page();
    }
}
