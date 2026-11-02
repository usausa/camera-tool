namespace CameraTool.Vision;

// Level: ratio of moving cells to the cells in the detection area (0-1)
public readonly record struct MotionResult(bool Detected, float Level, int Cells);
