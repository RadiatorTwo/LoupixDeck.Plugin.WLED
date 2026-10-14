using LoupixDeck.PluginSdk;
using Zeroconf;

namespace LoupixDeck.Plugin.WLED;

/// <summary>
/// Finds WLED devices via mDNS (<c>_wled._tcp</c>) and resolves manually entered hosts.
/// Every hit is identified by <c>/json/info</c> and registered by MAC.
/// </summary>
public sealed class WledDiscovery(WledClient client, WledDeviceRegistry registry, IPluginLogger logger) : IDisposable
{
    private const string Protocol = "_wled._tcp.local.";
    private static readonly TimeSpan ScanTime = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _probeCts;
    private ZeroconfResolver.ResolverListener? _listener;

    /// <summary>Raised after a device was identified, so its lists can be loaded.</summary>
    public event Action<WledDevice>? DeviceResolved;

    /// <summary>Runs a background mDNS listener for two minutes. A new call supersedes the old one.</summary>
    public async Task ProbeAsync()
    {
        _probeCts?.Cancel();
        _probeCts?.Dispose();
        CancellationTokenSource cts = _probeCts = new CancellationTokenSource();

        _listener?.Dispose();
        ZeroconfResolver.ResolverListener listener = _listener = ZeroconfResolver.CreateListener(
            Protocol, queryInterval: 4000, pingsUntilRemove: 2, scanTime: ScanTime);

        listener.ServiceFound += (_, host) =>
        {
            // A callback still in flight after shutdown must not reach the plugin.
            if (!cts.IsCancellationRequested)
                _ = ResolveAsync(ToHost(host), cts.Token);
        };

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown or a rescan ended the probe early.
        }
        finally
        {
            listener.Dispose();
            if (ReferenceEquals(_listener, listener))
                _listener = null;
        }
    }

    /// <summary>One bounded mDNS scan, for the settings page. Returns the number of devices found.</summary>
    public async Task<int> ScanOnceAsync()
    {
        IReadOnlyList<IZeroconfHost> hosts = await ZeroconfResolver.ResolveAsync(Protocol, ScanTime);
        WledDevice?[] resolved = await Task.WhenAll(hosts.Select(h => ResolveAsync(ToHost(h), CancellationToken.None)));
        return resolved.Count(d => d != null);
    }

    /// <summary>
    /// Identifies the device at <paramref name="host"/>; returns null when unreachable. Retries pass
    /// <paramref name="logFailure"/> = false so an offline device does not flood the log.
    /// </summary>
    public async Task<WledDevice?> ResolveAsync(string host, CancellationToken ct, bool logFailure = true)
    {
        try
        {
            WledInfo info = await client.GetInfoAsync(host, ct);
            if (ct.IsCancellationRequested)
                return null;

            WledDevice device = registry.Upsert(info, host);
            DeviceResolved?.Invoke(device);
            return device;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (logFailure)
                logger.Warn($"WLED device at '{host}' not reachable: {ex.Message}");
            return null;
        }
    }

    private static string ToHost(IZeroconfHost host)
    {
        int port = host.Services.Values.FirstOrDefault()?.Port ?? 80;
        return port == 80 ? host.IPAddress : $"{host.IPAddress}:{port}";
    }

    public void Dispose() => Stop();

    /// <summary>Ends a running background probe. <see cref="ProbeAsync"/> can start a new one.</summary>
    public void Stop()
    {
        _probeCts?.Cancel();
        _probeCts?.Dispose();
        _probeCts = null;
        _listener?.Dispose();
        _listener = null;
    }
}
