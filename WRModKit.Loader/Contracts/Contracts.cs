// WRModKit.Loader/Contracts/Contracts.cs
namespace WRModKit.Loader.Contracts;

public enum LogLevel { Trace, Debug, Info, Warning, Error, Fatal }

public interface ILogSource
{
    string Name { get; }
    void Log(LogLevel level, string message);
    void Trace(string m)  => Log(LogLevel.Trace, m);
    void Debug(string m)  => Log(LogLevel.Debug, m);
    void Info(string m)   => Log(LogLevel.Info,  m);
    void Warn(string m)   => Log(LogLevel.Warning, m);
    void Error(string m)  => Log(LogLevel.Error, m);
    void Fatal(string m)  => Log(LogLevel.Fatal, m);
}

public interface IConfigSection<T> where T : class, new()
{
    T Value { get; }
    void Set(T newValue, bool save = true);
    void Update(System.Action<T> mutator, bool save = true);
    void Save();
    event System.Action<T> Changed;
}

public interface IMod
{
    void Load(IModContext ctx);
    void Start(IModContext ctx);
    void Update(IModContext ctx, float dtSeconds);
    void Stop(IModContext ctx);
}

public interface IModContext
{
    string Id { get; }
    string ModDirectory { get; }
    ILogSource Log { get; }
    ILogSource CreateLogger(string customName);
    IConfigSection<T> GetConfig<T>(string? fileName = null) where T : class, new();
}