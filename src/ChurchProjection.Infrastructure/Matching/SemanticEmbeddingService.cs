using System.Numerics.Tensors;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using Serilog;

namespace ChurchProjection.Infrastructure.Matching;

public class SemanticEmbeddingService : IDisposable
{
    private const string ModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    private const string VocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";
    private const int MaxSequenceLength = 128;
    private const int EmbeddingDim = 384;

    private readonly string _modelDir;
    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private bool _ready;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public bool IsReady => _ready;

    public SemanticEmbeddingService()
    {
        _modelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChurchProjection", "models", "minilm-v2");
    }

    public async Task InitializeAsync()
    {
        if (_ready) return;
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_ready) return;
            Directory.CreateDirectory(_modelDir);

            var modelPath = Path.Combine(_modelDir, "model.onnx");
            var vocabPath = Path.Combine(_modelDir, "vocab.txt");

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);

            if (!File.Exists(modelPath))
            {
                Log.Information("Downloading MiniLM ONNX model...");
                var data = await http.GetByteArrayAsync(ModelUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(modelPath, data).ConfigureAwait(false);
                Log.Information("MiniLM model downloaded ({Size:N1} MB)", data.Length / 1048576.0);
            }

            if (!File.Exists(vocabPath))
            {
                Log.Information("Downloading MiniLM vocabulary...");
                var data = await http.GetByteArrayAsync(VocabUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(vocabPath, data).ConfigureAwait(false);
                Log.Information("MiniLM vocab downloaded");
            }

            var options = new SessionOptions();
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            _session = new InferenceSession(modelPath, options);

            _tokenizer = BertTokenizer.Create(vocabPath, new BertOptions
            {
                LowerCaseBeforeTokenization = true,
            });

            _ready = true;
            Log.Information("Semantic embedding engine ready (MiniLM-L6-v2)");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize semantic embedding engine");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public float[]? Embed(string text)
    {
        if (!_ready || _session is null || _tokenizer is null)
            return null;

        try
        {
            var ids = _tokenizer.EncodeToIds(text, MaxSequenceLength, out _, out _);
            var inputIds = ids.ToArray();
            var attentionMask = new int[inputIds.Length];
            var tokenTypeIds = new int[inputIds.Length];
            Array.Fill(attentionMask, 1);

            var inputIdsTensor = new DenseTensor<long>(inputIds.Select(i => (long)i).ToArray(), [1, inputIds.Length]);
            var attentionTensor = new DenseTensor<long>(attentionMask.Select(i => (long)i).ToArray(), [1, inputIds.Length]);
            var typeTensor = new DenseTensor<long>(tokenTypeIds.Select(i => (long)i).ToArray(), [1, inputIds.Length]);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionTensor),
                NamedOnnxValue.CreateFromTensor("token_type_ids", typeTensor),
            };

            using var results = _session.Run(inputs);
            var output = results.First(r => r.Name == "last_hidden_state");
            var tensor = output.AsTensor<float>();

            var embedding = MeanPool(tensor, attentionMask, inputIds.Length);
            Normalize(embedding);

            return embedding;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Embedding generation failed for text length {Len}", text.Length);
            return null;
        }
    }

    private static float[] MeanPool(Microsoft.ML.OnnxRuntime.Tensors.Tensor<float> hiddenState, int[] attentionMask, int seqLen)
    {
        var embedding = new float[EmbeddingDim];
        float count = 0;

        for (int i = 0; i < seqLen; i++)
        {
            if (attentionMask[i] == 0) continue;
            count++;
            for (int j = 0; j < EmbeddingDim; j++)
                embedding[j] += hiddenState[0, i, j];
        }

        if (count > 0)
        {
            for (int j = 0; j < EmbeddingDim; j++)
                embedding[j] /= count;
        }

        return embedding;
    }

    private static void Normalize(float[] vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        if (norm > 0)
            TensorPrimitives.Divide(vector, norm, vector);
    }

    /// <summary>
    /// Cosine similarity for L2-normalized embeddings, which reduces to a SIMD dot product.
    /// </summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        return TensorPrimitives.Dot(a, b);
    }

    public void Dispose()
    {
        _session?.Dispose();
    }
}
