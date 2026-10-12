using System.Buffers.Binary;

namespace HappyTime;

internal static class TimeHelper
{
    public static byte[] GetTimeBigEdianBytes()
    {
        var epoch = GetEpoch();
        var seconds = (ulong)(DateTimeOffset.UtcNow - epoch).TotalSeconds;
        var timeValue = unchecked((uint)seconds);

        byte[] responseBytes = new byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(responseBytes, timeValue);
        return responseBytes;
    }

    public static DateTimeOffset GetEpoch()
    {
        var epoch = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return epoch;
    }
}
