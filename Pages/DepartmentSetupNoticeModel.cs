using Amadban.Data;

namespace Amadban.Pages;

public sealed class DepartmentSetupNoticeModel(IReadOnlyList<DepartmentRoster> roster, int departmentCount, bool compact)
{
    public IReadOnlyList<DepartmentRoster> Roster { get; } = roster;
    public int DepartmentCount { get; } = departmentCount;
    public bool Compact { get; } = compact;
}