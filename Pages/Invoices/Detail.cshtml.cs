using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages.Invoices;

[RequestSizeLimit(11 * 1024 * 1024)]
public sealed class DetailModel(Store store) : PageModel
{
    public InvoiceDetail Entry { get; private set; } = null!;
    public RequestSigner RequestSigner { get; private set; } = new(null,null,null,null);
    public List<Department> Departments { get; private set; } = [];
    public List<Person> People { get; private set; } = [];
    public List<ReceiptMatch> ReceiptMatches { get; private set; } = [];
    public List<SupplierAccount> SupplierAccounts { get; private set; } = [];
    public (long? Id, string? Name) MeetingExtra { get; private set; }
    public IReadOnlyList<string> Missing => Store.Missing(Entry);
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;
    public bool HandlesFinance => Role is "chief" or "agent";
    public bool CanIssue => Entry.Summary.Document is null && (Entry.Summary.Kind == "cash"
        ? Role == "agent" : Role is "agent" or "warehouse" or "chief");
    public bool CanKardex => Entry.Summary.Warehouse && Role == "kardex";
    public bool CanAttach => Role is "agent" or "chief" or "warehouse";
    public bool CanReceive => Role is "warehouse" or "chief";
    public bool Editable => Entry.Summary.Delivery is "open" or "returned";
    public static readonly Dictionary<string, string> AttachmentKinds = new()
    {
        ["invoice"] = "اسکن فاکتور", ["pos"] = "رسید کارت‌خوان", ["meeting"] = "صورت‌جلسه",
        ["account"] = "تأییدیه حساب", ["taxpayer"] = "برگه مؤدیان", ["tax"] = "برگه ثبت مالیات",
        ["bank-slip"] = "فیش واریز", ["proxy"] = "برگه نیابتی", ["check"] = "تصویر چک"
    };
    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var entry = await store.DetailAsync(id);
        if (entry is null) return NotFound();
        Entry = entry;
        RequestSigner = await store.RequestSignerAsync(id);
        if (HandlesFinance) { Departments = await store.DepartmentsAsync(); People = await store.PeopleAsync(); }
        if (entry.Summary.Warehouse && CanReceive) ReceiptMatches = await store.ReceiptMatchesAsync(id);
        if (entry.Account && HandlesFinance && await store.SupplierByNameAsync(entry.Summary.Vendor) is { } supplier)
            SupplierAccounts = await store.SupplierAccountsAsync(supplier.Id);
        if (entry.Meeting) MeetingExtra = await store.MeetingExtraAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostIssueAsync(long id)
    {
        Message = await store.IssueDocumentAsync(id, Actor, Role) ? "سند صادر شد." : "صدور سند برای این پرونده ممکن نیست.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostKardexAsync(long id, string? number, string? date)
    {
        if (Role != "kardex") return Forbid();
        var parsed = Persian.ParseDate(date);
        number = Persian.Latin(number);
        if (parsed is null || number.Length is < 1 or > 80)
            Message = "شماره کارتکس و تاریخ شمسی معتبر وارد کنید.";
        else
            Message = await store.SaveKardexAsync(id, Actor, number, parsed.Value) ? "کارتکس ثبت شد." : "ثبت کارتکس ممکن نیست.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostAttachAsync(long id, string? kind, IFormFile? file)
    {
        if (!CanAttach) return Forbid();
        var document = await DocumentUpload.ReadAsync(file);
        if (kind is null || !AttachmentKinds.ContainsKey(kind) || document is null)
        {
            Message = "نوع مدرک و فایل PDF، JPG یا PNG تا ۱۰ مگابایت را انتخاب کنید.";
            return RedirectToPage(new { id });
        }
        Message = await store.AddAttachmentAsync(id, Actor, kind, document.Name, document.Type, document.Content)
            ? "مدرک اضافه شد." : "پرونده برای پیوست مدرک در دسترس نیست.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSupplierDocumentAsync(long id, string? kind)
    {
        if (!HandlesFinance) return Forbid();
        if (kind is not ("account" or "taxpayer")) return BadRequest();
        Message = await store.CopySupplierDocumentAsync(id, kind, Actor)
            ? "مدرک فروشنده به پرونده اضافه شد." : "مدرک موجود نیست یا پیش‌تر به پرونده اضافه شده است.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetAttachmentAsync(long id, long attachment, bool preview)
    {
        var file = await store.ReadAttachmentAsync(id, attachment);
        if (file is not { } found) return NotFound();
        return preview ? File(found.Content, found.Type) : File(found.Content, found.Type, found.Name);
    }

    public async Task<IActionResult> OnPostFinanceAsync(long id, string action)
    {
        if (!HandlesFinance) return Forbid();
        Message = await store.UpdateFinanceAsync(id, Actor, Role, action);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostPosAsync(long id, bool embedded)
    {
        if (!HandlesFinance) return Forbid();
        Message = await store.SetPosInInvoiceAsync(id, embedded, Actor);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReceiptAsync(long id, string? date, string? operation, List<string?>? productCodes)
    {
        if (!CanReceive) return Forbid();
        var day = Persian.ParseDate(date);
        Message = day is null ? "تاریخ شمسی ورود یا اصلاح انبار را بررسی کنید." : operation switch
        {
            "receive" => await store.ReceiveInvoiceAsync(id, day.Value, productCodes ?? [], Actor),
            "void" => await store.VoidInvoiceReceiptAsync(id, day.Value, Actor),
            _ => "اقدام انبار معتبر نیست."
        };
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRequestSignerAsync(long id, long department, long expert)
    {
        if (!HandlesFinance) return Forbid();
        Message = await store.SetRequestSignerAsync(id,department,expert,Actor);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMeetingExtraAsync(long id,long person)
    {
        if (!HandlesFinance) return Forbid();
        Message = await store.SetMeetingExtraAsync(id,person,Actor);
        return RedirectToPage(new { id });
    }
}
