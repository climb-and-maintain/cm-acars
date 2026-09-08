namespace ClimbAndMaintain.Acars.App.Configuration;

public sealed class DesktopSettingsCoordinator : IDisposable
{
    private readonly DesktopSettingsStore store;
    private readonly SemaphoreSlim updateLock = new(1, 1);
    private bool disposed;

    public DesktopSettingsCoordinator(DesktopSettingsStore store, DesktopSettings initialSettings)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        Current = initialSettings ?? throw new ArgumentNullException(nameof(initialSettings));
    }

    public DesktopSettings Current { get; private set; }

    public event EventHandler? SettingsChanged;

    public async ValueTask<DesktopSettings> UpdateAsync(
        Func<DesktopSettings, DesktopSettings> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ObjectDisposedException.ThrowIf(disposed, this);
        await updateLock.WaitAsync(cancellationToken);
        try
        {
            DesktopSettings updated = update(Current)
                ?? throw new InvalidOperationException("The settings update returned no settings.");
            await store.SaveAsync(updated, cancellationToken);
            Current = updated;
        }
        finally
        {
            updateLock.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return Current;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        updateLock.Dispose();
    }
}
