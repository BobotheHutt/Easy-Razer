namespace EasyRazer.Devices.Razer;

/// <summary>
/// The 90-byte control packet Razer devices use for configuration, sent as a HID feature report.
/// Layout (documented by the OpenRazer project, razercommon.h):
///   [0]  status            0x00 for a new command; device answers 0x02 success, 0x01 busy, 0x03 fail, 0x04 timeout, 0x05 unsupported
///   [1]  transaction id    device-specific (commonly 0x1F, 0x3F or 0xFF)
///   [2..3] remaining packets (big endian, usually 0)
///   [4]  protocol type     0x00
///   [5]  data size         number of argument bytes used
///   [6]  command class
///   [7]  command id        bit 7 set = "get" request
///   [8..87] arguments (80 bytes)
///   [88] crc               XOR of bytes 2..87
///   [89] reserved          0x00
/// </summary>
public sealed class RazerReport
{
    public const int Length = 90;

    public byte Status { get; init; }
    public byte TransactionId { get; init; }
    public byte DataSize { get; init; }
    public byte CommandClass { get; init; }
    public byte CommandId { get; init; }
    public byte[] Arguments { get; init; } = new byte[80];

    public static RazerReport Create(byte transactionId, byte commandClass, byte commandId, byte dataSize, params byte[] args)
    {
        if (args.Length > 80) throw new ArgumentException("Razer reports carry at most 80 argument bytes");
        var a = new byte[80];
        Array.Copy(args, a, args.Length);
        return new RazerReport
        {
            TransactionId = transactionId,
            DataSize = dataSize,
            CommandClass = commandClass,
            CommandId = commandId,
            Arguments = a,
        };
    }

    public byte[] ToBytes()
    {
        var b = new byte[Length];
        b[0] = Status;
        b[1] = TransactionId;
        b[2] = 0;
        b[3] = 0;
        b[4] = 0;
        b[5] = DataSize;
        b[6] = CommandClass;
        b[7] = CommandId;
        Array.Copy(Arguments, 0, b, 8, 80);
        b[88] = Crc(b);
        b[89] = 0;
        return b;
    }

    public static RazerReport Parse(ReadOnlySpan<byte> b)
    {
        if (b.Length < Length) throw new ArgumentException("Short Razer report");
        return new RazerReport
        {
            Status = b[0],
            TransactionId = b[1],
            DataSize = b[5],
            CommandClass = b[6],
            CommandId = b[7],
            Arguments = b.Slice(8, 80).ToArray(),
        };
    }

    public static byte Crc(ReadOnlySpan<byte> b)
    {
        byte crc = 0;
        for (var i = 2; i < 88; i++) crc ^= b[i];
        return crc;
    }

    /// <summary>Compact hex of the meaningful part, for logs: "tid cls:cmd size | args".</summary>
    public string Describe()
    {
        var used = Math.Clamp((int)DataSize, 0, 80);
        return $"tid={TransactionId:X2} cmd={CommandClass:X2}:{CommandId:X2} size={DataSize:X2} args={Convert.ToHexString(Arguments, 0, used)}";
    }
}
