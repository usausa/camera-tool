namespace CameraTool.Vision;

// Box of a detection (face or object), 0-1 in the frame
public interface IDetectionBox
{
    float Left { get; }

    float Top { get; }

    float Right { get; }

    float Bottom { get; }
}
