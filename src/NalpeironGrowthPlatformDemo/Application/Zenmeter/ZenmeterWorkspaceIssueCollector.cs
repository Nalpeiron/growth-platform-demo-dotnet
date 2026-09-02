namespace NalpeironGrowthPlatformDemo.Application.Zenmeter;

internal sealed class ZenmeterWorkspaceIssueCollector
{
    private readonly List<string> _issues = [];

    public void Add(string issue)
    {
        if (!_issues.Contains(issue, StringComparer.Ordinal))
        {
            _issues.Add(issue);
        }
    }

    public IReadOnlyList<string> ToList() => _issues.ToList();
}
