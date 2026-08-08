using System;
using FrameShift.Core.AI.RemoveNoise;
using Xunit;

namespace FrameShift.Tests;

public sealed class RemoveNoiseBufferReuseTests
{
    [Fact]
    public void LookaheadFeatureWrites_MatchPreviousShiftedFeatureLayout()
    {
        const int channels = 2;
        const int frames = 7;
        const int bins = 3;
        const int lookahead = 2;
        var source = new float[channels * frames * bins];
        for (int i = 0; i < source.Length; i++)
            source[i] = i + 0.25f;

        var actual = new float[source.Length];
        for (int channel = 0; channel < channels; channel++)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                int offset = (channel * frames + frame) * bins;
                RemoveNoiseEngine.StoreLookaheadShiftedFrame(
                    actual,
                    channels,
                    frames,
                    bins,
                    lookahead,
                    frame,
                    channel,
                    source.AsSpan(offset, bins));
            }
        }

        var expected = LegacyShift(source, channels, frames, bins, lookahead);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DirectSynthesisWrite_MatchesLegacyFullSynthesisBuffer()
    {
        const int hop = 4;
        const int frames = 5;
        const int delay = 3;
        var legacySynthesis = new float[frames * hop];
        var directClean = new float[15];
        for (int frame = 0; frame < frames; frame++)
        {
            var chunk = new float[hop];
            for (int sample = 0; sample < hop; sample++)
                chunk[sample] = frame * 10 + sample;

            Array.Copy(chunk, 0, legacySynthesis, frame * hop, hop);
            RemoveNoiseEngine.CopySynthesisChunkToClean(chunk, directClean, frame, delay);
        }

        var expected = new float[directClean.Length];
        Array.Copy(legacySynthesis, delay, expected, 0, expected.Length);
        Assert.Equal(expected, directClean);
    }

    private static float[] LegacyShift(float[] source, int channels, int frames, int bins, int lookahead)
    {
        var result = new float[source.Length];
        for (int channel = 0; channel < channels; channel++)
        {
            int channelOffset = channel * frames * bins;
            for (int frame = 0; frame + lookahead < frames; frame++)
            {
                Array.Copy(
                    source,
                    channelOffset + (frame + lookahead) * bins,
                    result,
                    channelOffset + frame * bins,
                    bins);
            }
        }

        return result;
    }
}
