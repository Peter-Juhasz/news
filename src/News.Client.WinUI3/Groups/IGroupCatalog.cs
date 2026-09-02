using News.Domain;

namespace News.Client.WinUI3.Groups;

public interface IGroupCatalog
{
    event EventHandler<IReadOnlyList<NewsGroup>>? GroupsChanged;

    IReadOnlyList<NewsGroup> CurrentGroups { get; }

    Task InitializeAsync(CancellationToken cancellationToken);
}
