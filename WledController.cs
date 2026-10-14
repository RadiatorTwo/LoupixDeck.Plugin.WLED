using System.Text.Json.Nodes;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.WLED;

/// <summary>
/// Owns the WLED client, polls device state and sends state changes. Dial changes go through
/// <see cref="SendLatest"/>, which keeps at most one request per device and key in flight, so a
/// fast-turned dial does not flood the controller.
/// </summary>
public sealed class WledController : IDisposable
{
    private readonly IPluginHost _host;
    private readonly WledClient _client = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _pendingLock = new();
    private readonly Dictionary<string, JsonObject?> _pending = [];
    private readonly Lock _manualLock = new();
    private readonly Lock _powerLock = new();
    private readonly Dictionary<string, string> _powerStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unresolvedHosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Commands whose button visuals depend on device state.</summary>
    private readonly List<string> _refreshCommands = [];

    public WledController(IPluginHost host)
    {
        _host = host;
        Registry = new WledDeviceRegistry(host.Settings);
        Discovery = new WledDiscovery(_client, Registry, host.Logger);
        Discovery.DeviceResolved += device => _ = LoadListsAsync(device);
    }

    public WledDeviceRegistry Registry { get; }
    public WledDiscovery Discovery { get; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    public void RegisterRefreshCommand(string commandName) => _refreshCommands.Add(commandName);

    public void Start()
    {
        foreach (WledDevice device in Registry.All)
            _ = LoadListsAsync(device);
        _ = PollLoopAsync(_cts.Token);
    }

    /// <summary>Resolves manually entered hosts, separated by line breaks, commas or semicolons.</summary>
    public Task<WledDevice?[]> ResolveManualHostsAsync(string? hosts)
    {
        string[] entries = (hosts ?? string.Empty)
            .Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lock (_manualLock)
        {
            _unresolvedHosts.Clear();
            _unresolvedHosts.UnionWith(entries);
        }
        return Task.WhenAll(entries.Select(h => ResolveManualHostAsync(h, logFailure: true)));
    }

    /// <summary>Unreachable manual hosts stay in the retry set and are tried again on every poll tick.</summary>
    private async Task<WledDevice?> ResolveManualHostAsync(string host, bool logFailure)
    {
        WledDevice? device = await Discovery.ResolveAsync(host, _cts.Token, logFailure);
        if (device != null)
        {
            lock (_manualLock)
                _unresolvedHosts.Remove(host);
        }
        return device;
    }

    public async Task LoadListsAsync(WledDevice device)
    {
        try
        {
            await _client.RefreshListsAsync(device, _cts.Token);
            await _client.RefreshStateAsync(device, _cts.Token);
            RefreshButtons();
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            device.Online = false;
            _host.Logger.Warn($"WLED '{device.DisplayName}': loading lists failed: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
            return;
        }

        PublishPowerState(device);
    }

    /// <summary>Sends a state change and updates the cache from the response.</summary>
    public async Task SendAsync(WledDevice device, JsonObject patch)
    {
        try
        {
            await _client.PostStateAsync(device, patch, _cts.Token);
        }
        catch (Exception ex) when (!_cts.IsCancellationRequested)
        {
            device.Online = false;
            _host.Logger.Warn($"WLED '{device.DisplayName}': request failed: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            return;
        }

        PublishPowerState(device);
        RefreshButtons();
    }

    /// <summary>
    /// Sends <paramref name="patch"/>, but if a request with the same <paramref name="key"/> is
    /// still in flight for this device, only the latest patch is sent after it completes.
    /// </summary>
    public void SendLatest(WledDevice device, string key, JsonObject patch)
    {
        string slot = device.Mac + "|" + key;
        lock (_pendingLock)
        {
            if (_pending.ContainsKey(slot))
            {
                _pending[slot] = patch;
                return;
            }
            _pending[slot] = null;
        }

        _ = DrainAsync(device, slot, patch);
    }

    private async Task DrainAsync(WledDevice device, string slot, JsonObject patch)
    {
        JsonObject? next = patch;
        while (next != null)
        {
            await SendAsync(device, next);
            lock (_pendingLock)
            {
                next = _pending[slot];
                if (next == null)
                    _pending.Remove(slot);
                else
                    _pending[slot] = null;
            }
        }
    }

    /// <summary>
    /// Switches the power buttons to the device's state when it changed. The host addresses states by
    /// command name only, so all power buttons follow the device that changed last.
    /// </summary>
    private void PublishPowerState(WledDevice device)
    {
        string state = WledPowerStates.Of(device);
        lock (_powerLock)
        {
            if (_powerStates.TryGetValue(device.Mac, out string? last) && (last == state))
                return;
        }

        try
        {
            // False means no button changed (none assigned yet, or already in that state). The state
            // is then not remembered, so a button assigned later gets it on the next poll.
            if (!_host.SetActiveButtonState(WledCommandNames.Power, state))
                return;
            lock (_powerLock)
                _powerStates[device.Mac] = state;
        }
        catch (Exception ex)
        {
            _host.Logger.Warn($"WLED: switching the power state failed: {ex.Message}");
        }
    }

    public void RefreshButtons()
    {
        foreach (string command in _refreshCommands)
        {
            try
            {
                _host.RequestButtonRefresh(command);
            }
            catch (Exception ex)
            {
                _host.Logger.Warn($"WLED: button refresh for '{command}' failed: {ex.Message}");
            }
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            string[] retry;
            lock (_manualLock)
                retry = [.. _unresolvedHosts];
            foreach (string host in retry)
                _ = ResolveManualHostAsync(host, logFailure: false);

            bool changed = false;
            await Task.WhenAll(Registry.All.Select(async device =>
            {
                string before = Snapshot(device);
                try
                {
                    await _client.RefreshStateAsync(device, ct);
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // Offline devices are retried on the next tick; no log spam.
                    device.Online = false;
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (before != Snapshot(device))
                    changed = true;
                PublishPowerState(device);

                // A device that was offline at startup gets its names once it answers.
                if (device.Online && (device.Effects.Count == 0))
                    _ = LoadListsAsync(device);
            }));

            if (changed)
                RefreshButtons();
        }
    }

    private static string Snapshot(WledDevice d) =>
        $"{d.Online}|{d.On}|{d.Brightness}|{d.Preset}|{d.Effect}|{d.Palette}|{d.Speed}|{d.Intensity}";

    public void Dispose()
    {
        _cts.Cancel();
        Discovery.Dispose();
        _client.Dispose();
        // _cts is not disposed: late continuations may still read its token.
    }
}
