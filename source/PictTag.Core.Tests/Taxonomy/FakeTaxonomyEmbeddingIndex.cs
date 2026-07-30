using PictTag.Core.Taxonomy;

namespace PictTag.Core.Tests.Taxonomy;

/// <summary>A canned stand-in for <see cref="OllamaTaxonomyEmbeddingIndex"/> - no live Ollama call.</summary>
public sealed class FakeTaxonomyEmbeddingIndex : ITaxonomyEmbeddingIndex
{
    private readonly Dictionary<string, TaxonomyMatch?> _resultsByFreeText;
    public List<string> Queries { get; } = [];

    public FakeTaxonomyEmbeddingIndex(Dictionary<string, TaxonomyMatch?> resultsByFreeText)
    {
        _resultsByFreeText = resultsByFreeText;
    }

    public Task<TaxonomyMatch?> FindNearestAsync(string freeText, EntityCategory? categoryHint, CancellationToken cancellationToken = default)
    {
        Queries.Add(freeText);
        return Task.FromResult(_resultsByFreeText.GetValueOrDefault(freeText));
    }
}
