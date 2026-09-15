namespace SenheiserControl.Services;

internal static class HeadsetCommands
{
    public const ushort AncSet = 0x1A04;
    public const ushort AncGet = 0x1A05;
    public const ushort TransparencySet = 0x1A02;
    public const ushort TransparencyGet = 0x1A03;
    public const ushort BassBoostSet = 0x1008;
    public const ushort BassBoostGet = 0x1009;

    // ANC sub-modes and Sound Mode. Confirmed against the MIT-licensed m4-companion
    // (Swift) source: AncModeSet payload = [modeId, state] (antiWind=1/comfort=2/adaptive=3,
    // antiWind state is off=0/max=1/auto=2, comfort/adaptive state is boolean).
    // AncModesGet response = repeating [modeId, state] pairs (>=6 bytes).
    public const ushort AncModeSet = 0x1A00;
    public const ushort AncModesGet = 0x1A01;
    public const ushort TransparentHearingSet = 0x1804;
    public const ushort TransparentHearingGet = 0x1805;

    /// <summary>Sound Mode: off=0/equalizer=1/podcast=2/soundPersonalization=3. Payload = [0, mode].</summary>
    public const ushort AudioModeSet = 0x0803;
    public const ushort SoundModeGet = 0x0804;

    // 5-band EQ. Payloads confirmed against the MIT-licensed m4-companion (Swift) source:
    // EqConfigGet response = [bandCount, minGainTenthsDb(sbyte), maxGainTenthsDb(sbyte)];
    // EqBandSet/Get payload = [index, gainTenthsDb(sbyte)].
    public const ushort EqConfigGet = 0x1000;
    public const ushort EqBandSet = 0x1001;
    public const ushort EqBandGet = 0x1002;

    /// <summary>Battery percentage. Request payload empty; response payload[0] = percent 0-100.</summary>
    public const ushort BatteryGet = 0x0603;

    // Feature 10 - Device Management. Confirmed against hardware by the ohr project
    // (reads) and m4-companion (writes). Connect/Disconnect payload = [peerIndex].
    public const ushort PairedDeviceCountGet = 0x1400;
    public const ushort PeerDetailsGet = 0x1401;
    public const ushort Connect = 0x1402;
    public const ushort Disconnect = 0x1403;
    public const ushort ConnectionStatusGet = 0x1404;
    public const ushort OwnPeerIndexGet = 0x1407;
    public const ushort MaxConnectionsGet = 0x1409;

    /// <summary>Alternate failure response for Connect - device sends this instead of the normal ResponseFor(Connect) on error.</summary>
    public const ushort ConnectError = 0x1582;

    public const ushort ResponseFlag = 0x0100;
    public const ushort FailureFlag = 0x0080;

    public static ushort ResponseFor(ushort command) => (ushort)(command | ResponseFlag);
}
