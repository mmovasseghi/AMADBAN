using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "chief,agent")]
[RequestSizeLimit(11 * 1024 * 1024)]
public sealed class SuppliersModel(Store store) : PageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string Search { get; set; } = "";
    public List<Supplier> Rows { get; private set; } = [];
    public Supplier? Supplier { get; private set; }
    public List<SupplierAccount> Accounts { get; private set; } = [];
    public List<Attachment> Documents { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        P = Math.Clamp(P, 1, 4000);
        Search = Search?.Trim() ?? "";
        if (Search.Length > 100) Search = Search[..100];
        Rows = await store.SuppliersAsync(Search, P);
        if (Id <= 0) return;
        Supplier = await store.SupplierAsync(Id);
        if (Supplier is null) return;
        Accounts = await store.SupplierAccountsAsync(Id);
        Documents = await store.SupplierDocumentsAsync(Id);
    }

    public async Task<IActionResult> OnGetNamesAsync(string? search)
    {
        search = search?.Trim() ?? "";
        if (search.Length is < 2 or > 100) return new JsonResult(Array.Empty<string>());
        return new JsonResult((await store.SuppliersAsync(search, 1)).Take(10).Select(x => x.Name));
    }

    public async Task<IActionResult> OnPostAsync(string? name, string? kind, string? national, string? economic, string? phone, string? address)
    {
        name = name?.Trim() ?? "";
        national = Persian.Latin(national);
        economic = Persian.Latin(economic);
        phone = Persian.Latin(phone);
        address = address?.Trim() ?? "";
        if (name.Length is < 1 or > 180 || kind is not ("person" or "company") ||
            national.Length > 30 || economic.Length > 40 || phone.Length > 40 || address.Length > 500)
        {
            Message = "اطلاعات فروشنده را بررسی کنید.";
            return RedirectToPage();
        }
        var result = await store.SaveSupplierAsync(name, kind, national, economic, phone, address);
        Message = result.Message;
        return RedirectToPage(new { id = result.Id });
    }

    public async Task<IActionResult> OnPostAccountAsync(long id, string? owner, string? bank, string? account, string? sheba)
    {
        owner = owner?.Trim() ?? "";
        bank = bank?.Trim() ?? "";
        account = Persian.Latin(account);
        sheba = Persian.Latin(sheba).Replace(" ", "").ToUpperInvariant();
        if (!sheba.StartsWith("IR", StringComparison.Ordinal)) sheba = "IR" + sheba;
        Message = owner.Length is < 1 or > 140 || bank.Length is < 1 or > 100 || account.Length > 50 ||
            sheba.Length != 26 || sheba[2..].Any(c => !char.IsAsciiDigit(c))
            ? "صاحب حساب، بانک و شبای معتبر را وارد کنید."
            : await store.SaveSupplierAccountAsync(id, owner, bank, account, sheba);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDocumentAsync(long id, string? kind, IFormFile? file)
    {
        var document = await DocumentUpload.ReadAsync(file);
        Message = kind is not ("account" or "taxpayer") || document is null
            ? "نوع مدرک و فایل PDF، JPG یا PNG تا ۱۰ مگابایت را انتخاب کنید."
            : await store.SaveSupplierDocumentAsync(id, kind, document, long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetDocumentAsync(long id, string? kind, bool preview)
    {
        if (kind is not ("account" or "taxpayer")) return NotFound();
        var file = await store.ReadSupplierDocumentAsync(id, kind);
        if (file is null) return NotFound();
        return preview ? File(file.Content, file.Type) : File(file.Content, file.Type, file.Name);
    }
}
