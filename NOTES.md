# Sennheiser Momentum 4 PC Control — research notes

Goal: control ANC / transparency / bass boost on Sennheiser Momentum 4 (over-ear) from Windows, no Android emulator. Stack decision pending: user prefers **C#** where possible.

## Protocol (reverse-engineered by community, MIT-licensed sources)

Bluetooth Classic RFCOMM, GAIA-v3-style framing:

```
MAGIC (2 bytes)   = FF 03
Length (2 bytes, big-endian) = length of payload
Vendor (2 bytes, big-endian) = 0x0495 (Sennheiser)
Command (2 bytes, big-endian)
Payload (variable)
```

Response command = request command | 0x0100.

RFCOMM service UUID: `A2129FF3-081B-4C45-8AFE-469D9C4842EC`

Known commands (single-byte payloads, 0x00=off/0x01=on, transparency 0x00-0x64):
- ANC: set 0x1A04, get 0x1A05
- Transparency: set 0x1A02, get 0x1A03
- Bass boost: set 0x1008, get 0x1009

Battery command not yet found/documented.

## Reference repos
- https://github.com/f3Y0/momentum4-control — Linux/KDE Python, source of the command codes above, MIT.
- https://github.com/jarek102/ohr — cross-platform-designed Python codec + protocol spec/test vectors, MIT. Targets ACCENTUM Wireless / MOMENTUM True Wireless 4 (earbuds), not confirmed for the over-ear Momentum 4.
- https://github.com/zaval/sennheiser-desktop-client — Qt/C++, early-stage (11 stars, 6 commits), no Windows build instructions or prebuilt binaries.

## Windows implementation approach
Windows has no raw AF_BLUETOOTH/RFCOMM socket like Linux. Use the WinRT `Windows.Devices.Bluetooth.Rfcomm` API:
- First-class/native from C# via CsWinRT (no extra package needed beyond target Windows SDK).
- From Python would need third-party `winrt-*` PyPI wrapper packages (not used — project is going C#).

Flow: enumerate paired Bluetooth devices → find headset by name → get `RfcommDeviceService` for the UUID above → open `StreamSocket` → wrap in `DataReader`/`DataWriter` → send/receive GAIA-v3 frames.

## Open decisions (as of last session)
- App shape: tray app vs WPF/WinUI window vs CLI tool
- .NET version: proposed .NET 8 (LTS)
- v1 scope: proposed ANC on/off + transparency toggle first, bass boost/battery later (battery needs more reverse-engineering)

## Status
No code written yet against real hardware. Previous session's Python venv scaffold was deleted at user's request (wrong stack — should be C#). Project folder: `D:\Dev\_Silentsoft\SenheiserMomentum4`.
