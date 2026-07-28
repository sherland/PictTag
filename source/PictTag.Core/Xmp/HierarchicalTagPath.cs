namespace PictTag.Core.Xmp;

/// <summary>Builds the "Category&lt;separator&gt;Label" path strings both hierarchical-tag XMP properties use.</summary>
internal static class HierarchicalTagPath
{
    public static string Compose(EntityCategory category, string label, char separator) => $"{category}{separator}{label}";

    public static string Compose(string category, string label, char separator) => $"{category}{separator}{label}";
}
