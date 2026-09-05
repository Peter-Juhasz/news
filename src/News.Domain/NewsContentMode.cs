using System.Text.Json.Serialization;

namespace News.Domain;

/// <summary>
/// Written down by name rather than by number, so a stored view reads the way the groups file
/// spells it rather than as a position in this list that reordering would quietly change.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NewsContentMode>))]
public enum NewsContentMode
{
    Text,
    Html,
    Web
}
