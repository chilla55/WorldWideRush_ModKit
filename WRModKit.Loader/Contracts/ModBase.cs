// WRModKit.Loader/Contracts/ModBase.cs
namespace WRModKit.Loader.Contracts;

public abstract class ModBase : IMod
{
    protected IModContext? Context { get; private set; }
    protected ILogSource Log => Context!.Log;

    public virtual void Load(IModContext ctx)    { Context = ctx; }
    public virtual void Start(IModContext ctx)   { }
    public virtual void Update(IModContext ctx, float dtSeconds) { }
    public virtual void Stop(IModContext ctx)    { }

    protected IConfigSection<T> Config<T>(string? fileName = null) where T : class, new()
        => Context!.GetConfig<T>(fileName);

    protected ILogSource CreateLogger(string name) => Context!.CreateLogger(name);
}