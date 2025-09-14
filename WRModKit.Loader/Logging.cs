// WRModKit.Loader/Logging/Logging.cs

using WRModKit.Loader.Contracts;

namespace WRModKit.Loader;

public sealed record LogEvent(DateTime Timestamp, string Source, LogLevel Level, string Message);

public interface ILogSink { bool Handle(in LogEvent e); }

public static class LoggerManager
{
    private static readonly List<ILogSink> Sinks = new();
    private static readonly object Lock = new();

    public static void AddSink(ILogSink sink)
    {
        lock (Lock) { Sinks.Add(sink); }
    }
    public static void RemoveSink(ILogSink sink)
    {
        lock (Lock) { Sinks.Remove(sink); }
    }
    public static ILogSource CreateSource(string name) => new LogSource(name);

    internal static void Publish(in LogEvent e)
    {
        ILogSink[] sinks;
        lock (Lock) sinks = Sinks.ToArray();
        foreach (var s in sinks) { try { s.Handle(e); } catch { } }
    }

    private sealed class LogSource : ILogSource
    {
        public string Name { get; }
        public LogSource(string name) => Name = name;
        public void Log(LogLevel level, string message)
            => Publish(new LogEvent(DateTime.Now, Name, level, message));
    }
}

public sealed class ConsoleSink : ILogSink
{
    public LogLevel Minimum { get; }
    public ConsoleSink(LogLevel min = LogLevel.Info) => Minimum = min;
    public bool Handle(in LogEvent e)
    {
        if (e.Level < Minimum) return false;
        Console.WriteLine($"[{e.Source}] {e.Timestamp:yyyy-MM-dd HH:mm:ss.fff} : {e.Message}");
        return true;
    }
}

public sealed class RollingFileSink : ILogSink, IDisposable
{
    private readonly string _path;
    private readonly long _rollBytes;
    private readonly object _ioLock = new();
    private StreamWriter _sw;
    public LogLevel Minimum { get; }

    public RollingFileSink(string path, LogLevel min = LogLevel.Debug, long rollBytes = 10_000_000)
    {
        _path = path; _rollBytes = rollBytes; Minimum = min;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _sw = new(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _sw.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] --- start ---");
    }

    public bool Handle(in LogEvent e)
    {
        if (e.Level < Minimum) return false;
        lock (_ioLock)
        {
            _sw.WriteLine($"[{e.Source}] {e.Timestamp:yyyy-MM-dd HH:mm:ss.fff} : {e.Message}");
            TryRoll();
        }
        return true;
    }

    private void TryRoll()
    {
        try
        {
            if (new FileInfo(_path).Length < _rollBytes) return;
            _sw.Dispose();
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dir = Path.GetDirectoryName(_path)!;
            var file = Path.GetFileNameWithoutExtension(_path);
            var rolled = Path.Combine(dir, $"{file}.{ts}.log");
            File.Move(_path, rolled, true);
            _sw = new(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
            _sw.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] --- rolled ---");
        } catch { }
    }

    public void Dispose() { try { _sw.Dispose(); } catch { } }
}

public sealed class FilterSink : ILogSink
{
    private readonly ILogSink _inner;
    private readonly Dictionary<string, LogLevel> _perSource = new(StringComparer.OrdinalIgnoreCase);
    public LogLevel DefaultMin { get; set; } = LogLevel.Info;
    public FilterSink(ILogSink inner) => _inner = inner;
    public void SetMin(string source, LogLevel level) => _perSource[source] = level;

    public bool Handle(in LogEvent e)
    {
        var min = _perSource.TryGetValue(e.Source, out var lvl) ? lvl : DefaultMin;
        if (e.Level < min) return false;
        return _inner.Handle(e);
    }
}
