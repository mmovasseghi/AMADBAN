namespace Amadban.Data;

public sealed partial class Store
{
    private static readonly byte[] DemoPdf = "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF"u8.ToArray();

    public async Task<bool> IsDemoSeedAvailableAsync() => await DemoInvoiceCountAsync() < 3;

    public async Task<string> SeedPresentationAsync(long chiefId)
    {
        if (await DemoInvoiceCountAsync() >= 3)
            return "داده نمایشی قبلاً بارگذاری شده است.";

        const string demoPassword = "amadban12345678";
        if (await StaffIdByUsernameAsync("ofis") is null && !await AddStaffAsync("ofis", "افسرعامل نمایشی", "agent", demoPassword, chiefId))
            return "ساخت حساب افسرعامل ممکن نشد.";
        if (await StaffIdByUsernameAsync("anbardar") is null && !await AddStaffAsync("anbardar", "انباردار نمایشی", "warehouse", demoPassword, chiefId))
            return "ساخت حساب انباردار ممکن نشد.";
        if (await StaffIdByUsernameAsync("kardex1") is null && !await AddStaffAsync("kardex1", "مسئول کارتکس نمایشی", "kardex", demoPassword, chiefId))
            return "ساخت حساب کارتکس ممکن نشد.";

        var agent = (await StaffIdByUsernameAsync("ofis"))!.Value;
        var warehouse = (await StaffIdByUsernameAsync("anbardar"))!.Value;
        var kardex = (await StaffIdByUsernameAsync("kardex1"))!.Value;

        if (await CreditIdByCodeAsync("1404-53") is null)
        {
            await AddCreditCodeAsync("1404-53", "اعتبار ماده ۵۳ بیمارستان", chiefId, "chief");
            await AddCreditCodeAsync("1404-01", "اعتبار مصرفی جاری", chiefId, "chief");
            var credit53 = (await CreditIdByCodeAsync("1404-53"))!.Value;
            await ChangeCreditAsync(0, credit53, 5_000_000_000, chiefId, "chief");
        }

        if (await SupplierByNameAsync("شرکت تجهیزات پزشکی سینا") is null)
        {
            await SaveSupplierAsync("شرکت تجهیزات پزشکی سینا", "company", "10123456789", "41123456789",
                "02188776655", "تهران، خیابان ولیعصر", chiefId);
            var supplier = (await SupplierByNameAsync("شرکت تجهیزات پزشکی سینا"))!.Id;
            await SaveSupplierAccountAsync(supplier, "شرکت تجهیزات پزشکی سینا", "ملی", "0123456789012", "IR120170000000123456789012", chiefId);
        }

        if (await DepartmentIdByNameAsync("اورژانس") is null) await AddDepartmentAsync("اورژانس", chiefId);
        if (await DepartmentIdByNameAsync("آی‌سی‌یو") is null) await AddDepartmentAsync("آی‌سی‌یو", chiefId);
        if (await DepartmentIdByNameAsync("اتاق عمل") is null) await AddDepartmentAsync("اتاق عمل", chiefId);
        var er = (await DepartmentIdByNameAsync("اورژانس"))!.Value;
        var icu = (await DepartmentIdByNameAsync("آی‌سی‌یو"))!.Value;
        if (await PersonIdInDepartmentAsync(er, "علی") is null)
            await AddPersonAsync("علی رضایی", "مسئول بخش", "کارشناس", er, chiefId);
        if (await PersonIdInDepartmentAsync(icu, "مریم") is null)
            await AddPersonAsync("مریم احمدی", "سرپرستار", "کارمند ۱۵", icu, chiefId);
        await AddPersonAsync("حسین کریمی", "رئیس آماد", "سرهنگ", 0, chiefId);

        await SaveSignerAsync("chief", "سرهنگ حسین کریمی · رئیس آماد", chiefId);
        await SaveSignerAsync("agent", "افسرعامل نمایشی", chiefId);
        await SaveSignerAsync("warehouse", "انباردار نمایشی", chiefId);
        await SaveSignerAsync("finance", "مسئول مالی نمونه", chiefId);

        if (await ProductIdByCodeAsync("P-1001") is null) await NewProductAsync("P-1001", "دستکش لاتکس بدون پودر", "بسته", 20, warehouse);
        if (await ProductIdByCodeAsync("P-1002") is null) await NewProductAsync("P-1002", "سرنگ ۱۰ سی‌سی", "عدد", 50, warehouse);
        if (await ProductIdByCodeAsync("P-1003") is null) await NewProductAsync("P-1003", "پوشک یکبارمصرف بزرگ", "بسته", 10, warehouse);
        var glove = (await ProductIdByCodeAsync("P-1001"))!.Value;
        var syringe = (await ProductIdByCodeAsync("P-1002"))!.Value;
        var diaper = (await ProductIdByCodeAsync("P-1003"))!.Value;
        var day = Persian.Today;
        if (await DemoInvoiceCountAsync() == 0)
        {
            await MoveStockAsync(Guid.NewGuid(), glove, 0, "opening", 120, "", day, warehouse);
            await MoveStockAsync(Guid.NewGuid(), syringe, 0, "opening", 400, "", day, warehouse);
            await MoveStockAsync(Guid.NewGuid(), diaper, 0, "opening", 80, "", day, warehouse);
        }

        static InvoiceLine L(string title, string unit, decimal qty, long price) => new(title, unit, qty, price, (long)(qty * price));

        if (await InvoiceByNumberAsync("1404/201") is not null)
            return "داده نمایشی قبلاً بارگذاری شده است.";

        var invNew = await AddInvoiceAsync(new(Guid.NewGuid(), "1404/201", "شرکت تجهیزات پزشکی سینا", "خرید لوازم مصرفی",
            day.AddDays(-5), "credit", false, false, false, false,
            [L("ماسک سه‌لایه", "بسته", 20, 850_000), L("الکل طبی ۷۰٪", "گالن", 5, 2_400_000)], 0), agent);

        var invKardex = await AddInvoiceAsync(new(Guid.NewGuid(), "1404/198", "شرکت تجهیزات پزشکی سینا", "تجهیزات بخش اورژانس",
            day.AddDays(-8), "credit", true, false, false, false,
            [L("دستکش لاتکس", "بسته", 30, 620_000), L("سرنگ ۱۰ سی‌سی", "عدد", 100, 85_000)], 0), agent);
        await IssueDocumentAsync(invKardex, chiefId, "chief");

        var invCashFlow = await AddInvoiceAsync(new(Guid.NewGuid(), "1404/205", "شرکت تجهیزات پزشکی سینا", "خرید فوری اتاق عمل",
            day.AddDays(-2), "cash", false, false, false, false,
            [L("نخ بخیه ۲/۰", "عدد", 12, 180_000)], 0), agent);
        await IssueDocumentAsync(invCashFlow, agent, "agent");
        await AddAttachmentAsync(invCashFlow, agent, "invoice", "invoice-demo.pdf", "application/pdf", DemoPdf);
        await SetPosInInvoiceAsync(invCashFlow, true, agent);
        await UpdateFinanceAsync(invCashFlow, chiefId, "chief", "deliver");
        await UpdateFinanceAsync(invCashFlow, chiefId, "chief", "send-deputy");
        await UpdateFinanceAsync(invCashFlow, chiefId, "chief", "close");

        var invReady = await AddInvoiceAsync(new(Guid.NewGuid(), "1404/210", "شرکت تجهیزات پزشکی سینا", "ملزومات آی‌سی‌یو",
            day.AddDays(-1), "cash", false, false, false, false,
            [L("لوله اکسیژن", "عدد", 4, 3_200_000)], 0), agent);
        await IssueDocumentAsync(invReady, agent, "agent");
        await AddAttachmentAsync(invReady, agent, "invoice", "invoice-210.pdf", "application/pdf", DemoPdf);
        await SetPosInInvoiceAsync(invReady, true, agent);

        var invWarehouseDone = await AddInvoiceAsync(new(Guid.NewGuid(), "1404/190", "شرکت تجهیزات پزشکی سینا", "انبار پوشک و سرنگ",
            day.AddDays(-12), "credit", true, false, false, false,
            [L("پوشک یکبارمصرف", "بسته", 25, 910_000)], 0), agent);
        await IssueDocumentAsync(invWarehouseDone, chiefId, "chief");
        await SaveKardexAsync(invWarehouseDone, kardex, "ک-۱۴۰۴/۹۰", day.AddDays(-10));
        await ReceiveInvoiceAsync(invWarehouseDone, day.AddDays(-9), ["P-1003"], warehouse);

        await CreateDispatchAsync(Guid.NewGuid(), er, (await PersonIdInDepartmentAsync(er, "علی"))!.Value, warehouse,
            day.AddDays(-3), "تحویل عادی بخش اورژانس",
            [new("P-1001", 15), new("P-1002", 40)], warehouse);

        await CreatePettyAdvanceAsync(Guid.NewGuid(), 1001, day.AddDays(-4), 25_000_000,
            "افسرعامل نمایشی", "ملی", "IR120170000000123456789012", "خرید فوری مصرفی", agent);

        return """
            سناریوی نمایشی بارگذاری شد.
            حساب‌ها (رمز همه: amadban12345678): ofis (افسرعامل)، anbardar (انباردار)، kardex1 (کارتکس).
            فاکتور ۱۴۰۴/۲۰۱ در کارتابل باز است؛ ۱۴۰۴/۱۹۸ در صف کارتکس؛ ۱۴۰۴/۲۱۰ آماده تحویل؛ ۱۴۰۴/۱۹۰ دریافت‌شده در انبار؛ یک حواله و یک تنخواه نمونه ثبت شد.
            """;
    }

    private async Task<long> DemoInvoiceCountAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        return (long)(await Scalar(conn, null, "SELECT count(*) FROM invoices") ?? 0L);
    }

    private async Task<long?> StaffIdByUsernameAsync(string username)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null, "SELECT id FROM staff WHERE username=$1", username) as long?;
    }

    private async Task<long?> CreditIdByCodeAsync(string code)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null, "SELECT id FROM credit_codes WHERE code=$1", code) as long?;
    }

    private async Task<long?> DepartmentIdByNameAsync(string name)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null, "SELECT id FROM departments WHERE name=$1 AND active", name) as long?;
    }

    private async Task<long?> ProductIdByCodeAsync(string code)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null, "SELECT id FROM products WHERE code=$1", code) as long?;
    }

    private async Task<long?> InvoiceByNumberAsync(string number)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null, "SELECT id FROM invoices WHERE number=$1 LIMIT 1", number) as long?;
    }

    private async Task<long?> PersonIdInDepartmentAsync(long department, string namePart)
    {
        await using var conn = await source.OpenConnectionAsync();
        return await Scalar(conn, null, """
            SELECT id FROM people WHERE department_id=$1 AND active AND strpos(name,$2)>0 LIMIT 1
            """, department, namePart) as long?;
    }
}