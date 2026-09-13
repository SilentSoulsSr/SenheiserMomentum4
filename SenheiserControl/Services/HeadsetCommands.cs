namespace SenheiserControl.Services;

internal static class HeadsetCommands
{
    public const ushort AncSet = 0x1A04;
    public const ushort AncGet = 0x1A05;
    public const ushort TransparencySet = 0x1A02;
    public const ushort TransparencyGet = 0x1A03;
    public const ushort BassBoostSet = 0x1008;
    public const ushort BassBoostGet = 0x1009;

    public const ushort ResponseFlag = 0x0100;

    public static ushort ResponseFor(ushort command) => (ushort)(command | ResponseFlag);
}
