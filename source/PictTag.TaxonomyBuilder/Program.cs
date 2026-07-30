using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OllamaSharp;
using PictTag.TaxonomyBuilder.Embedding;
using PictTag.TaxonomyBuilder.Seeding;
using PictTag.TaxonomyBuilder.Taxonomy;
using PictTag.TaxonomyBuilder.WordNet;

Option<string> wordNetDirOption = new("--wordnet-dir")
{
    Description = "Directory containing the raw data.noun/index.noun files.",
    DefaultValueFactory = _ => Path.Combine("data", "wordnet", "raw"),
};

Option<string> imageNetIndexOption = new("--imagenet-index")
{
    Description = "Path to imagenet_class_index.json.",
    DefaultValueFactory = _ => Path.Combine("data", "wordnet", "raw", "imagenet_class_index.json"),
};

Option<string> manualSeedsOption = new("--manual-seeds")
{
    Description = "Path to manual-seeds.json.",
    DefaultValueFactory = _ => Path.Combine("data", "wordnet", "seeds", "manual-seeds.json"),
};

Option<string> expandDomainsOption = new("--expand-domains")
{
    Description = "Path to expand-domains.json.",
    DefaultValueFactory = _ => Path.Combine("data", "wordnet", "seeds", "expand-domains.json"),
};

Option<string> trimConfigOption = new("--trim-config")
{
    Description = "Path to trim-config.json.",
    DefaultValueFactory = _ => Path.Combine("data", "wordnet", "seeds", "trim-config.json"),
};

Option<string> outDirOption = new("--out-dir")
{
    Description = "Directory to write the emitted taxonomy.json into.",
    DefaultValueFactory = _ => Path.Combine("source", "PictTag.Core", "Taxonomy"),
};

Option<string?> debugOutOption = new("--debug-out")
{
    Description = "Optional path to also write the full untrimmed reachable graph (before excludeSynsets/excludeSubtrees/rootCollapseAnchors are applied) for inspecting what trimming actually removed.",
};

Option<string> ollamaUrlOption = new("--ollama-url")
{
    Description = "Base URL of the Ollama server used for embeddings.",
    DefaultValueFactory = _ => "http://localhost:11434",
};

Option<string> embeddingModelOption = new("--embedding-model")
{
    Description = "Ollama embedding model to embed each taxonomy node's canonical text with.",
    DefaultValueFactory = _ => "nomic-embed-text",
};

Option<string> embeddingCacheOption = new("--embedding-cache")
{
    Description = "Path to the embedding cache file (speeds up reruns - only new/changed nodes are re-embedded). Not committed to git.",
    DefaultValueFactory = _ => Path.Combine("data", "wordnet", "build-debug", "embedding-cache.json"),
};

Option<bool> skipEmbeddingsOption = new("--skip-embeddings")
{
    Description = "Skip the embedding step entirely (fast iteration on seeding/trimming without needing Ollama running).",
};

RootCommand rootCommand = new("PictTag.TaxonomyBuilder - builds the WordNet-derived taxonomy.json/taxonomy-embeddings.bin shipped with PictTag.Core.");
rootCommand.Add(wordNetDirOption);
rootCommand.Add(imageNetIndexOption);
rootCommand.Add(manualSeedsOption);
rootCommand.Add(expandDomainsOption);
rootCommand.Add(trimConfigOption);
rootCommand.Add(outDirOption);
rootCommand.Add(debugOutOption);
rootCommand.Add(ollamaUrlOption);
rootCommand.Add(embeddingModelOption);
rootCommand.Add(embeddingCacheOption);
rootCommand.Add(skipEmbeddingsOption);

rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
{
    string wordNetDir = parseResult.GetValue(wordNetDirOption)!;
    string imageNetIndexPath = parseResult.GetValue(imageNetIndexOption)!;
    string manualSeedsPath = parseResult.GetValue(manualSeedsOption)!;
    string expandDomainsPath = parseResult.GetValue(expandDomainsOption)!;
    string trimConfigPath = parseResult.GetValue(trimConfigOption)!;
    string outDir = parseResult.GetValue(outDirOption)!;
    string? debugOutPath = parseResult.GetValue(debugOutOption);
    string ollamaUrl = parseResult.GetValue(ollamaUrlOption)!;
    string embeddingModel = parseResult.GetValue(embeddingModelOption)!;
    string embeddingCachePath = parseResult.GetValue(embeddingCacheOption)!;
    bool skipEmbeddings = parseResult.GetValue(skipEmbeddingsOption);

    Console.WriteLine($"Parsing WordNet noun database from {wordNetDir}...");
    WordNetDatabase db = WordNetParser.ParseNounDatabase(
        Path.Combine(wordNetDir, "data.noun"), Path.Combine(wordNetDir, "index.noun"));
    Console.WriteLine($"Parsed {db.SynsetsById.Count} synsets, {db.SynsetIdsByLemma.Count} lemmas.");

    IReadOnlyList<ImageNetLeaf> imageNetLeaves = ImageNetLeafLoader.Load(imageNetIndexPath);
    IReadOnlyList<ManualSeed> manualSeeds = ManualSeedLoader.Load(manualSeedsPath);
    IReadOnlyList<DomainExpansion> domainExpansions = DomainExpansionLoader.Load(expandDomainsPath);
    TrimConfig trimConfig = TrimConfigLoader.Load(trimConfigPath);
    Console.WriteLine(
        $"Loaded {imageNetLeaves.Count} ImageNet leaves, {manualSeeds.Count} manual seeds, "
        + $"{domainExpansions.Count} domain expansions, {trimConfig.AllAnchorIds.Count} category anchors.");

    IReadOnlyList<TaxonomyBuildNode> nodes = TaxonomyGraphBuilder.Build(db, imageNetLeaves, manualSeeds, domainExpansions, trimConfig);
    Console.WriteLine($"Built taxonomy with {nodes.Count} nodes (from {imageNetLeaves.Count + manualSeeds.Count + domainExpansions.Count} seeds).");

    TaxonomyDocument document = new(
        Version: "1.0",
        License: "WordNet 3.0, Princeton University - see data/wordnet/raw/LICENSE for full terms.",
        GeneratedFromWordNetVersion: "3.0",
        Nodes: nodes,
        CategoryRoots: trimConfig.RootCollapseAnchors);

    string taxonomyPath = Path.Combine(outDir, "taxonomy.json");
    TaxonomyJsonWriter.Write(document, taxonomyPath);
    Console.WriteLine($"Wrote {taxonomyPath}");

    if (debugOutPath is not null)
    {
        WriteDebugGraph(db, debugOutPath);
        Console.WriteLine($"Wrote debug graph (full parsed database, {db.SynsetsById.Count} synsets) to {debugOutPath}");
    }

    if (!skipEmbeddings)
    {
        Console.WriteLine($"Embedding {nodes.Count} nodes via '{embeddingModel}' at {ollamaUrl}...");
        IEmbeddingGenerator<string, Embedding<float>> generator = new OllamaApiClient(new Uri(ollamaUrl), embeddingModel);
        EmbeddingResult embeddingResult = await NodeEmbedder.EmbedAllAsync(nodes, generator, embeddingCachePath, ct);
        Console.WriteLine($"Embeddings: {embeddingResult.CacheHits} cache hits, {embeddingResult.NewlyEmbedded} newly computed (dimension {embeddingResult.Dimension}).");

        string embeddingsPath = Path.Combine(outDir, "taxonomy-embeddings.bin");
        EmbeddingsBinWriter.Write(embeddingsPath, embeddingResult.Dimension, embeddingResult.Vectors);
        Console.WriteLine($"Wrote {embeddingsPath}");
    }

    return 0;
});

ParseResult parsed = rootCommand.Parse(args);
return await parsed.InvokeAsync();

static void WriteDebugGraph(WordNetDatabase db, string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    using FileStream stream = File.Create(path);
    JsonSerializer.Serialize(stream, db.SynsetsById.Values, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
}
