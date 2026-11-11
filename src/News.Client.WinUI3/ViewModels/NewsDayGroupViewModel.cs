using System.Collections.ObjectModel;
using System.Globalization;

namespace News.Client.WinUI3.ViewModels;

public sealed partial class NewsDayGroupViewModel(DateOnly day, IEnumerable<NewsItemViewModel> items)
    : ObservableCollection<NewsItemViewModel>(items)
{
    public DateOnly Day { get; } = day;

    public string DayLabel => Day.ToString("dddd, MMM d", CultureInfo.CurrentCulture);
}
