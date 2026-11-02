namespace CameraTool.Vision;

internal static class TestData
{
    // Deterministic xorshift data
    public static byte[] CreateRandom(int length, uint seed)
    {
        var buffer = new byte[length];
        var state = seed;
        for (var i = 0; i < buffer.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            buffer[i] = (byte)state;
        }

        return buffer;
    }
}
