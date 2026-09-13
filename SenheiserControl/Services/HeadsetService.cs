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

    public bool IsConnected { get; private set; }
    public bool? AncEnabled { get; private set; }
    public int? TransparencyLevel { get; private set; }
    public bool? BassBoostEnabled { get; private set; }
    public string Status { get; private set; } = "Disconnected";

    public event Action? Changed;

    public async Task ConnectAsync()
    {
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

    public Task SetAncAsync(bool enabled) =>
        SendCommandAsync(HeadsetCommands.AncSet, [(byte)(enabled ? 1 : 0)]);

    public Task SetTransparencyAsync(int level) =>
        SendCommandAsync(HeadsetCommands.TransparencySet, [(byte)Math.Clamp(level, 0, 100)]);

    public Task SetBassBoostAsync(bool enabled) =>
        SendCommandAsync(HeadsetCommands.BassBoostSet, [(byte)(enabled ? 1 : 0)]);

    private async Task RefreshAllAsync()
    {
        ushort[] getCommands = [HeadsetCommands.AncGet, HeadsetCommands.TransparencyGet, HeadsetCommands.BassBoostGet];
        foreach (ushort getCommand in getCommands)
        {
            try { await SendCommandAsync(getCommand, []); }
            catch { /* one missing status shouldn't block the others */ }
        }
    }

    private async Task<GaiaFrame> SendCommandAsync(ushort command, byte[] payload, TimeSpan? timeout = null)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        ushort responseCommand = HeadsetCommands.ResponseFor(command);
        var tcs = new TaskCompletionSource<GaiaFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[responseCommand] = tcs;

        await _writeLock.WaitAsync();
        try
        {
            byte[] frame = new GaiaFrame(command, payload).Encode();
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
        return await tcs.Task;
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

    private void HandleFrame(GaiaFrame frame)
    {
        if (_pending.TryRemove(frame.Command, out TaskCompletionSource<GaiaFrame>? tcs))
        {
            tcs.TrySetResult(frame);
        }

        ApplyState(frame);
        Changed?.Invoke();
    }

    private void ApplyState(GaiaFrame frame)
    {
        if (frame.Payload.Length == 0)
        {
            return;
        }

        byte value = frame.Payload[0];
        if (frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.AncGet) ||
            frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.AncSet))
        {
            AncEnabled = value != 0;
        }
        else if (frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.TransparencyGet) ||
                 frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.TransparencySet))
        {
            TransparencyLevel = value;
        }
        else if (frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.BassBoostGet) ||
                 frame.Command == HeadsetCommands.ResponseFor(HeadsetCommands.BassBoostSet))
        {
            BassBoostEnabled = value != 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _writeLock.Dispose();
    }
}
