using Godot;

namespace PS1Godot.Exporter;

// Fixed-point + rotation conversions for PSX coordinate space.
//
// PSX hardware uses 4.12 fixed-point for most things (12 fractional bits),
// stored as int16 for vertex positions / normals / matrix elements, or int32
// for world-space positions that need more range.
//
// Coordinate convention: PS1 is Y-down, Godot is Y-up. Callers must negate Y
// at the boundary — these helpers don't, so the call site stays explicit
// about what's happening (matches how SplashEdit's port does it).
public static class PSXTrig
{
    public const float FixedScale = 4096.0f; // 2^12

    /// <summary>4.12 fixed-point (int16). For local-space vertex positions and matrix elements.</summary>
    public static short ConvertCoordinateToPSX(float value, float gteScaling = 1.0f)
    {
        int fixedValue = Mathf.RoundToInt((value / gteScaling) * FixedScale);
        return (short)Mathf.Clamp(fixedValue, -32768, 32767);
    }

    /// <summary>4.12 fixed-point (int16). For values already in GTE space (pre-divided by gteScaling).</summary>
    public static short ConvertToFixed12(float value)
    {
        int fixedValue = Mathf.RoundToInt(value * FixedScale);
        return (short)Mathf.Clamp(fixedValue, -32768, 32767);
    }

    /// <summary>20.12 fixed-point (int32). For world-space positions / AABBs that need full int32 range.</summary>
    public static int ConvertWorldToFixed12(float value)
    {
        long fixedValue = (long)Mathf.RoundToInt(value * FixedScale);
        if (fixedValue < int.MinValue) return int.MinValue;
        if (fixedValue > int.MaxValue) return int.MaxValue;
        return (int)fixedValue;
    }

    /// <summary>
    /// Quaternion → 3×3 PSX rotation matrix in 4.12 fixed-point.
    /// Applies the Y-down conversion (negates rows/cols touching Y) inline.
    /// </summary>
    public static int[,] ConvertRotationToPSXMatrix(Quaternion rotation)
    {
        float x = rotation.X, y = rotation.Y, z = rotation.Z, w = rotation.W;

        float m00 = 1f - 2f * (y * y + z * z);
        float m01 = 2f * (x * y - z * w);
        float m02 = 2f * (x * z + y * w);
        float m10 = 2f * (x * y + z * w);
        float m11 = 1f - 2f * (x * x + z * z);
        float m12 = 2f * (y * z - x * w);
        float m20 = 2f * (x * z - y * w);
        float m21 = 2f * (y * z + x * w);
        float m22 = 1f - 2f * (x * x + y * y);

        // Y-down adjustment: negate elements that involve a Y axis exactly once.
        float[,] adjusted = new float[3, 3]
        {
            {  m00, -m01,  m02 },
            { -m10,  m11, -m12 },
            {  m20, -m21,  m22 },
        };

        var result = new int[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                result[i, j] = ConvertToFixed12(adjusted[i, j]);
        return result;
    }

    /// <summary>Color channel float [0,1] → byte [0,255] for PSX vertex colors.</summary>
    public static byte ColorChannelToPSX(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}
