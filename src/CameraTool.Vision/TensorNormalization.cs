namespace CameraTool.Vision;

// Tensor value of a color value (0-255): (x - Mean) * Scale
public readonly record struct TensorNormalization(float Mean, float Scale)
{
    // (x - 127) / 128 (version-RFB-320)
    public static TensorNormalization Centered { get; } = new(127f, 1f / 128f);

    // x / 255 (D-FINE)
    public static TensorNormalization Unit { get; } = new(0f, 1f / 255f);
}
