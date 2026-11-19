using System.Diagnostics;
using System.Net.NetworkInformation;
using TaskbarWidget.Interop;
using TaskbarWidget.Models;

namespace TaskbarWidget.Services;

public sealed class SystemMonitorService : IDisposable
{
    private readonly SettingsService _settings;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    // Previous samples for delta computation.
    private ulong _prevIn;
    private ulong _prevOut;
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private long _prevTimestamp;
    private bool _initialized;

    public event EventHandler<SystemStats>? StatsUpdated;

    public SystemMonitorService(SettingsService settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        Stop();
        _initialized = false;
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.Current.RefreshMilliseconds));
        _loop = Task.Run(() => LoopAsync(_cts!.Token));
    }

    public void RestartTimer() => Start();

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _timer?.Dispose();
        _timer = null;
        _loop = null;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var stats = Sample();
            StatsUpdated?.Invoke(this, stats);
            try
            {
                if (_timer is not null)
                    await _timer.WaitForNextTickAsync(token);
                else
                    await Task.Delay(1000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private (ulong In, ulong Out) ReadNetworkCounters()
    {
        ulong inTotal = 0, outTotal = 0;
        var all = NetworkInterface.GetAllNetworkInterfaces();
        var mode = _settings.Current.NicMode;

        if (mode == NicMode.Primary)
        {
            uint index = NativeMethods.GetPrimaryInterfaceIndex();
            var primary = all.FirstOrDefault(n =>
                n.GetIPProperties().GetIPv4Properties()?.Index == index);
            if (primary is not null)
            {
                var s = primary.GetIPv4Statistics();
                return ((ulong)Math.Max(0, s.BytesReceived), (ulong)Math.Max(0, s.BytesSent));
            }
            return (0, 0);
        }

        if (mode == NicMode.Selected)
        {
            var selected = all.FirstOrDefault(n =>
                string.Equals(n.Name, _settings.Current.SelectedNicAlias, StringComparison.OrdinalIgnoreCase)
                || string.Equals(n.Description, _settings.Current.SelectedNicAlias, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                var s = selected.GetIPv4Statistics();
                return ((ulong)Math.Max(0, s.BytesReceived), (ulong)Math.Max(0, s.BytesSent));
            }
            return (0, 0);
        }

        foreach (var nic in all)
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            var s = nic.GetIPv4Statistics();
            inTotal += (ulong)Math.Max(0, s.BytesReceived);
            outTotal += (ulong)Math.Max(0, s.BytesSent);
        }
        return (inTotal, outTotal);
    }

    public List<string> GetInterfaceAliases()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .Select(n => n.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private SystemStats Sample()
    {
        var stats = new SystemStats();
        long now = Stopwatch.GetTimestamp();

        var (inOctets, outOctets) = ReadNetworkCounters();

        _ = NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user);

        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        _ = NativeMethods.GlobalMemoryStatusEx(ref mem);
        stats.MemoryPercent = mem.dwMemoryLoad;
        stats.MemoryTotalGb = mem.ullTotalPhys / 1024.0 / 1024 / 1024;
        stats.MemoryUsedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1024.0 / 1024 / 1024;

        if (!_initialized)
        {
            _prevIn = inOctets;
            _prevOut = outOctets;
            _prevIdle = idle.Value;
            _prevKernel = kernel.Value;
            _prevUser = user.Value;
            _prevTimestamp = now;
            _initialized = true;
            return stats;
        }

        double seconds = (now - _prevTimestamp) / (double)Stopwatch.Frequency;
        if (seconds > 0)
        {
            ulong dIn = inOctets >= _prevIn ? inOctets - _prevIn : 0;
            ulong dOut = outOctets >= _prevOut ? outOctets - _prevOut : 0;
            stats.DownloadBps = dIn / seconds;
            stats.UploadBps = dOut / seconds;

            ulong dIdle = idle.Value >= _prevIdle ? idle.Value - _prevIdle : 0;
            ulong dKernel = kernel.Value >= _prevKernel ? kernel.Value - _prevKernel : 0;
            ulong dUser = user.Value >= _prevUser ? user.Value - _prevUser : 0;
            ulong total = dKernel + dUser; // kernel time includes idle time
            if (total > 0)
                stats.CpuPercent = 100.0 * (1.0 - dIdle / (double)total);
            stats.CpuPercent = Math.Clamp(stats.CpuPercent, 0, 100);
        }

        _prevIn = inOctets;
        _prevOut = outOctets;
        _prevIdle = idle.Value;
        _prevKernel = kernel.Value;
        _prevUser = user.Value;
        _prevTimestamp = now;

        return stats;
    }

    public void Dispose() => Stop();
}
