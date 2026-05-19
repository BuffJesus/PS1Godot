#if TOOLS
using Godot;

namespace PS1Godot.UI;

// Custom thumbnail generator for PS1AudioClip resources (UE port plan
// — beyond-Blueprint pick #1). Godot's FileSystem dock previews any
// Resource via registered EditorResourcePreviewGenerators; before
// this plugin's load no generator handles PS1AudioClip, so every
// clip looks like an anonymous Resource icon.
//
// Rendered preview: peak-detected waveform on a dark background +
// a colored top stripe encoding the runtime route (SPU/XA/CDDA/Auto)
// + a thin bottom stripe when Loop=true. Author can scan an audio
// folder and immediately spot "this clip is long" (wide waveform),
// "this clip is silent" (flat line), "this is XA" (blue stripe),
// "this loops" (bottom marker).
//
// Reads samples from AudioStreamWav.Data (raw PCM). Non-WAV streams
// (Ogg, MP3) render a placeholder gradient — most PS1 authoring uses
// WAV upstream of the ADPCM conversion, so handling WAV covers the
// common case. Slice 2 can decode Ogg via AudioStreamOggVorbis.GetData
// if a real author hits the placeholder.
public partial class PS1AudioClipPreviewGenerator : EditorResourcePreviewGenerator
{
    public override bool _Handles(string type) => type == nameof(PS1AudioClip);

    // FileSystem dock thumbnails are SMALL previews (64×64 typically).
    // Without these overrides, Godot 4.7-dev5 falls back to the
    // anonymous Resource icon for every PS1AudioClip — our _Generate
    // is only queried for large previews (Inspector / asset browser).
    // _CanGenerateSmallPreview makes the dock query us for small
    // sizes; _GenerateSmallPreviewAutomatically tells Godot to reuse
    // our _Generate output and downscale it (the waveform reads fine
    // at 64×64 once antialiasing kicks in).
    public override bool _CanGenerateSmallPreview() => true;
    public override bool _GenerateSmallPreviewAutomatically() => true;

    public override Texture2D? _Generate(Resource resource, Vector2I size,
                                          Godot.Collections.Dictionary metadata)
    {
        if (resource is not PS1AudioClip clip) return null;
        if (size.X < 8 || size.Y < 8) return null;

        var img = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        Color bg       = new(0.08f, 0.09f, 0.12f);
        Color waveform = new(0.55f, 0.85f, 0.55f);
        Color mid      = new(0.20f, 0.22f, 0.25f);
        img.Fill(bg);

        // Route stripe across the top — three pixel rows. Colour
        // matches the existing dock convention (Audio category =
        // amber-ish in PS1 Doctor; per-route distinct hues here so
        // SPU/XA/CDDA read at a glance).
        Color routeColor = clip.Route switch
        {
            PS1AudioRoute.SPU  => new Color(0.95f, 0.65f, 0.30f),  // amber
            PS1AudioRoute.XA   => new Color(0.30f, 0.65f, 0.95f),  // blue
            PS1AudioRoute.CDDA => new Color(0.55f, 0.85f, 0.55f),  // green
            _                  => new Color(0.55f, 0.55f, 0.55f),  // grey for Auto
        };
        int stripeRows = System.Math.Max(2, size.Y / 16);
        for (int y = 0; y < stripeRows; y++)
        for (int x = 0; x < size.X; x++)
            img.SetPixel(x, y, routeColor);

        // Midline reference (zero crossing) — single dim pixel row.
        int midY = stripeRows + (size.Y - stripeRows) / 2;
        for (int x = 0; x < size.X; x++)
            img.SetPixel(x, midY, mid);

        // Waveform — peak-detect per X bucket. Falls back to flat
        // midline when no usable samples.
        if (TryReadWavSamples(clip.Stream, out short[] samples, out int channels))
        {
            int waveTop    = stripeRows + 1;
            int waveBottom = size.Y - (clip.Loop ? 2 : 1);
            int waveHeight = waveBottom - waveTop;
            if (waveHeight < 4) waveHeight = 4;

            int totalFrames = samples.Length / System.Math.Max(1, channels);
            int buckets     = size.X;
            for (int bx = 0; bx < buckets; bx++)
            {
                int startFrame = (int)((long)bx       * totalFrames / buckets);
                int endFrame   = (int)((long)(bx + 1) * totalFrames / buckets);
                if (endFrame <= startFrame) endFrame = startFrame + 1;
                short min = short.MaxValue, max = short.MinValue;
                for (int f = startFrame; f < endFrame && f < totalFrames; f++)
                {
                    // Mix to mono for multichannel: just take channel 0
                    // (stereo PS1 clips are rare and the preview only
                    // cares about envelope, not channel separation).
                    short s = samples[f * channels];
                    if (s < min) min = s;
                    if (s > max) max = s;
                }
                if (min > max) { min = 0; max = 0; }
                // Map [-32768..32767] → [waveBottom..waveTop]
                int yMin = midY + (int)((long)max * (waveHeight / 2) / -32768);
                int yMax = midY + (int)((long)min * (waveHeight / 2) / -32768);
                if (yMin > yMax) (yMin, yMax) = (yMax, yMin);
                if (yMin < waveTop)    yMin = waveTop;
                if (yMax > waveBottom) yMax = waveBottom;
                for (int y = yMin; y <= yMax; y++)
                    img.SetPixel(bx, y, waveform);
            }
        }
        else
        {
            // No usable PCM — placeholder zig-zag so the preview
            // doesn't look broken. Different shade so the author
            // notices ("oh, this isn't a WAV").
            Color placeholder = new(0.55f, 0.55f, 0.30f);
            for (int x = 0; x < size.X; x++)
            {
                int amp = (size.Y - stripeRows) / 6;
                int y = midY + (int)(System.Math.Sin(x * 0.25) * amp);
                if (y >= 0 && y < size.Y) img.SetPixel(x, y, placeholder);
            }
        }

        // Loop marker — single-pixel bottom row, distinct hue.
        if (clip.Loop)
        {
            Color loopColor = new(0.90f, 0.40f, 0.85f);
            for (int x = 0; x < size.X; x++)
                img.SetPixel(x, size.Y - 1, loopColor);
        }

        return ImageTexture.CreateFromImage(img);
    }

    // Pulls raw PCM samples out of an AudioStreamWav. Other stream
    // types (Ogg etc.) return false so the generator falls back to
    // the placeholder. Channels = 1 (mono) or 2 (stereo); samples
    // are interleaved frame-major for stereo.
    private static bool TryReadWavSamples(AudioStream? stream, out short[] samples, out int channels)
    {
        samples  = System.Array.Empty<short>();
        channels = 1;
        if (stream is not AudioStreamWav wav) return false;
        var data = wav.Data;
        if (data == null || data.Length < 2) return false;
        channels = wav.Stereo ? 2 : 1;

        if (wav.Format == AudioStreamWav.FormatEnum.Format8Bits)
        {
            samples = new short[data.Length];
            for (int i = 0; i < data.Length; i++)
                samples[i] = (short)((data[i] - 128) << 8);  // unsigned 8 → signed 16
        }
        else if (wav.Format == AudioStreamWav.FormatEnum.Format16Bits)
        {
            int count = data.Length / 2;
            samples = new short[count];
            for (int i = 0; i < count; i++)
                samples[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
        }
        else
        {
            // IMA-ADPCM or QOA — skip; placeholder will render.
            return false;
        }
        return true;
    }
}
#endif
