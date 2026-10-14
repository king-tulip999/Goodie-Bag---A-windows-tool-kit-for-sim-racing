using System.Net.NetworkInformation;
using GoodieBag.Config;

namespace GoodieBag.Network;

internal sealed record NetworkSnapshot(
    bool Connected,
    string? Adapter,
    string? AdapterKind,
    long? LinkSpeedBps,
    double DownBytesPerSec,
    double UpBytesPerSec,
    double? PingMs,
    double? LossPercent,
    int PingsSent,
    string PingHost);

internal sealed class NetworkMonitor : IDisposable
{
    public const int HistoryLength = 120;
    private const int LossWindow = 30;

    private readonly NetworkSettings _settings;
    private readonly HashSet<string> _consumers = new();
    private readonly object _gate = new();
    private readonly Queue<bool> _pingResults = new();
    private readonly List<double?> _down = new(), _up = new(), _ping = new();
    private System.Threading.Timer? _timer;
    private int _generation;
    private volatile bool _dirty = true;
    private int _sampling;
    private NetworkInterface[] _interfaces = Array.Empty<NetworkInterface>();
    private NetworkInterface? _main;
    private bool _available;
    private long _lastRx = -1, _lastTx = -1, _lastTicks;
    private long _nextPing;
    private volatile bool _pinging;
    private double? _lastPing;

    public NetworkMonitor(NetworkSettings settings)
    {
        _settings = settings;
    }

    public volatile NetworkSnapshot? Latest;

    public void Acquire(string consumer)
    {
        lock (_gate)
        {
            if (!_consumers.Add(consumer) || _consumers.Count > 1)
                return;
            int generation = ++_generation;
            _dirty = true;
            _timer?.Dispose();
            _timer = new System.Threading.Timer(_ => Sample(generation), null, 0, 1000);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (_gate)
                {
                    if (generation != _generation || _subscribed) return;
                    NetworkChange.NetworkAddressChanged += OnAddressChanged;
                    NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
                    _subscribed = true;
                }
            });
        }
    }

    private bool _subscribed;

    public void Release(string consumer)
    {
        lock (_gate)
        {
            if (!_consumers.Remove(consumer) || _consumers.Count > 0)
                return;
            _generation++;
            if (_subscribed)
            {
                NetworkChange.NetworkAddressChanged -= OnAddressChanged;
                NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
                _subscribed = false;
            }
            _timer?.Dispose();
            _timer = null;
            _down.Clear(); _up.Clear(); _ping.Clear(); _pingResults.Clear();
            _lastPing = null;
            _dirty = true;
            Latest = null;
        }
    }

    public (IReadOnlyList<double?> Down, IReadOnlyList<double?> Up, IReadOnlyList<double?> Ping) History()
    {
        lock (_gate)
            return (_down.ToList(), _up.ToList(), _ping.ToList());
    }

    private void OnAddressChanged(object? sender, EventArgs e) => _dirty = true;

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => _dirty = true;

    private void RefreshInterfaces()
    {
        try
        {
            _interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                            && HasAddress(n))
                .ToArray();
        }
        catch
        {
            _interfaces = Array.Empty<NetworkInterface>();
        }

        _main = null;
        foreach (var nic in _interfaces)
        {
            try
            {
                if (nic.GetIPProperties().GatewayAddresses.Count > 0)
                {
                    _main = nic;
                    break;
                }
            }
            catch { }
        }

        try { _available = NetworkInterface.GetIsNetworkAvailable(); }
        catch { _available = false; }

        _lastRx = _lastTx = -1;
    }

    private static bool HasAddress(NetworkInterface nic)
    {
        try { return nic.GetIPProperties().UnicastAddresses.Count > 0; }
        catch { return false; }
    }

    private void Sample(int generation)
    {
        if (Interlocked.Exchange(ref _sampling, 1) == 1)
            return;
        try
        {
            if (_dirty)
            {
                _dirty = false;
                RefreshInterfaces();
            }

            long rx = 0, tx = 0;
            foreach (var nic in _interfaces)
            {
                try
                {
                    var stats = nic.GetIPStatistics();
                    rx += stats.BytesReceived;
                    tx += stats.BytesSent;
                }
                catch { }
            }

            long now = Environment.TickCount64;
            double down = 0, up = 0;
            if (_lastRx >= 0 && now > _lastTicks)
            {
                double seconds = (now - _lastTicks) / 1000.0;
                down = Math.Max(0, (rx - _lastRx) / seconds);
                up = Math.Max(0, (tx - _lastTx) / seconds);
            }
            _lastRx = rx;
            _lastTx = tx;
            _lastTicks = now;

            if (now >= _nextPing && !_pinging)
            {
                _nextPing = now + Math.Clamp(_settings.PingIntervalSeconds, 1, 60) * 1000L;
                _ = PingAsync(generation);
            }

            var main = _main;
            bool connected = _available && main is not null;
            lock (_gate)
            {
                if (generation != _generation)
                    return;
                Push(_down, down);
                Push(_up, up);
                Push(_ping, _lastPing);
                int sent = _pingResults.Count;
                double? loss = sent >= 5 ? _pingResults.Count(ok => !ok) * 100.0 / sent : null;
                Latest = new NetworkSnapshot(connected, main?.Name, main is null ? null : Kind(main.NetworkInterfaceType),
                    main?.Speed > 0 ? main.Speed : null, down, up, connected ? _lastPing : null, loss, sent, _settings.PingHost);
            }
        }
        catch
        {
        }
        finally
        {
            Volatile.Write(ref _sampling, 0);
        }
    }

    private async Task PingAsync(int generation)
    {
        _pinging = true;
        bool ok = false;
        double? ms = null;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(_settings.PingHost, 1000);
            ok = reply.Status == IPStatus.Success;
            if (ok) ms = reply.RoundtripTime;
        }
        catch
        {
        }
        finally
        {
            _pinging = false;
        }
        lock (_gate)
        {
            if (generation != _generation)
                return;
            _lastPing = ms;
            _pingResults.Enqueue(ok);
            while (_pingResults.Count > LossWindow)
                _pingResults.Dequeue();
        }
    }

    private static void Push(List<double?> list, double? value)
    {
        list.Add(value);
        if (list.Count > HistoryLength)
            list.RemoveAt(0);
    }

    private static string Kind(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx => "Ethernet",
        NetworkInterfaceType.Ppp => "Dial-up / PPP",
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Mobile broadband",
        _ => type.ToString(),
    };

    public static string Bits(double bytesPerSecond)
    {
        double bits = bytesPerSecond * 8;
        return bits switch
        {
            >= 1e9 => $"{bits / 1e9:0.0} Gbit/s",
            >= 1e6 => $"{bits / 1e6:0.0} Mbit/s",
            >= 1e3 => $"{bits / 1e3:0} kbit/s",
            _ => $"{bits:0} bit/s",
        };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _consumers.Clear();
            _generation++;
            if (_subscribed)
            {
                NetworkChange.NetworkAddressChanged -= OnAddressChanged;
                NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
                _subscribed = false;
            }
            _timer?.Dispose();
            _timer = null;
        }
    }
}
