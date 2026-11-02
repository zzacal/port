using System.Reflection;
using Gallery.Core.Design;

namespace Gallery.Core.Tests;

/// <summary>011 T004 (contracts/components.md C1): the display type size for the home links.</summary>
public class TypeDisplayTests
{
    [Fact]
    public void ToCssRoot_emits_type_display_right_after_type_brand()
    {
        Assert.Contains("--type-brand:16px;--type-display:48px;", Tokens.ToCssRoot());
    }

    [Fact]
    public void All_lists_TypeDisplay_after_TypeBrand()
    {
        var all = Tokens.All().ToList();
        var brand = all.FindIndex(t => t.Name == "TypeBrand");
        Assert.True(brand >= 0);
        Assert.Equal(("Typography", "TypeDisplay", "48px"), all[brand + 1]);
    }

    [Fact]
    public void TypeDisplay_is_the_strict_maximum_type_size()
    {
        var sizes = typeof(Tokens).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int) && f.Name.StartsWith("Type", StringComparison.Ordinal))
            .ToDictionary(f => f.Name, f => (int)f.GetRawConstantValue()!);
        Assert.Contains("TypeDisplay", sizes.Keys);
        Assert.True(sizes.Count >= 4, string.Join(",", sizes.Keys));
        Assert.All(sizes.Where(s => s.Key != "TypeDisplay"), s => Assert.True(s.Value < Tokens.TypeDisplay, $"{s.Key}={s.Value}"));
    }
}
