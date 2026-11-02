using ExpenseTracker.Api.Features.Catalogs;
using ExpenseTracker.Api.Features.Users;
using Xunit;

namespace ExpenseTracker.Tests;

public sealed class CategoryIconTests
{
    [Fact]
    public void IconLibraryHasUniqueStableIdsAndBothLanguagesAndSupportsAllDefaultCategories()
    {
        var groups = CategoryIcons.Library.Groups;
        var icons = groups.SelectMany(group => group.Icons).ToArray();
        Assert.Equal(10, groups.Count);
        Assert.Equal(100, icons.Length);
        Assert.Equal(icons.Length, icons.Select(icon => icon.Id).Distinct().Count());
        Assert.All(groups, group =>
        {
            Assert.False(string.IsNullOrWhiteSpace(group.Uk));
            Assert.False(string.IsNullOrWhiteSpace(group.En));
            Assert.InRange(group.Icons.Count, 1, 20);
        });
        Assert.All(icons, icon =>
        {
            Assert.Matches("^[a-z][a-z0-9-]{0,30}$", icon.Id);
            Assert.False(string.IsNullOrWhiteSpace(icon.Uk));
            Assert.False(string.IsNullOrWhiteSpace(icon.En));
            Assert.False(string.IsNullOrWhiteSpace(icon.Symbol));
            Assert.True(CategoryIcons.Contains(icon.Id));
        });
        Assert.All(UserService.Categories, category => Assert.True(CategoryIcons.Contains(CategoryIcons.Default(category.Code))));
        Assert.True(CategoryIcons.Contains(CategoryIcons.Default(null)));
        Assert.False(CategoryIcons.Contains("<svg onload=alert(1)>"));
        Assert.False(CategoryIcons.Contains("https://example.com/icon.svg"));
    }
}
