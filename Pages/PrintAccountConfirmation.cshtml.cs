using Amadban.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Amadban.Pages;

[Authorize(Roles="agent,chief")]
public sealed class PrintAccountConfirmationModel(Store store):PageModel
{
    public InvoiceDetail Invoice { get; private set; }=null!;
    public Supplier Supplier { get; private set; }=null!;
    public SupplierAccount Account { get; private set; }=null!;
    public async Task<IActionResult> OnGetAsync(long invoice,long account)
    {
        var found=await store.DetailAsync(invoice);
        if(found is null || !found.Account)return NotFound();
        var supplier=await store.SupplierByNameAsync(found.Summary.Vendor);
        if(supplier is null)return NotFound();
        var bank=(await store.SupplierAccountsAsync(supplier.Id)).FirstOrDefault(x=>x.Id==account);
        if(bank is null)return NotFound();
        Invoice=found;Supplier=supplier;Account=bank;
        return Page();
    }
}
