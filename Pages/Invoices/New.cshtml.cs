using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages.Invoices;

[Authorize(Roles = "agent,chief")]
public sealed class NewModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }
    [BindProperty] public long CashCredit { get; set; }
    public List<(long Id,string Code,string Title)> Codes { get; private set; } = [];
    [BindProperty] public Guid Submission { get; set; }
    [BindProperty, Required, StringLength(80)] public string Number { get; set; } = "";
    [BindProperty, Required, StringLength(180)] public string Vendor { get; set; } = "";
    [BindProperty, Required, StringLength(250)] public string Subject { get; set; } = "";
    [BindProperty, Required] public string Date { get; set; } = "";
    [BindProperty] public string Kind { get; set; } = "credit";
    [BindProperty] public bool Warehouse { get; set; } = true;
    [BindProperty] public bool Meeting { get; set; }
    [BindProperty] public bool Account { get; set; }
    [BindProperty] public bool Tax { get; set; }
    [BindProperty] public List<LineForm> Lines { get; set; } = [];

    public sealed class LineForm
    {
        public string? Title { get; set; }
        public string? Unit { get; set; }
        public string? Quantity { get; set; }
        public string? Price { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Codes = await store.CreditLabelsAsync();
        Submission = Guid.NewGuid();
        Date = Persian.Date(Persian.Today);
        Lines = [new() { Quantity = "1" }];
        if (Id <= 0) return Page();
        var current = await store.DetailAsync(Id);
        if (current is null) return NotFound();
        if (current.Summary.Delivery is "closed" or "delivered") return RedirectToPage("Detail", new { id = Id });
        Number = current.Summary.Number;
        Vendor = current.Summary.Vendor;
        Subject = current.Summary.Subject;
        Date = Persian.Date(current.Summary.Date);
        Kind = current.Summary.Kind;
        Warehouse = current.Summary.Warehouse;
        Meeting = current.Meeting;
        Account = current.Account;
        Tax = current.Tax;
        CashCredit = await store.CashCodeAsync(Id);
        Lines = current.Lines.Select(line => new LineForm
        {
            Title = line.Title, Unit = line.Unit, Quantity = line.Quantity.ToString("0.###", CultureInfo.InvariantCulture),
            Price = line.Price.ToString(CultureInfo.InvariantCulture)
        }).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Codes = await store.CreditLabelsAsync();
        var date = Persian.ParseDate(Date);
        if (CashCredit < 0 || (CashCredit > 0 && !Codes.Any(c => c.Id == CashCredit)))
            ModelState.AddModelError("", "کد اعتباری خرید نقدی را بررسی کنید.");
        if (date is null) ModelState.AddModelError("", "تاریخ فاکتور را به‌صورت شمسی وارد کنید.");
        if (Kind is not ("cash" or "credit")) ModelState.AddModelError("", "نوع خرید را انتخاب کنید.");
        if (Submission == Guid.Empty) return BadRequest();
        if (Lines.Count > 20) ModelState.AddModelError("", "هر فاکتور حداکثر ۲۰ قلم دارد. فاکتورهای بیشتر از ۲۰ قلم در آمادبان پذیرفته نمی‌شوند.");
        var lines = new List<InvoiceLine>();
        foreach (var line in Lines.Take(20))
        {
            var title = line.Title?.Trim() ?? "";
            var unit = line.Unit?.Trim() ?? "";
            if (title.Length == 0 && string.IsNullOrWhiteSpace(line.Price)) continue;
            var quantity = Persian.Latin(line.Quantity).Replace('٫', '.');
            var price = Persian.Latin(line.Price).Replace(",", "").Replace("٬", "");
            if (title.Length is < 1 or > 250 || unit.Length > 30 ||
                !decimal.TryParse(quantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var qty) ||
                qty is <= 0 or > 1000000 || Math.Round(qty, 3) != qty ||
                !long.TryParse(price, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                value > 10_000_000_000L)
            {
                ModelState.AddModelError("", "نام، تعداد و قیمت هر قلم را بررسی کنید.");
                break;
            }
            lines.Add(new(title, unit.Length == 0 ? "عدد" : unit, qty, value,
                (long)Math.Round(qty * value, MidpointRounding.AwayFromZero)));
        }
        if (lines.Sum(line => line.Amount) <= 0)
            ModelState.AddModelError("", "حداقل یک قلم با مبلغ مثبت وارد کنید.");
        if (!ModelState.IsValid || date is null)
        {
            if (Lines.Count == 0) Lines.Add(new());
            return Page();
        }
        var actor = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var entry = new InvoiceEntry(Submission, Persian.Latin(Number), Vendor.Trim(), Subject.Trim(),
            date.Value, Kind, Warehouse, Meeting || !Warehouse, Account && Kind == "credit", Tax, lines,
            Kind == "cash" ? CashCredit : 0);
        if (Id > 0)
        {
            var error = await store.CorrectInvoiceAsync(Id, entry, actor);
            if (error is null) return RedirectToPage("Detail", new { id = Id });
            ModelState.AddModelError("", error);
            return Page();
        }
        var id = await store.AddInvoiceAsync(entry, actor);
        return RedirectToPage("Detail", new { id });
    }
}
