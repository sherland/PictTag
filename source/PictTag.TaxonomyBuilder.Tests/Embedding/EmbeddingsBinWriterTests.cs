using PictTag.TaxonomyBuilder.Embedding;

namespace PictTag.TaxonomyBuilder.Tests.Embedding;

public class EmbeddingsBinWriterTests
{
    [Fact]
    public void WriteThenRead_RoundTripsNodeOrderAndValuesExactly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"embeddings-{Guid.NewGuid():N}.bin");
        try
        {
            List<float[]> vectors =
            [
                [1.0f, 2.0f, 3.0f],
                [-0.5f, 0.25f, 0.125f],
                [0f, 0f, 0f],
            ];

            EmbeddingsBinWriter.Write(path, dimension: 3, vectors);
            (int nodeCount, int dimension, float[][] readBack) = EmbeddingsBinWriter.Read(path);

            Assert.Equal(3, nodeCount);
            Assert.Equal(3, dimension);
            Assert.Equal(vectors[0], readBack[0]);
            Assert.Equal(vectors[1], readBack[1]);
            Assert.Equal(vectors[2], readBack[2]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
