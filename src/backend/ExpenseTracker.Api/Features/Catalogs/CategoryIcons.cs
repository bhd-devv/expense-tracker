using System.Text.Json;

namespace ExpenseTracker.Api.Features.Catalogs;

public sealed record CategoryIconDto(string Id, string Uk, string En, string Symbol);
public sealed record CategoryIconGroupDto(string Id, string Uk, string En, IReadOnlyList<CategoryIconDto> Icons);
public sealed record CategoryIconLibraryDto(IReadOnlyList<CategoryIconGroupDto> Groups);

public static class CategoryIcons
{
    public static CategoryIconLibraryDto Library { get; } = Load();
    private static readonly HashSet<string> Ids = Library.Groups.SelectMany(x => x.Icons).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);

    private static CategoryIconLibraryDto Load()
    {
        using var stream = typeof(CategoryIcons).Assembly.GetManifestResourceStream("CategoryIcons.json")
            ?? throw new InvalidOperationException("Category icon library is missing");
        return JsonSerializer.Deserialize<CategoryIconLibraryDto>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Category icon library is invalid");
    }

    public static bool Contains(string id) => Ids.Contains(id);

    public static string Default(string? code) => code switch
    {
        "food" => "utensils-crossed", "transport" => "car-front", "shopping" => "shopping-bag",
        "home" => "house", "health" => "heart-pulse", "leisure" => "ticket", "other" => "receipt-text",
        _ => "tags"
    };
}
