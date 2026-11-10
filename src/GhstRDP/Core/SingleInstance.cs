namespace GhstRDP.Core;

/// <summary>Garante uma única instância; a segunda só traz a primeira para frente.</summary>
public sealed class SingleInstance : IDisposable
{
    static readonly string Suffix = Paths.InstanceKey == "default" ? "" : "." + Paths.InstanceKey;
    static readonly string MutexName = @"Local\GhstRDP.SingleInstance.v1" + Suffix;
    static readonly string EventName = @"Local\GhstRDP.Activate.v1" + Suffix;

    readonly Mutex _mutex;
    readonly EventWaitHandle _event;
    volatile bool _disposed;

    public event Action? ActivationRequested;

    SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        new Thread(Listen) { IsBackground = true, Name = "GhstRDP.SingleInstance" }.Start();
    }

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew) return new SingleInstance(mutex);
        mutex.Dispose();
        return null;
    }

    public static void SignalExisting()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(EventName);
            ev.Set();
        }
        catch
        {
            // a outra instância pode estar fechando
        }
    }

    void Listen()
    {
        while (!_disposed)
        {
            try
            {
                if (_event.WaitOne(500) && !_disposed) ActivationRequested?.Invoke();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        _mutex.Dispose();
        _event.Dispose();
    }
}
