using System;
using Godot;

namespace PS1Godot.Exporter;

// PSX SPU ADPCM encoder. Converts 16-bit signed PCM mono to the 16-byte
// block format the PSX SPU expects.
//
// Block layout (16 bytes encoding 28 samples):
//   [0]       header: (filter << 4) | shift
//   [1]       flags:  bit0 end, bit1 repeat, bit2 loopStart
//   [2..15]   14 bytes × 2 nibbles = 28 samples, low nibble first
//
// This pass uses filter 0 (no prediction) exclusively — legal PSX ADPCM,
// just leaves compression quality on the table. Upgrading to per-block
// filter selection (filters 1-4 use previous outputs as predictors) is a
// follow-up when audio quality becomes the bottleneck; the block layout
// doesn't change.
public static class ADPCMEncoder
{
    private const int SamplesPerBlock = 28;
    public const int BytesPerBlock = 16;

    // Encode a stream of 16-bit signed PCM mono samples to SPU ADPCM.
    // The output starts with a silent block (required so the SPU has a
    // valid starting state before the first real block — many PSX titles
    // do this) and ends with an end-marker block so the hardware stops
    // or loops as configured.
    public static byte[] Encode(short[] samples, bool loop)
    {
        int blockCount = (samples.Length + SamplesPerBlock - 1) / SamplesPerBlock;
        if (blockCount == 0) blockCount = 1;

        // +1 leading silent block: the SPU references "previous sample"
        // across blocks; starting from a known-zero state keeps the first
        // real block decode clean.
        int totalBlocks = blockCount + 1;
        byte[] output = new byte[totalBlocks * BytesPerBlock];

        // Silent leading block (all zeros). Header + flags + 14 data bytes
        // are already zero from the array init.

        // Reused across iterations — one allocation outside the hot loop.
        Span<short> block = stackalloc short[SamplesPerBlock];

        for (int b = 0; b < blockCount; b++)
        {
            int srcStart = b * SamplesPerBlock;
            int srcEnd = Math.Min(srcStart + SamplesPerBlock, samples.Length);

            // Copy this block's samples, zero-padding the tail on the final
            // partial block.
            for (int i = 0; i < SamplesPerBlock; i++)
            {
                int srcIdx = srcStart + i;
                block[i] = srcIdx < srcEnd ? samples[srcIdx] : (short)0;
            }

            byte shift = PickShift(block);
            int outIdx = (b + 1) * BytesPerBlock;

            // Header byte: filter 0, shift in low nibble.
            output[outIdx + 0] = (byte)(shift & 0x0F);

            // Flags byte:
            //   bit0 (0x01) "end of sample" — stops or repeats the voice
            //   bit1 (0x02) "repeat" — set on every block that's part of a
            //                          loop body so the SPU knows to come
            //                          back here after reaching the end
            //   bit2 (0x04) "loop start" — set exactly once per looping
            //                              sample on the block to loop back
            //                              to
            // For a looped sample we mark first block as loop-start and the
            // last block as end+repeat. For a one-shot we just mark the last
            // block end.
            byte flags = 0;
            bool isLast = b == blockCount - 1;
            bool isFirst = b == 0;
            if (loop && isFirst) flags |= 0x04;
            if (isLast)
            {
                flags |= 0x01; // end
                if (loop) flags |= 0x02; // repeat instead of stop
            }
            output[outIdx + 1] = flags;

            // Encode samples to nibbles. Each data byte holds two samples:
            // first sample in the low nibble, second in the high nibble.
            for (int i = 0; i < SamplesPerBlock; i += 2)
            {
                int s0 = EncodeSample(block[i], shift);
                int s1 = EncodeSample(block[i + 1], shift);
                output[outIdx + 2 + (i / 2)] = (byte)((s0 & 0x0F) | ((s1 & 0x0F) << 4));
            }
        }

        return output;
    }

    // For filter 0 the reconstructed sample is nibble << (12 - shift),
    // clamped to int16. Pick the largest shift (highest precision for
    // small signals) where every sample still fits in the 4-bit range.
    //
    // shift=0 gives nibble range [-8, 7] × 4096 = ±32768 (full int16),
    // shift=12 gives ±8. Signals quieter than ±8 get padded anyway.
    private static byte PickShift(ReadOnlySpan<short> block)
    {
        short maxAbs = 0;
        for (int i = 0; i < block.Length; i++)
        {
            short s = block[i];
            int abs = s < 0 ? -s : s;
            if (abs > maxAbs) maxAbs = (short)abs;
        }

        // We need:  (maxAbs + half_rounding) / 2^(12-shift) <= 7
        // => 2^(12-shift) >= maxAbs / 7
        // Largest shift satisfying that:
        for (byte shift = 12; shift > 0; shift--)
        {
            int divisor = 1 << (12 - shift);
            int rounded = (maxAbs + (divisor >> 1)) / divisor;
            if (rounded <= 7) return shift;
        }
        return 0; // full-scale signal; least precision.
    }

    private static int EncodeSample(short pcm, byte shift)
    {
        int divisor = 1 << (12 - shift);
        // Round to nearest (away from zero) so quantization isn't
        // asymmetric around small negative values.
        int n = pcm >= 0
            ? (pcm + (divisor >> 1)) / divisor
            : -((-pcm + (divisor >> 1)) / divisor);
        if (n > 7) n = 7;
        if (n < -8) n = -8;
        return n;
    }
}
