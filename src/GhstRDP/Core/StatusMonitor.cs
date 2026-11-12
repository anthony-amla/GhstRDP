using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Threading;
using GhstRDP.ViewModels;

namespace GhstRDP.Core;

/// <summary>Periodically checks whether each server's RDP port accepts a TCP connection.</summary>
public sealed class StatusMonitor : IDisposable
{
    readonly Func<IReadOnlyList<ServerItemViewModel>> _snapshot;
    readonly Dispatcher _ui;
    readonly CancellationTokenSource _cts = new();
    readonly SemaphoreSlim _parallel = new(16);

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    public StatusMonitor(Func<IReadOnlyList<ServerItemViewModel>> snapshot, Dispatcher ui)
    {
        _snapshot = snapshot;
        _ui = ui;
    }

    public void Start() => _ = LoopAsync(_cts.Token);

    async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Enabled)
            {
                try { await CheckAllAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Log.Warn("Status check failed", ex); }
            }
            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task CheckAllAsync(CancellationToken ct = default)
    {
        var items = await _ui.InvokeAsync(_snapshot);
        await Task.WhenAll(items.Select(i => CheckAsync(i, ct)));
    }

    public async Task CheckAsync(ServerItemViewModel item, CancellationToken ct = default)
    {
        await _parallel.WaitAsync(ct);
        try
        {
            var (host, port) = await _ui.InvokeAsync(() =>
            {
                if (item.Status == ServerStatus.Unknown) item.Status = ServerStatus.Checking;
                return (item.Host, item.Port);
            });
            var (online, ms) = await ProbeAsync(host, port, ct);
            await _ui.InvokeAsync(() => item.SetStatus(online, ms));
        }
        finally
        {
            _parallel.Release();
        }
    }

    /// <returns>Whether the port answered within 2.5 s, and the connect time in milliseconds.</returns>
    public static async Task<(bool Online, int Ms)> ProbeAsync(string host, int port, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) return (false, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(2500));
        using var tcp = new TcpClient();
        var sw = Stopwatch.StartNew();
        try
        {
            await tcp.ConnectAsync(host.Trim(), port, timeout.Token);
            return (true, (int)Math.Max(1, sw.ElapsedMilliseconds));
        }
        catch
        {
            return (false, 0);
        }
    }

    public enum ProbeResult { Online, Refused, NotFound, NoResponse }

    /// <summary>Pre-connect check that tells a closed port or unknown host apart from a slow network.</summary>
    public static async Task<ProbeResult> ProbeDetailedAsync(string host, int port, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        using var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host.Trim(), port, cts.Token);
            return ProbeResult.Online;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return ProbeResult.Refused;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
        {
            return ProbeResult.NotFound;
        }
        catch
        {
            return ProbeResult.NoResponse;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
