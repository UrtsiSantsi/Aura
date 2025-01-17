using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tmds.DBus;

namespace Nickvision.Aura.Network;

/// <summary>
/// Network state monitor
/// </summary>
public class NetworkMonitor : IDisposable
{
    private bool _disposed;
    private Connection? _dbusConnection;
    private INetworkManager? _networkManager;
    private string[]? _networkAddresses;
    
    /// <summary>
    /// Occurs when the network state is changed
    /// </summary>
    /// <remarks>The boolean value is true if there is an internet connection, else false</remarks>
    private event EventHandler<bool>? _stateChanged;

    /// <summary>
    /// Construct NetworkMonitor
    /// </summary>
    private NetworkMonitor()
    {
        _disposed = false;
    }

    /// <summary>
    /// Create and setup new network monitor
    /// </summary>
    /// <returns>Network monitor</returns>
    public static async Task<NetworkMonitor> NewAsync()
    {
        var netmon = new NetworkMonitor();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                netmon._dbusConnection = new Connection(Address.System);
                await netmon._dbusConnection.ConnectAsync();
                netmon._networkManager = netmon._dbusConnection.CreateProxy<INetworkManager>("org.freedesktop.NetworkManager", new ObjectPath("/org/freedesktop/NetworkManager"));
                await netmon._networkManager.WatchStateChangedAsync(state => netmon._stateChanged?.Invoke(netmon, state switch
                {
                    NetworkState.ConnectedGlobal => true,
                    NetworkState.ConnectedSite => true,
                    NetworkState.Unknown => true,
                    _ => false
                }));
            }
            catch
            {
                netmon._networkManager = null;
                netmon.SetupPing();
            }
        }
        else
        {
            netmon.SetupPing();
        }
        return netmon;
    }

    /// <summary>
    /// Finalizes the NetworkMonitor
    /// </summary>
    ~NetworkMonitor() => Dispose(false);
    
    /// <summary>
    /// Occurs when network state is changed
    /// </summary>
    public event EventHandler<bool> StateChanged
    {
        add
        {
            _stateChanged += value;
            Task.Run(async () => _stateChanged.Invoke(this, await GetStateAsync()));
        }

        remove
        {
            _stateChanged -= value;
        }
    }

    /// <summary>
    /// Frees resources used by the NetworkMonitor object
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Frees resources used by the NetworkMonitor object
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }
        _dbusConnection?.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Get current network state
    /// </summary>
    /// <returns>True if internet connection, else false</returns>
    public async Task<bool> GetStateAsync()
    {
        if (_networkManager != null)
        {
            return (await _networkManager.GetAsync<NetworkState>("State")) switch
            {
                NetworkState.ConnectedGlobal => true,
                NetworkState.ConnectedSite => true,
                NetworkState.ConnectedLocal => true,
                NetworkState.Unknown => true,
                _ => false
            };
        }
        return await PingReliableSitesAsync();
    }
    
    /// <summary>
    /// Setup network monitor to use dotnet NetworkChange and ping
    /// </summary>
    private void SetupPing()
    {
        _networkAddresses = new []{ "8.8.8.8", "http://www.baidu.com", "http://www.aparat.com" };
        NetworkChange.NetworkAvailabilityChanged += async (sender, e) =>
        {
            _stateChanged?.Invoke(this, await PingReliableSitesAsync());
        };
    }
    
    /// <summary>
    /// Ping reliable web sites to ensure network connection
    /// </summary>
    /// <returns>True if internet connection, else false</returns>
    private async Task<bool> PingReliableSitesAsync()
    {
        foreach (var addr in _networkAddresses!)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(addr);
                if (reply.Status == IPStatus.Success)
                {
                    return true;
                }
            }
            catch (PlatformNotSupportedException)
            {
                return true;
            }
            catch { }
        }
        return false;
    }
}