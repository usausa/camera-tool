namespace CameraTool.Vision;

public enum ObjectGroup
{
    Other,
    Person,
    Animal,
    Vehicle
}

// COCO classes in the order of the model output
public static class ObjectLabels
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat", "traffic light",
        "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse", "sheep", "cow",
        "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
        "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket", "bottle",
        "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange",
        "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch", "potted plant", "bed",
        "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard", "cell phone", "microwave", "oven",
        "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush"
    ];

    public static int Count => Names.Count;

    // Case insensitive, -1 when not found
    public static int IndexOf(string name)
    {
        for (var i = 0; i < Names.Count; i++)
        {
            if (String.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public static ObjectGroup GetGroup(int classId) =>
        classId switch
        {
            0 => ObjectGroup.Person,
            >= 1 and <= 8 => ObjectGroup.Vehicle,
            >= 14 and <= 23 => ObjectGroup.Animal,
            _ => ObjectGroup.Other
        };
}
