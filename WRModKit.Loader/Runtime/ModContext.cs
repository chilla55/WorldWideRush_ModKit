// WRModKit.Loader/Runtime/ModContext.cs
using WRModKit.Loader.Contracts;

namespace WRModKit.Loader.Runtime;

public sealed class ModContext : IModContext
{
    public string Id { get; init; } = "";
    public string ModDirectory { get; init; } = "";
    public ILogSource Log { get; init; } = default!;

    private ConfigService _cfg = default!;
    internal void Attach(ConfigService svc) => _cfg = svc;

    public ILogSource CreateLogger(string customName) => LoggerManager.CreateSource(customName);

    public IConfigSection<T> GetConfig<T>(string? fileName = null) where T : class, new()
        => _cfg.GetSection<T>(Id, fileName);
}
