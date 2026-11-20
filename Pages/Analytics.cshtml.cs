using Amadban.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

public sealed class AnalyticsModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Group { get; set; } = "vendor";
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Search { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Subject { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string Credit { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string From { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string To { get; set; } = "";
    public ReportOverview Overview { get; private set; } = new(0, 0, 0, 0);
    public List<ReportGroup> Groups { get; private set; } = [];

    public async Task OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(From)) From = Persian.Date(Persian.Today)[..4] + "/01/01";
        if (string.IsNullOrWhiteSpace(To)) To = Persian.Date(Persian.Today);
        var from = Persian.ParseDate(From);
        var to = Persian.ParseDate(To);
        if (from is null || to is null || from > to)
        {
            ModelState.AddModelError("", "بازه تاریخ شمسی را بررسی کنید.");
            return;
        }
        Group = Group is "vendor" or "subject" or "credit" or "kind" ? Group : "vendor";
        Search = Persian.Latin(Search);
        if (Search.Length > 100) Search = Search[..100];
        Subject = (Subject ?? "").Trim();
        if (Subject.Length > 100) Subject = Subject[..100];
        Credit = Persian.Latin(Credit);
        if (Credit.Length > 40) Credit = Credit[..40];
        if (Kind is not ("cash" or "credit")) Kind = "";
        var result = await store.GroupedReportAsync(Group, Status ?? "", Search, Subject, Kind, Credit,
            from.Value, to.Value);
        Overview = result.Overview;
        Groups = result.Groups;
    }
}
