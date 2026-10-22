using Recora.Encoding;

namespace Recora.Core.Tests;

public class EncoderTests
{
    [Fact]
    public void MinimalMp4_has_ftyp_box()
    {
        var bytes = MinimalMp4.CreateSilentClip();
        Assert.True(bytes.Length > 16);
        Assert.Equal((byte)'f', bytes[4]);
        Assert.Equal((byte)'t', bytes[5]);
        Assert.Equal((byte)'y', bytes[6]);
        Assert.Equal((byte)'p', bytes[7]);
    }

    [Fact]
    public async Task ChunkMediaEncoder_stub_is_playable_flagged()
    {
        var enc = new ChunkMediaEncoder(preferPlayableStub: true);
        var result = await enc.EncodeChunkAsync(
            "m1", 0, new QualityEncodeSettings(1280, 720, 1_500_000, 8, 96_000),
            Array.Empty<float>(), 48000, 2);

        Assert.True(result.Bytes.Length > 8);
        if (enc.FfmpegPath is null)
        {
            Assert.Equal(ChunkEncodeMode.MinimalPlayableStub, result.Mode);
            Assert.True(result.IsPlayable);
            Assert.False(string.IsNullOrWhiteSpace(result.Warning));
        }
        else
        {
            Assert.True(result.IsPlayable);
        }
    }

    [Fact]
    public void WasapiEnumerator_returns_at_least_default()
    {
        var devices = new Recora.Capture.WasapiMicrophoneEnumerator().ListMicrophones();
        Assert.Contains(devices, d => d.Id == "default" && d.IsDefault);
    }
}
