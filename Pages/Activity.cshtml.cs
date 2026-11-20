using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "chief")]
public sealed class ActivityModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    public List<ActivityItem> Items { get; private set; } = [];
    public bool HasNext { get; private set; }
    private long Chief => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task OnGetAsync()
    {
        P = Math.Clamp(P,1,4000);
        Items = await store.ActivityAsync(P,26);
        HasNext = Items.Count>25;
        Items = Items.Take(25).ToList();
        if (P==1) await store.MarkActivityReadAsync(Chief);
    }

    public async Task<IActionResult> OnGetUnreadAsync() => new JsonResult(new { count = await store.UnreadActivityAsync(Chief) });
}
