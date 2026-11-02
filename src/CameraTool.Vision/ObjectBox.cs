namespace CameraTool.Vision;

// Normalized coordinates (0-1), ClassId: index of ObjectLabels
public readonly record struct ObjectBox(float Left, float Top, float Right, float Bottom, float Confidence, int ClassId) : IDetectionBox;
