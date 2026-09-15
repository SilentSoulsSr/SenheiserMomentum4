namespace SenheiserControl.Services;

public readonly record struct PeerInfo(byte Index, byte DeviceType, byte ConnectionType, bool NonClassicPairing, string Name)
{
    public string ConnectionTypeLabel => ConnectionType switch
    {
        0 => "Disconnected",
        1 => "Classic",
        2 => "BLE",
        3 => "Classic + BLE",
        _ => "Unknown"
    };
}
