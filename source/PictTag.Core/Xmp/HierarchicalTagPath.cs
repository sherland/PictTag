using System.Globalization;

namespace PictTag.Core.Xmp;

/// <summary>Builds the ordered path segments the hierarchical-tag XMP properties use.</summary>
internal static class HierarchicalTagPath
{
    /// <summary>
    /// Title-cases free text (e.g. "angel" -> "Angel"). Lowercasing first avoids .NET's
    /// "leave ALL-CAPS words alone" acronym behavior in <see cref="TextInfo.ToTitleCase"/>.
    /// Only ever apply this to genuine natural-language text (entity label/group/taxonomy node
    /// names, ArtStyle) - never to enum-derived tokens like "ThreeDRender" or
    /// "DigitalIllustration", which have no word boundaries to title-case against and would have
    /// their internal capitalization collapsed ("ThreeDRender" -> "Threedrender").
    /// </summary>
    public static string TitleCase(string text) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());

    /// <summary>
    /// Builds the ordered path segments for a hierarchical tag from an already-prepared segment
    /// list (title-casing, if wanted, is the caller's responsibility - see <see cref="TitleCase"/>
    /// - since some segments, like a raw <c>EntityCategory</c> enum token, must never be
    /// title-cased). Collapses a segment that equals the immediately preceding *kept* segment
    /// (case-insensitive) - e.g. the model repeats a detection's label as its group when no more
    /// general group genuinely applies, and a resolved taxonomy chain can likewise end in two
    /// identical node names - neither should produce a redundant "X &gt; X" tag. Works for a
    /// chain of any length, not just a fixed 2-3 levels.
    /// </summary>
    public static string[] BuildSegments(IReadOnlyList<string> segments)
    {
        List<string> result = new(segments.Count);
        foreach (string segment in segments)
        {
            if (result.Count > 0 && string.Equals(result[^1], segment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(segment);
        }

        return [.. result];
    }

    /// <summary>
    /// The prepared, collapsed tag segments for one detected entity: its resolved taxonomy
    /// ancestor chain (title-cased) when available, or the raw Category/Group/Label shape
    /// (Category left as its PascalCase enum token, Group/Label title-cased) when the entity
    /// couldn't be resolved - this is the entity's only tag-shape fallback, not a legacy
    /// compatibility path, and it's what keeps the feature additive: an unresolved entity is
    /// tagged exactly as well as it always was, never worse.
    /// </summary>
    public static string[] BuildEntitySegments(DetectedEntity entity)
    {
        IReadOnlyList<string> segments = entity.Taxonomy is { Ancestors.Count: > 0 } taxonomy
            ? taxonomy.Ancestors.Select(n => TitleCase(n.Name)).ToArray()
            : [entity.Raw.Category.ToString(), TitleCase(entity.Raw.Group), TitleCase(entity.Raw.Label)];

        return BuildSegments(segments);
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
