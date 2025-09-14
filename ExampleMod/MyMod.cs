// ExampleMod/MyMod.cs

using System.Reflection;
using HarmonyLib;
using WRModKit.Loader.Contracts;

namespace ExampleMod;

public sealed class ExampleModConfig
{
    public bool Enabled { get; set; } = true;
    public string CustomLoggerName { get; set; } = "ExampleMod.AI";
}

public sealed class MyMod : ModBase
{
    public static MyMod? Instance { get; private set; }
    
    private ILogSource? _aiLog;
    private IConfigSection<ExampleModConfig>? _cfg;

    public override void Load(IModContext ctx)
    {
        Instance = this;
        base.Load(ctx);
        Log.Info("Load()");
        _cfg = Config<ExampleModConfig>();
        _cfg.Changed += c => Log.Info($"Config reloaded: Enabled={c.Enabled}");
        
        _aiLog = CreateLogger(_cfg.Value.CustomLoggerName);
        _aiLog.Info("AI logger ready");
    }

    public override void Start(IModContext ctx)
    {
        Log.Info("Start()");
    }

    public override void Update(IModContext ctx, float dtSeconds)
    {
        // keep logs low-volume here
    }

    public override void Stop(IModContext ctx)
    {
        Log.Info("Stop()");
    }
}