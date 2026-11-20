namespace Amadban.Data;

public sealed record Supplier(long Id, string Name, string Kind, string National, string Economic, string Phone, string Address);
public sealed record SupplierAccount(long Id, string Owner, string Bank, string Account, string Sheba);

public sealed partial class Store
{
    private const string SupplierColumns = "id,name,kind,national_code,economic_code,phone,address";

    public async Task<List<Supplier>> SuppliersAsync(string search, int page)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT " + SupplierColumns + " " + """
             FROM suppliers WHERE ($1='' OR strpos(lower(name),lower($1))>0) ORDER BY name,id LIMIT 26 OFFSET $2
             """, search, (page - 1) * 25);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Supplier>();
        while (await r.ReadAsync()) list.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6)));
        return list;
    }

    public async Task<Supplier?> SupplierAsync(long id)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT " + SupplierColumns + " FROM suppliers WHERE id=$1", id);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6)) : null;
    }

    public async Task<Supplier?> SupplierByNameAsync(string name)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT " + SupplierColumns + " FROM suppliers WHERE name=$1", name);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6)) : null;
    }

    public async Task<(long Id, string Message)> SaveSupplierAsync(string name, string kind, string national, string economic, string phone, string address, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var id = (long)(await Scalar(conn, tx, """
            INSERT INTO suppliers(name,kind,national_code,economic_code,phone,address) VALUES($1,$2,$3,$4,$5,$6)
            ON CONFLICT(name) DO UPDATE SET kind=excluded.kind,national_code=excluded.national_code,
                economic_code=excluded.economic_code,phone=excluded.phone,address=excluded.address
            RETURNING id
            """, name, kind, national, economic, phone, address))!;
        await Activity(conn, tx, actor, "اطلاعات فروشنده ثبت شد", name, "supplier", id);
        await tx.CommitAsync();
        return (id, "اطلاعات فروشنده ثبت شد.");
    }

    public async Task<List<SupplierAccount>> SupplierAccountsAsync(long id)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT id,owner,bank,account,sheba FROM supplier_accounts WHERE supplier_id=$1 ORDER BY id LIMIT 30", id);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<SupplierAccount>();
        while (await r.ReadAsync()) list.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }

    public async Task<string> SaveSupplierAccountAsync(long id, string owner, string bank, string account, string sheba, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var updated = await Exec(conn, tx, """
            INSERT INTO supplier_accounts(supplier_id,owner,bank,account,sheba) SELECT id,$2,$3,$4,$5 FROM suppliers WHERE id=$1
            ON CONFLICT(supplier_id,sheba) DO UPDATE SET owner=excluded.owner,bank=excluded.bank,account=excluded.account
            """, id, owner, bank, account, sheba);
        if (updated == 1)
        {
            await Activity(conn, tx, actor, "حساب بانکی فروشنده ثبت شد", owner + " · " + bank, "supplier", id);
            await tx.CommitAsync();
        }
        return updated == 1 ? "حساب فروشنده ثبت شد." : "فروشنده پیدا نشد.";
    }

    public async Task<string> SaveSupplierDocumentAsync(long id, string kind, DocumentUpload document, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        var written = await Exec(conn, tx, """
            INSERT INTO supplier_documents(supplier_id,kind,filename,content_type,content,actor_id)
            SELECT id,$2,$3,$4,$5,$6 FROM suppliers WHERE id=$1
            ON CONFLICT(supplier_id,kind) DO UPDATE SET filename=excluded.filename,content_type=excluded.content_type,
                content=excluded.content,actor_id=excluded.actor_id,uploaded_at=now()
            """, id, kind, document.Name, document.Type, document.Content, actor);
        if(written==1)
        {
            await Activity(conn,tx,actor,"مدرک فروشنده ثبت شد",$"فروشنده {id} · {(kind=="account"?"تأییدیه حساب":"مودیان")}","supplier",id);
            await tx.CommitAsync();
        }
        return written == 1 ? "مدرک فروشنده ثبت شد." : "فروشنده پیدا نشد.";
    }

    public async Task<List<Attachment>> SupplierDocumentsAsync(long id)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT kind,filename FROM supplier_documents WHERE supplier_id=$1", id);
        await using var r = await cmd.ExecuteReaderAsync();
        var attachments = new List<Attachment>();
        while (await r.ReadAsync()) attachments.Add(new(id, r.GetString(0), r.GetString(1)));
        return attachments;
    }

    public async Task<DocumentUpload?> ReadSupplierDocumentAsync(long id, string kind)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT filename,content_type,content FROM supplier_documents WHERE supplier_id=$1 AND kind=$2", id, kind);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new(r.GetString(0), r.GetString(1), r.GetFieldValue<byte[]>(2)) : null;
    }

    public async Task<bool> CopySupplierDocumentAsync(long invoice, string kind, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (await Scalar(conn, tx, "SELECT id FROM invoices WHERE id=$1 AND delivery_state IN ('open','returned') FOR UPDATE", invoice) is null) return false;
        if (await Exec(conn, tx, """
            INSERT INTO invoice_attachments(invoice_id,kind,filename,content_type,content,uploaded_by)
            SELECT i.id,f.kind,f.filename,f.content_type,f.content,$3
            FROM invoices i JOIN suppliers s ON s.name=i.vendor JOIN supplier_documents f ON f.supplier_id=s.id
            WHERE i.id=$1 AND f.kind=$2 AND NOT EXISTS (
                SELECT 1 FROM invoice_attachments a WHERE a.invoice_id=i.id AND a.kind=f.kind
            )
            """, invoice, kind, actor) != 1) return false;
        await Event(conn, tx, invoice, actor, "پیوست از پرونده فروشنده");
        await tx.CommitAsync();
        return true;
    }
}
