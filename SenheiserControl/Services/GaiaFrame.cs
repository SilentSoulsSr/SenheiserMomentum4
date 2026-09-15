using System.Buffers.Binary;

namespace SenheiserControl.Services;

/// <summary>
/// GAIA-v3-style frame used by the Momentum 4's RFCOMM control channel:
/// FF 03 | length(2, BE) | vendor(2, BE) | command(2, BE) | payload.
/// Layout taken from the community reverse-engineering in f3Y0/momentum4-control; unverified against real hardware.
/// </summary>
public readonly struct GaiaFrame(ushort command, byte[] payload)
{
    private const byte MagicHigh = 0xFF;
    private const byte MagicLow = 0x03;
    private const ushort Vendor = 0x0495;
    private const int HeaderLength = 8;

    public ushort Command { get; } = command;
    public byte[] Payload { get; } = payload;

    public byte[] Encode()
    {
        var frame = new byte[HeaderLength + Payload.Length];
        frame[0] = MagicHigh;
        frame[1] = MagicLow;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), (ushort)Payload.Length);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), Vendor);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6, 2), Command);
        Payload.CopyTo(frame.AsSpan(HeaderLength));
        return frame;
    }

    /// <summary>Tries to parse one frame from the front of <paramref name="buffer"/>.
    /// Returns false with consumed=1 on a bad magic byte, so the caller can resync by dropping it.</summary>
    public static bool TryParse(byte[] buffer, out GaiaFrame frame, out int consumed)
    {
        frame = default;
        consumed = 0;

        if (buffer.Length < 2)
        {
            return false;
        }

        if (buffer[0] != MagicHigh || buffer[1] != MagicLow)
        {
            consumed = 1;
            return false;
        }

        if (buffer.Length < HeaderLength)
        {
            return false;
        }

        ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
        int totalLength = HeaderLength + payloadLength;
        if (buffer.Length < totalLength)
        {
            return false;
        }

        ushort command = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6, 2));
        byte[] payload = buffer.AsSpan(HeaderLength, payloadLength).ToArray();

        frame = new GaiaFrame(command, payload);
        consumed = totalLength;
        return true;
    }
}
