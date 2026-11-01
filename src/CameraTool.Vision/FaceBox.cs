namespace CameraTool.Vision;

// Normalized coordinates (0-1)
public readonly record struct FaceBox(float Left, float Top, float Right, float Bottom, float Confidence);
