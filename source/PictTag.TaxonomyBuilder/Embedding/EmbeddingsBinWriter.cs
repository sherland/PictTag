namespace PictTag.TaxonomyBuilder.Embedding;

/// <summary>
/// Writes/reads the packed binary embeddings file shipped alongside taxonomy.json. Binary, not
/// JSON, because this is bulk numeric data with nothing to hand-review - the reviewable content
/// (which node maps to which parent, its lemmas, etc.) lives entirely in taxonomy.json.
///
/// Format (little-endian, matching BinaryWriter/BinaryReader's default on all supported
/// platforms): int32 nodeCount, int32 dimension, then nodeCount * dimension float32 values in
/// row-major order, aligned 1:1 with taxonomy.json's "nodes" array order (vector i belongs to
/// taxonomy.json's i-th node - there is no id stored per-vector, the arrays must stay in sync).
/// </summary>
public static class EmbeddingsBinWriter
{
    public static void Write(string path, int dimension, IReadOnlyList<float[]> vectors)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);

        writer.Write(vectors.Count);
        writer.Write(dimension);
        foreach (float[] vector in vectors)
        {
            foreach (float value in vector)
            {
                writer.Write(value);
            }
        }
    }

    public static (int NodeCount, int Dimension, float[][] Vectors) Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);

        int nodeCount = reader.ReadInt32();
        int dimension = reader.ReadInt32();
        float[][] vectors = new float[nodeCount][];
        for (int i = 0; i < nodeCount; i++)
        {
            float[] vector = new float[dimension];
            for (int d = 0; d < dimension; d++)
            {
                vector[d] = reader.ReadSingle();
            }

            vectors[i] = vector;
        }

        return (nodeCount, dimension, vectors);
    }
}
