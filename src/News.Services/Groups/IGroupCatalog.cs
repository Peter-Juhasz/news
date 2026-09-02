using News.Domain;

namespace News.Groups;

public interface IGroupCatalog
{
    event EventHandler<IReadOnlyList<NewsGroup>>? GroupsChanged;

    IReadOnlyList<NewsGroup> CurrentGroups { get; }

    Task InitializeAsync(CancellationToken cancellationToken);
}
