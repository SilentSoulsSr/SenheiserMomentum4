using System.Collections.Concurrent;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace SenheiserControl.Services;

/// <summary>
/// Talks to the Momentum 4 over its RFCOMM control channel. Pair the headset in Windows
/// Bluetooth settings first; this only opens the already-paired service, it doesn't pair.
/// </summary>
public sealed class HeadsetService : IAsyncDisposable
{
    private static readonly Guid ServiceUuid = Guid.Parse("A2129FF3-081B-4C45-8AFE-469D9C4842EC");

    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<GaiaFrame>> _pending = new();
    private readonly List<byte> _receiveBuffer = [];
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private StreamSocket? _socket;
    private DataWriter? _writer;
    private DataReader? _reader;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoopTask;

    private readonly CancellationTokenSource _lifetimeCts = new();
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

    public bool IsConnected { get; private set; }
    public bool AutoReconnect { get; private set; } = true;
    public bool? AncEnabled { get; private set; }
    public int? TransparencyLevel { get; private set; }
    public bool? BassBoostEnabled { get; private set; }
    public AntiWindMode? AntiWind { get; private set; }
    public bool? AncComfortEnabled { get; private set; }
    public bool? AncAdaptiveEnabled { get; private set; }
    public SoundMode? SoundMode { get; private set; }
    public NoiseControlMode NoiseControlMode =>
        AncEnabled != true ? Services.NoiseControlMode.Off :
        AncAdaptiveEnabled == true ? Services.NoiseControlMode.Adaptive : Services.NoiseControlMode.Custom;
    public IReadOnlyList<PeerInfo> Peers { get; private set; } = [];
    public byte? OwnPeerIndex { get; private set; }
    public byte? MaxConnections { get; private set; }
    public int? BatteryPercent { get; private set; }
    public int EqBandCount { get; private set; }
    public double EqMinGainDb { get; private set; }
    public double EqMaxGainDb { get; private set; }
    public IReadOnlyList<double> EqBandGains { get; private set; } = [];
    public string Status { get; private set; } = "Disconnected";

    public event Action? Changed;

    /// <summary>Starts a background loop that connects immediately and retries on drop, for the app's lifetime.</summary>
    public void StartAutoConnect() => _ = MaintainConnectionAsync(_lifetimeCts.Token);

