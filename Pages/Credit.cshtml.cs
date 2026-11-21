using System.Security.Claims;
using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles = "chief,agent")]
public sealed class CreditModel(Store store) : PageModel
{
    public List<CreditBalance> Codes { get; private set; } = [];
    public List<CreditChange> History { get; private set; } = [];
    public List<string> Notices { get; private set; } = [];
    public List<Prepayment> Proformas { get; private set; } = [];
    public sealed record PendingPayment(string Proforma, string Vendor, long Code, string Owner, string Bank,
        string Sheba, string? Amount, string Permit, string PermitDate, string? Tax);
    public PendingPayment? Pending { get; private set; }
    public List<SupplierAccount> Accounts { get; private set; } = [];
    public InvoiceDetail? Invoice { get; private set; }
    public Prepayment? Payment { get; private set; }
    public bool IsAgent => User.IsInRole("agent");
    public bool CanPrepay => User.IsInRole("agent") || User.IsInRole("chief");
    public string? InvoicePrepayNotice
    {
        get
        {
            if (Invoice is null || Payment is not null) return null;
            return Invoice.Summary.Kind != "credit" ? "این پرونده نقدی است. پیش‌پرداخت اعتباری فقط برای فاکتور چکی است."
                : Invoice.Summary.Document is null ? "برای پیش‌پرداخت روی سند، ابتدا در همان پرونده شماره سند صادر شود."
                : Invoice.Summary.Paid ? "این سند قبلاً پرداخت‌شده است."
                : null;
        }
    }
    public bool ShowProformaForm => CanPrepay && Invoice is null && Payment is null;
    public bool ShowInvoiceForm => CanPrepay && Invoice is not null && Payment is null && InvoicePrepayNotice is null;
    public Guid Submission { get; } = Guid.NewGuid();
    private long Actor => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Role => User.FindFirstValue(ClaimTypes.Role)!;
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync(long invoice, long number)
    {
        Codes = await store.CreditBalancesAsync();
        if (invoice > 0)
        {
            Invoice = await store.DetailAsync(invoice);
            Payment = await store.ReadPrepaymentAsync(invoice, true);
            if (Payment is null && Invoice is not null && await store.SupplierByNameAsync(Invoice.Summary.Vendor) is { } supplier)
                Accounts = await store.SupplierAccountsAsync(supplier.Id);
        }
        else if (number > 0)
        {
            Payment = await store.ReadPrepaymentAsync(number, false);
        }
        else
        {
            History = await store.CreditHistoryAsync();
            Proformas = await store.OpenProformasAsync();
            if (!IsAgent) Notices = await store.NoticesAsync();
        }
    }

    public async Task<IActionResult> OnPostAddAsync(string? code, string? title)
    {
        code = Persian.Latin(code);
        title = title?.Trim() ?? "";
        Message = code.Length is < 1 or > 40 || title.Length is < 1 or > 150
            ? "شماره و عنوان کد را وارد کنید."
            : await store.AddCreditCodeAsync(code, title, Actor, Role);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAllocateAsync(long from, long to, string? amount)
    {
        if (from == 0 && !User.IsInRole("chief")) return Forbid();
        Message = ParseAmount(amount) is long value && from >= 0 && to > 0
            ? await store.ChangeCreditAsync(from, to, value, Actor, Role)
            : "کد و مبلغ معتبر وارد کنید.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostReserveAsync(Guid submission, long invoice, string? proforma, string? vendor,
        long code, string? owner, string? bank, string? sheba, string? amount, string? permit, string? permitDate, string? tax)
    {
        if (!CanPrepay) return Forbid();
        proforma = Persian.Latin(proforma);
        vendor = vendor?.Trim() ?? "";
        owner = owner?.Trim() ?? "";
        bank = bank?.Trim() ?? "";
        sheba = Persian.Latin(sheba).Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        permit = Persian.Latin(permit)?.Trim() ?? "";
        permitDate = Persian.Latin(permitDate)?.Trim() ?? "";
        var taxAmount = string.IsNullOrWhiteSpace(tax) ? 0 : ParseAmount(tax) ?? -1;
        if (!sheba.StartsWith("IR", StringComparison.Ordinal)) sheba = "IR" + sheba;
        if (submission == Guid.Empty || invoice < 0 || (invoice == 0 && (proforma.Length is < 1 or > 80 || vendor.Length is < 1 or > 180)) ||
            permit.Length > 80 || (permitDate.Length > 0 && Persian.ParseDate(permitDate) is null) || taxAmount < 0 ||
            owner.Length is < 1 or > 140 || bank.Length is < 1 or > 100 ||
            sheba.Length != 26 || sheba[2..].Any(c => !char.IsAsciiDigit(c)) || ParseAmount(amount) is not long value || code <= 0)
            Message = "اطلاعات سند، کد اعتباری، مبلغ و شبای ۲۴ رقمی را بررسی کنید.";
        else
        {
            if (invoice > 0) proforma = "";
            var result = await store.ReserveAsync(submission, invoice, proforma, vendor, code, owner, bank, sheba, value, permit, permitDate, taxAmount, Actor);
            if (result.Number is long number)
            {
                Message = result.Message;
                return RedirectToPage("/PrintPrepayment", new { number });
            }
            Message = result.Message;
        }
        Pending = new(proforma, vendor, code, owner, bank, sheba, amount, permit, permitDate, tax);
        await OnGetAsync(invoice, 0);
        return Page();
    }

    public async Task<IActionResult> OnPostActionAsync(long invoice, long number, string? action, long code, bool proxy)
    {
        Message = await store.PrepaymentActionAsync(number, action ?? "", code, proxy, Actor, Role);
        return RedirectToPage(new { invoice, number });
    }


    private static long? ParseAmount(string? amount) =>
        long.TryParse(Persian.Latin(amount).Replace(",", "").Replace("٬", ""), out var value) &&
        value is > 0 and <= 1_000_000_000_000_000L ? value : null;
}
