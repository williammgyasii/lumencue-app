namespace ChurchProjection.Core.Services;

/// <summary>
/// MiniLM/ONNX is large. Projection-only booths must not pay that RAM until Find Scripture runs.
/// </summary>
public static class SemanticEmbeddingPolicy
{
    public const bool LoadAtLaunch = false;
    public const bool IndexAfterBibleCache = false;
}