    private async Task MaintainConnectionAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!IsConnected && AutoReconnect)
            {
                await ConnectAsync();
            }

            try { await Task.Delay(ReconnectInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task ConnectAsync()
    {
        AutoReconnect = true;
        Status = "Searching for headset...";
        Changed?.Invoke();

        try
        {
            string selector = RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(ServiceUuid));
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);
            if (devices.Count == 0)
            {
                Status = "Headset not found. Pair it in Windows Bluetooth settings first.";
                Changed?.Invoke();
                return;
            }

            RfcommDeviceService? service = await RfcommDeviceService.FromIdAsync(devices[0].Id);
            if (service is null)
            {
                Status = "Could not open the control channel.";
                Changed?.Invoke();
                return;
            }

            var socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName);

            _socket = socket;
            _writer = new DataWriter(socket.OutputStream);
            _reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

            IsConnected = true;
            Status = "Connected";
            _readLoopCts = new CancellationTokenSource();
            _readLoopTask = ReadLoopAsync(_readLoopCts.Token);
            Changed?.Invoke();

            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Status = $"Connection failed: {ex.Message}";
            Changed?.Invoke();
        }
    }

    public async Task DisconnectAsync()
    {
        AutoReconnect = false;
        _readLoopCts?.Cancel();
        if (_readLoopTask is not null)
        {
            try { await _readLoopTask; } catch { /* loop reports its own status */ }
        }

        _writer?.Dispose();
        _reader?.Dispose();
        _socket?.Dispose();
        _writer = null;
        _reader = null;
        _socket = null;

        IsConnected = false;
        Status = "Disconnected";
        Changed?.Invoke();
    }

    public async Task SetAncAsync(bool enabled)
    {
        await SendCommandAsync(HeadsetCommands.AncSet, [(byte)(enabled ? 1 : 0)], WriteResponses(HeadsetCommands.AncSet));
        AncEnabled = enabled;
        Changed?.Invoke();
    }

    public async Task SetTransparencyAsync(int level)
    {
        int clamped = Math.Clamp(level, 0, 100);
        await SendCommandAsync(HeadsetCommands.AncModeSet, [3, 0], WriteResponses(HeadsetCommands.AncModeSet));
        await SendCommandAsync(HeadsetCommands.TransparencySet, [(byte)clamped], WriteResponses(HeadsetCommands.TransparencySet));
        AncAdaptiveEnabled = false;
        TransparencyLevel = clamped;
        Changed?.Invoke();
    }

    public async Task SetBassBoostAsync(bool enabled)
    {
        await SendCommandAsync(HeadsetCommands.BassBoostSet, [(byte)(enabled ? 1 : 0)], WriteResponses(HeadsetCommands.BassBoostSet));
        BassBoostEnabled = enabled;
        Changed?.Invoke();
    }

    /// <summary>Off/Custom/Adaptive is a UI concept, not a single device command - this replays
    /// the exact write sequence m4-companion uses for each transition.</summary>
    public async Task SetNoiseControlModeAsync(NoiseControlMode mode)
    {
        switch (mode)
        {
            case NoiseControlMode.Off:
                await SetAncAsync(false);
                break;
            case NoiseControlMode.Custom:
                await SetTransparentHearingAsync(false);
                await SetAncAsync(true);
                await SendCommandAsync(HeadsetCommands.AncModeSet, [3, 0], WriteResponses(HeadsetCommands.AncModeSet));
                AncAdaptiveEnabled = false;
                break;
            case NoiseControlMode.Adaptive:
                await SetAncAsync(true);
                await SendCommandAsync(HeadsetCommands.AncModeSet, [3, 1], WriteResponses(HeadsetCommands.AncModeSet));
                AncAdaptiveEnabled = true;
                break;
        }
        await RefreshAncModesAsync();
    }

    public async Task SetAntiWindAsync(AntiWindMode mode)
    {
        await SendCommandAsync(HeadsetCommands.AncModeSet, [1, (byte)mode], WriteResponses(HeadsetCommands.AncModeSet));
        AntiWind = mode;
        Changed?.Invoke();
    }

    public async Task SetSoundModeAsync(SoundMode mode)
    {
        await SendCommandAsync(HeadsetCommands.AudioModeSet, [0, (byte)mode], WriteResponses(HeadsetCommands.AudioModeSet));
        SoundMode = mode;
        Changed?.Invoke();
    }

    private Task SetTransparentHearingAsync(bool enabled) =>
        SendCommandAsync(HeadsetCommands.TransparentHearingSet, [(byte)(enabled ? 1 : 0)],
            WriteResponses(HeadsetCommands.TransparentHearingSet));

    public async Task ApplyEqPresetAsync(IReadOnlyList<double> gainsDb)
    {
        for (int i = 0; i < gainsDb.Count && i < EqBandCount; i++)
        {
            try { await SetEqBandAsync(i, gainsDb[i]); } catch { /* apply what we can */ }
        }
    }

    private static ushort[] WriteResponses(ushort command)
    {
        ushort success = HeadsetCommands.ResponseFor(command);
        return [success, (ushort)(success | HeadsetCommands.FailureFlag)];
    }

    public async Task SetEqBandAsync(int index, double gainDb)
    {
        double clamped = Math.Clamp(gainDb, EqMinGainDb, EqMaxGainDb);
        sbyte raw = (sbyte)Math.Round(clamped * 10);
        await SendCommandAsync(HeadsetCommands.EqBandSet, [(byte)index, unchecked((byte)raw)],
            WriteResponses(HeadsetCommands.EqBandSet));

        // Set responses ack with an empty payload on this hardware - no value to read back,
        // so trust the value we just successfully sent.
        if (index >= 0 && index < EqBandGains.Count)
        {
            double[] updated = [.. EqBandGains];
            updated[index] = raw / 10.0;
            EqBandGains = updated;
            Changed?.Invoke();
        }
    }

    /// <summary>Connects to a paired peer by index, making it (or adding it as) an active connection.</summary>
    public async Task<bool> ConnectPeerAsync(byte index)
    {
        GaiaFrame frame = await SendCommandAsync(
            HeadsetCommands.Connect, [index],
            [HeadsetCommands.ResponseFor(HeadsetCommands.Connect), HeadsetCommands.ConnectError]);
        await RefreshPeersAsync();
        return frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.Connect);
    }

    public async Task DisconnectPeerAsync(byte index)
    {
        ushort success = HeadsetCommands.ResponseFor(HeadsetCommands.Disconnect);
        ushort failure = (ushort)(success | HeadsetCommands.FailureFlag);
        await SendCommandAsync(HeadsetCommands.Disconnect, [index], [success, failure]);
        await RefreshPeersAsync();
    }

    private async Task RefreshAllAsync()
    {
        ushort[] getCommands = [HeadsetCommands.AncGet, HeadsetCommands.TransparencyGet, HeadsetCommands.BassBoostGet];
        foreach (ushort getCommand in getCommands)
        {
            try { await SendCommandAsync(getCommand, []); }
            catch { /* one missing status shouldn't block the others */ }
        }

        await RefreshBatteryAsync();
        await RefreshEqConfigAsync();
        await RefreshAncModesAsync();
        await RefreshSoundModeAsync();
        await RefreshPeersAsync();
    }

    private async Task RefreshAncModesAsync()
    {
        try
        {
            GaiaFrame frame = await SendCommandAsync(HeadsetCommands.AncModesGet, []);
            if (frame.Payload.Length < 6 || frame.Payload.Length % 2 != 0)
            {
                return;
            }

            for (int offset = 0; offset < frame.Payload.Length; offset += 2)
            {
                byte modeId = frame.Payload[offset];
                byte state = frame.Payload[offset + 1];
                switch (modeId)
                {
                    case 1: AntiWind = (AntiWindMode)state; break;
                    case 2: AncComfortEnabled = state != 0; break;
                    case 3: AncAdaptiveEnabled = state != 0; break;
                }
            }
            Changed?.Invoke();
        }
        catch { /* best-effort */ }
    }

    private async Task RefreshSoundModeAsync()
    {
        try
        {
            GaiaFrame frame = await SendCommandAsync(HeadsetCommands.SoundModeGet, []);
            if (frame.Payload.Length >= 2 && frame.Payload[0] == 0)
            {
                SoundMode = (SoundMode)frame.Payload[1];
                Changed?.Invoke();
            }
        }
        catch { /* best-effort */ }
    }

    private async Task RefreshBatteryAsync()
    {
        try
        {
            GaiaFrame frame = await SendCommandAsync(HeadsetCommands.BatteryGet, []);
            BatteryPercent = frame.Payload.Length > 0 ? frame.Payload[0] : null;
            Changed?.Invoke();
        }
        catch { /* battery is best-effort */ }
    }

    private async Task RefreshEqConfigAsync()
    {
        try
        {
            GaiaFrame configFrame = await SendCommandAsync(HeadsetCommands.EqConfigGet, []);
            if (configFrame.Payload.Length < 3)
            {
                return;
            }

            EqBandCount = configFrame.Payload[0];
            EqMinGainDb = (sbyte)configFrame.Payload[1] / 10.0;
            EqMaxGainDb = (sbyte)configFrame.Payload[2] / 10.0;

            var gains = new List<double>();
            for (byte i = 0; i < EqBandCount; i++)
            {
                try
                {
                    GaiaFrame bandFrame = await SendCommandAsync(HeadsetCommands.EqBandGet, [i]);
                    // This hardware replies with just [gain] (1 byte), not [index, gain] - fall back
                    // to the last byte either way so both shapes work.
                    gains.Add(bandFrame.Payload.Length > 0 ? (sbyte)bandFrame.Payload[^1] / 10.0 : 0);
                }
                catch { gains.Add(0); }
            }
            EqBandGains = gains;
            Changed?.Invoke();
        }
        catch { /* EQ is best-effort; core noise-control status matters more */ }
    }

    private async Task RefreshPeersAsync()
    {
        try
        {
            GaiaFrame countFrame = await SendCommandAsync(HeadsetCommands.PairedDeviceCountGet, []);
            int count = countFrame.Payload.Length >= 2
                ? (countFrame.Payload[0] << 8) | countFrame.Payload[1]
                : 0;

            var peers = new List<PeerInfo>();
            for (byte i = 0; i < count; i++)
            {
                try
                {
                    GaiaFrame peerFrame = await SendCommandAsync(HeadsetCommands.PeerDetailsGet, [i]);
                    peers.Add(ParsePeer(peerFrame.Payload));
                }
                catch { /* one missing peer shouldn't block the rest */ }
            }
            Peers = peers;

            GaiaFrame ownFrame = await SendCommandAsync(HeadsetCommands.OwnPeerIndexGet, []);
            OwnPeerIndex = ownFrame.Payload.Length > 0 ? ownFrame.Payload[0] : null;

            GaiaFrame maxFrame = await SendCommandAsync(HeadsetCommands.MaxConnectionsGet, []);
            MaxConnections = maxFrame.Payload.Length > 0 ? maxFrame.Payload[0] : null;
        }
        catch
        {
            // connection list is best-effort; core noise-control status matters more
        }
    }

    private static PeerInfo ParsePeer(byte[] payload)
    {
        byte index = payload.Length > 0 ? payload[0] : (byte)0;
        byte deviceType = payload.Length > 1 ? payload[1] : (byte)0;
        byte status = payload.Length > 2 ? payload[2] : (byte)0;
        byte connectionType = (byte)(status & 0x03);
        bool nonClassicPairing = (status & 0x80) != 0;

        string name = string.Empty;
        if (payload.Length > 3)
        {
            int nullIndex = Array.IndexOf(payload, (byte)0, 3);
            int nameLength = (nullIndex >= 0 ? nullIndex : payload.Length) - 3;
            name = System.Text.Encoding.UTF8.GetString(payload, 3, nameLength);
        }

        return new PeerInfo(index, deviceType, connectionType, nonClassicPairing, name);
    }

    private Task<GaiaFrame> SendCommandAsync(ushort command, byte[] payload, TimeSpan? timeout = null) =>
        SendCommandAsync(command, payload, [HeadsetCommands.ResponseFor(command)], timeout);

    private async Task<GaiaFrame> SendCommandAsync(ushort command, byte[] payload, ushort[] expectedResponses, TimeSpan? timeout = null)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        var tcs = new TaskCompletionSource<GaiaFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (ushort responseCommand in expectedResponses)
        {
            _pending[responseCommand] = tcs;
        }

        await _writeLock.WaitAsync();
        try
        {
            var outgoing = new GaiaFrame(command, payload);
            LogFrame("TX", outgoing);
            byte[] frame = outgoing.Encode();
            _writer.WriteBytes(frame);
            await _writer.StoreAsync().AsTask();
            await _writer.FlushAsync().AsTask();
        }
        finally
        {
            _writeLock.Release();
        }

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        using var reg = cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException($"No response to command 0x{command:X4}.")));

        try
        {
            return await tcs.Task;
        }
        catch (Exception ex)
        {
            Status = $"Command 0x{command:X4} failed: {ex.Message}";
            Changed?.Invoke();
            throw;
        }
        finally
        {
            foreach (ushort responseCommand in expectedResponses)
            {
                _pending.TryRemove(new KeyValuePair<ushort, TaskCompletionSource<GaiaFrame>>(responseCommand, tcs));
            }
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                uint loaded = await _reader!.LoadAsync(1024).AsTask(ct);
                if (loaded == 0)
                {
                    break;
                }

                var chunk = new byte[loaded];
                _reader.ReadBytes(chunk);
                _receiveBuffer.AddRange(chunk);

                while (true)
                {
                    byte[] snapshot = _receiveBuffer.ToArray();
                    if (GaiaFrame.TryParse(snapshot, out GaiaFrame frame, out int consumed))
                    {
                        _receiveBuffer.RemoveRange(0, consumed);
                        HandleFrame(frame);
                    }
                    else if (consumed > 0)
                    {
                        _receiveBuffer.RemoveRange(0, consumed);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on disconnect
        }
        catch (Exception ex)
        {
            IsConnected = false;
            Status = $"Connection lost: {ex.Message}";
            Changed?.Invoke();
        }
    }

    private static readonly string ProtocolLogPath = Path.Combine(AppContext.BaseDirectory, "protocol.log");

    private static void LogFrame(string direction, GaiaFrame frame)
    {
        try
        {
            string hex = Convert.ToHexString(frame.Payload);
            File.AppendAllText(ProtocolLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {direction} 0x{frame.Command:X4} payload=[{hex}]\n");
        }
        catch { /* logging must never crash the app */ }
    }

    private void HandleFrame(GaiaFrame frame)
    {
        LogFrame("RX", frame);

        if (_pending.TryRemove(frame.Command, out TaskCompletionSource<GaiaFrame>? tcs))
        {
            tcs.TrySetResult(frame);
        }

        ApplyState(frame);
        Changed?.Invoke();
    }

    // Only GET responses carry a payload on this hardware - SET responses are empty acks,
    // so their new state is applied directly in each SetXAsync method instead of here.
    private void ApplyState(GaiaFrame frame)
    {
        if (frame.Payload.Length == 0)
        {
            return;
        }

        byte value = frame.Payload[0];
        if (frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.AncGet))
        {
            AncEnabled = value != 0;
        }
        else if (frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.TransparencyGet))
        {
            TransparencyLevel = value;
        }
        else if (frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.BassBoostGet))
        {
            BassBoostEnabled = value != 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetimeCts.CancelAsync();
        await DisconnectAsync();
        _writeLock.Dispose();
        _lifetimeCts.Dispose();
    }
}
