using System.Globalization;

namespace PictTag.Core.Xmp;

/// <summary>Builds the "Category&lt;separator&gt;Group&lt;separator&gt;Label" path segments the hierarchical-tag XMP properties use.</summary>
internal static class HierarchicalTagPath
{
    /// <summary>
    /// Title-cases free text (e.g. "angel" -> "Angel"). Lowercasing first avoids .NET's
    /// "leave ALL-CAPS words alone" acronym behavior in <see cref="TextInfo.ToTitleCase"/>.
    /// Only ever apply this to genuine natural-language text (entity label/group, ArtStyle) -
    /// never to enum-derived tokens like "ThreeDRender" or "DigitalIllustration", which have no
    /// word boundaries to title-case against and would have their internal capitalization
    /// collapsed ("ThreeDRender" -> "Threedrender").
    /// </summary>
    public static string TitleCase(string text) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());

    /// <summary>
    /// Builds the ordered path segments for an entity's Category &gt; Group &gt; Label tag,
    /// title-casing the free-text group/label. Collapses to a 2-level [category, label] when
    /// the title-cased group and label are equal (case-insensitive) - the model repeats the
    /// label when no more general group genuinely applies, and that shouldn't produce a
    /// redundant "X &gt; X" tag.
    /// </summary>
    public static string[] BuildSegments(string category, string group, string label)
    {
        string titleLabel = TitleCase(label);
        string titleGroup = TitleCase(group);
        return string.Equals(titleGroup, titleLabel, StringComparison.OrdinalIgnoreCase)
            ? [category, titleLabel]
            : [category, titleGroup, titleLabel];
    }

    // '|' (lr:HierarchicalSubject) and '/' (digiKam:TagsList) are both used as hierarchy
    // separators across the two properties these paths feed. A literal '|' or '/' inside a
    // segment (e.g. a model-generated label like "angel/religious figure") must never survive
    // into either, or the reader that uses that character as its separator splits the segment
    // into extra hierarchy levels the other property doesn't have - producing two divergent
    // tag trees for what should be the same tag (observed in digiKam).
    public static string Compose(char separator, IReadOnlyList<string> segments) => string.Join(separator, segments.Select(Sanitize));

    private static string Sanitize(string segment) => segment.Replace('/', '-').Replace('|', '-');
}
