using DerpLib.AssetPipeline;

namespace Derp.UI;

[ContentReader(typeof(CompiledUi))]
public sealed class CompiledUiContentReader : IContentReader<CompiledUi>
{
    public int Version => 1;

    public CompiledUi Read(byte[] payload, IBlobSerializer serializer)
    {
        _ = serializer;
        return CompiledUi.FromBduiPayload(payload);
    }
}

