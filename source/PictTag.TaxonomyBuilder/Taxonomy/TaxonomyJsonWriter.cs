using System.Text.Json;

namespace PictTag.TaxonomyBuilder.Taxonomy;

public static class TaxonomyJsonWriter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Always writes indented, human-diffable JSON - this file is meant to be hand-reviewed in
    /// code review every time trim config changes, and a single minified line would defeat that.
    /// </summary>
    public static void Write(TaxonomyDocument document, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, document, Options);
    }
}
