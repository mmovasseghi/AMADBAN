using Npgsql;

namespace Amadban.Data;

public sealed record SetupHints(int Departments, int Recipients, int Suppliers, int DepartmentsWithoutRecipients);

public sealed partial class Store
{
    public async Task<SetupHints> SetupHintsAsync()
    {
        await using var conn = await source.OpenConnectionAsync();
        var departments = Convert.ToInt32(await Scalar(conn, null, "SELECT count(*)::int FROM departments WHERE active") ?? 0);
        var recipients = Convert.ToInt32(await Scalar(conn, null,
            "SELECT count(*)::int FROM people WHERE active AND department_id IS NOT NULL") ?? 0);
        var suppliers = Convert.ToInt32(await Scalar(conn, null, "SELECT count(*)::int FROM suppliers") ?? 0);
        var empty = Convert.ToInt32(await Scalar(conn, null, """
            SELECT count(*)::int FROM departments d
            WHERE d.active AND NOT EXISTS (
                SELECT 1 FROM people p WHERE p.active AND p.department_id=d.id)
            """) ?? 0);
        return new(departments, recipients, suppliers, empty);
    }
}