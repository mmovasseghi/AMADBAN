using Npgsql;

namespace Amadban.Data;

public sealed record Department(long Id, string Name);
public sealed record DepartmentRoster(long Id, string Name, IReadOnlyList<Person> Recipients);
public sealed record Person(long Id, string Name, string Position, string Rank, long? DepartmentId, string? Department);
public sealed record DispatchLineInput(string Code, decimal Quantity);
public sealed record DispatchLine(string Code, string Name, string Unit, decimal Quantity, decimal Returned=0)
{
    public decimal Remaining => Quantity-Returned;
}
public sealed record DispatchRecord(long Id, long Number, string Department, string Recipient, string RecipientPosition,
    string Warehouse, DateOnly Date, string Note, IReadOnlyList<DispatchLine> Lines, string State="active");

public sealed partial class Store
{
    public async Task<List<Department>> DepartmentsAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, "SELECT id,name FROM departments WHERE active ORDER BY name LIMIT 300");
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<Department>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    public async Task<List<DepartmentRoster>> DepartmentRosterAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT d.id,d.name,p.id,p.name,p.position,p.rank,p.department_id,d.name
            FROM departments d
            LEFT JOIN people p ON p.department_id=d.id AND p.active
            WHERE d.active
            ORDER BY d.name,p.name NULLS LAST
            """);
        await using var reader = await cmd.ExecuteReaderAsync();
        var map = new Dictionary<long, DepartmentRoster>();
        while (await reader.ReadAsync())
        {
            var departmentId = reader.GetInt64(0);
            var departmentName = reader.GetString(1);
            if (!map.TryGetValue(departmentId, out var roster))
            {
                roster = new(departmentId, departmentName, []);
                map[departmentId] = roster;
            }
            if (reader.IsDBNull(2)) continue;
            var people = roster.Recipients.ToList();
            people.Add(new(reader.GetInt64(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetInt64(6), reader.GetString(7)));
            map[departmentId] = roster with { Recipients = people };
        }
        return map.Values.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
    }

    public async Task<List<Person>> PeopleAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT p.id,p.name,p.position,p.rank,p.department_id,d.name
            FROM people p LEFT JOIN departments d ON d.id=p.department_id
            WHERE p.active ORDER BY d.name NULLS FIRST,p.name LIMIT 500
            """);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<Person>();
            while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return result;
    }

    public async Task<List<(long Id, string Name)>> WarehouseStaffAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null,
            "SELECT id,display_name FROM staff WHERE role='warehouse' AND active ORDER BY display_name LIMIT 100");
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<(long, string)>();
        while (await reader.ReadAsync()) result.Add((reader.GetInt64(0), reader.GetString(1)));
        return result;
    }

    public async Task<string> AddDepartmentAsync(string name, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var id = await Scalar(conn, tx,
            "INSERT INTO departments(name) VALUES($1) ON CONFLICT DO NOTHING RETURNING id", name);
        if (id is not long) return "این بخش قبلاً ثبت شده است.";
        await Activity(conn, tx, actor, "بخش بیمارستان تعریف شد", name, "organization");
        await tx.CommitAsync();
        return "بخش ثبت شد.";
    }

    public async Task<string> AddPersonAsync(string name, string position, string rank, long department, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        if (department > 0 && await Scalar(conn, tx,
            "SELECT id FROM departments WHERE id=$1 AND active", department) is null) return "بخش معتبر را انتخاب کنید.";
        await Exec(conn, tx, "INSERT INTO people(name,position,rank,department_id) VALUES($1,$2,$3,NULLIF($4,0))",
            name, position, rank, department);
        await Activity(conn, tx, actor, department > 0 ? "تحویل‌گیرنده بخش تعریف شد" : "شخص امضاکننده تعریف شد",
            (rank.Length > 0 ? rank + " " : "") + name + (position.Length > 0 ? " · " + position : ""), "organization");
        await tx.CommitAsync();
        return "شخص ثبت شد و برای فرم‌های چاپی قابل انتخاب است.";
    }

    public async Task<string> CorrectPersonAsync(long id,string name,string position,string rank,long department,long actor)
    {
        await using var conn=await source.OpenConnectionAsync();
        await using var tx=await conn.BeginTransactionAsync();
        if(department>0 && await Scalar(conn,tx,"SELECT id FROM departments WHERE id=$1 AND active",department) is null)
            return "بخش معتبر را انتخاب کنید.";
        if(await Exec(conn,tx,"""
            UPDATE people SET name=$2,position=$3,rank=$4,department_id=NULLIF($5,0)
            WHERE id=$1 AND active
            """,id,name,position,rank,department)!=1)return "شخص فعال پیدا نشد.";
        await Activity(conn,tx,actor,"مشخصات شخص اصلاح شد",$"{rank} {name} · شناسه {id}","organization");
        await tx.CommitAsync();
        return "مشخصات شخص اصلاح شد.";
    }

    public async Task<List<Person>> MeetingPeopleAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT p.id,p.name,p.position,p.rank,p.department_id,d.name
            FROM people p LEFT JOIN departments d ON d.id=p.department_id
            WHERE p.active ORDER BY p.name LIMIT 150
            """);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<Person>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        return result;
    }

    public async Task<List<Person>> RecipientsByDepartmentAsync(long department)
    {
        if (department <= 0) return [];
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT p.id,p.name,p.position,p.rank,p.department_id,d.name
            FROM people p JOIN departments d ON d.id=p.department_id
            WHERE p.active AND p.department_id=$1
            ORDER BY p.name LIMIT 200
            """, department);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<Person>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetInt64(4), reader.GetString(5)));
        return result;
    }

    public async Task<List<Product>> ProductHintsAsync(string search)
    {
        if (search.Length is < 2 or > 80) return [];
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT id,code,name,unit,stock,minimum FROM products
            WHERE strpos(lower(code || ' ' || name),lower($1))>0
            ORDER BY name,id LIMIT 15
            """, search);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<Product>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),
            reader.GetString(3),reader.GetDecimal(4),reader.GetDecimal(5)));
        return result;
    }

    public async Task<(long? Number, string Message)> CreateDispatchAsync(Guid submission, long department,
        long recipient, long warehouse, DateOnly date, string note, IReadOnlyList<DispatchLineInput> lines, long actor)
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Exec(conn, tx, "SELECT pg_advisory_xact_lock(782542000)");
        var previous = await Scalar(conn, tx,
            "SELECT number FROM stock_issues WHERE submission_id=$1 AND actor_id=$2", submission, actor);
        if (previous is long known) return (known, $"برگه خروج شماره {known} قبلاً ثبت شده است.");
        if (await Scalar(conn, tx, "SELECT id FROM departments WHERE id=$1 AND active", department) is null ||
            await Scalar(conn, tx, "SELECT id FROM people WHERE id=$1 AND department_id=$2 AND active", recipient, department) is null)
            return (null, "بخش و تحویل‌گیرنده همان بخش را انتخاب کنید.");
        if (await Scalar(conn, tx, "SELECT id FROM staff WHERE id=$1 AND role='warehouse' AND active", warehouse) is null)
            return (null, "نام انباردار را انتخاب کنید.");
        if (lines.Count == 0 || lines.Count > 30 || lines.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != lines.Count)
            return (null, "کالاهای برگه را بدون تکرار وارد کنید؛ حداکثر ۳۰ قلم در هر برگه.");
        var products = new Dictionary<string, (long Id, decimal Stock)>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
        {
            await using var cmd = Command(conn, tx, "SELECT id,stock FROM products WHERE code=$1 FOR UPDATE", line.Code);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return (null, $"کالای {line.Code} پیدا نشد.");
            var stock = reader.GetDecimal(1);
            if (stock < line.Quantity) return (null, $"موجودی کالای {line.Code} کافی نیست؛ مانده {stock:0.###} است.");
            products.Add(line.Code, (reader.GetInt64(0), stock));
        }
        var number = (long)(await Scalar(conn, tx,
            "UPDATE counters SET last_value=last_value+1 WHERE name='stock-issue' RETURNING last_value"))!;
        var issueId = (long)(await Scalar(conn, tx, """
            INSERT INTO stock_issues(submission_id,number,department_id,recipient_id,warehouse_person_id,actor_id,issue_date,note)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8) RETURNING id
            """, submission, number, department, recipient, warehouse, actor, date, note))!;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var product = products[line.Code];
            await Exec(conn, tx, "UPDATE products SET stock=stock-$2 WHERE id=$1", product.Id, line.Quantity);
            await Exec(conn, tx, """
                INSERT INTO stock_issue_lines(issue_id,position,product_id,quantity,stock_before)
                VALUES($1,$2,$3,$4,$5)
                """, issueId, index+1, product.Id, line.Quantity, product.Stock);
            await Exec(conn, tx, """
                INSERT INTO stock_events(submission_id,product_id,kind,delta,department,event_date,actor_id,before_quantity,issue_id)
                VALUES($1,$2,'out',$3,(SELECT name FROM departments WHERE id=$4),$5,$6,$7,$8)
                """, Guid.NewGuid(), product.Id, -line.Quantity, department, date, actor, product.Stock, issueId);
        }
        await Activity(conn,tx,actor,"حواله خروج انبار ثبت شد",$"حواله {number} · {lines.Count} قلم · بخش {department}","dispatch",number);
        await tx.CommitAsync();
        return (number, $"برگه خروج {number} برای {lines.Count} قلم ثبت شد.");
    }

    public async Task<DispatchRecord?> DispatchAsync(long number)
    {
        await using var conn = await source.OpenConnectionAsync();
        long id;
        string department, recipient, position, warehouse, note, state;
        DateOnly date;
        await using (var cmd = Command(conn, null, """
            SELECT i.id,d.name,concat_ws(' ',NULLIF(p.rank,''),p.name),p.position,s.display_name,i.issue_date,i.note,i.state
            FROM stock_issues i JOIN departments d ON d.id=i.department_id
            JOIN people p ON p.id=i.recipient_id JOIN staff s ON s.id=i.warehouse_person_id
            WHERE i.number=$1
            """, number))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) return null;
            id=reader.GetInt64(0);department=reader.GetString(1);recipient=reader.GetString(2);
            position=reader.GetString(3);warehouse=reader.GetString(4);
            date=reader.GetFieldValue<DateOnly>(5);note=reader.GetString(6);state=reader.GetString(7);
        }
        var lines = new List<DispatchLine>();
        await using (var cmd = Command(conn, null, """
            SELECT p.code,p.name,p.unit,l.quantity,
              coalesce((SELECT sum(r.quantity) FROM stock_issue_returns r WHERE r.issue_id=l.issue_id AND r.position=l.position),0)
            FROM stock_issue_lines l JOIN products p ON p.id=l.product_id
            WHERE l.issue_id=$1 ORDER BY l.position
            """, id))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) lines.Add(new(reader.GetString(0),reader.GetString(1),
                reader.GetString(2),reader.GetDecimal(3),reader.GetDecimal(4)));
        }
        return new(id,number,department,recipient,position,warehouse,date,note,lines,state);
    }

    public async Task<List<DispatchRecord>> RecentDispatchesAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        await using var cmd = Command(conn, null, """
            SELECT i.id,i.number,d.name,concat_ws(' ',NULLIF(p.rank,''),p.name),p.position,s.display_name,i.issue_date,i.note,i.state
            FROM stock_issues i JOIN departments d ON d.id=i.department_id
            JOIN people p ON p.id=i.recipient_id JOIN staff s ON s.id=i.warehouse_person_id
            ORDER BY i.id DESC LIMIT 25
            """);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<DispatchRecord>();
        while (await reader.ReadAsync()) result.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),
            reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetFieldValue<DateOnly>(6),
                reader.GetString(7),[],reader.GetString(8)));
        return result;
    }
}
