using System.Diagnostics;
using System.Net.Sockets;
using System.Windows.Threading;
using GhstRDP.ViewModels;

namespace GhstRDP.Core;

/// <summary>Verifica periodicamente se a porta RDP de cada servidor responde (TCP).</summary>
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
                catch (Exception ex) { Log.Warn("Falha na verificação de status", ex); }
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
                if (item.Status == ServerStatus.Desconhecido) item.Status = ServerStatus.Verificando;
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

    public enum ProbeResult { Online, Recusado, NaoEncontrado, SemResposta }

    /// <summary>Teste rápido antes de conectar: distingue porta fechada/DNS (falha certa) de lentidão.</summary>
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
            return ProbeResult.Recusado;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
        {
            return ProbeResult.NaoEncontrado;
        }
        catch
        {
            return ProbeResult.SemResposta;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
