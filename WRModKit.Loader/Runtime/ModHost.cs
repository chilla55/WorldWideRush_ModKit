// WRModKit.Loader/Runtime/ModHost.cs
using System.Diagnostics;
using WRModKit.Loader.Contracts;

namespace WRModKit.Loader.Runtime;

public static class ModHost
{
    private static readonly List<(IMod mod, IModContext ctx)> Mods = new();
    private static bool _started;
    private static Stopwatch _timer = new();

    public static void Register(IMod mod, IModContext ctx) => Mods.Add((mod, ctx));

    public static void EnsureStartedOnce()
    {
        if (_started) return;
        _started = true;
        _timer.Restart();
        foreach (var (m, c) in Mods)
        {
            try { m.Start(c); c.Log.Info("Start OK"); } catch (Exception e) { c.Log.Error($"Start ERROR: {e}"); }
        }
    }

    public static void Tick()
    {
        if (!_started) return;
        var dt = (float)_timer.Elapsed.TotalSeconds;
        _timer.Restart();
        foreach (var (m, c) in Mods)
        {
            try { m.Update(c, dt); } catch (Exception e) { c.Log.Error($"Update ERROR: {e}"); }
        }
    }

    public static void StopAll()
    {
        foreach (var (m, c) in Mods)
        {
            try { m.Stop(c); c.Log.Info("Stopped."); } catch (Exception e) { c.Log.Error($"Stop ERROR: {e}"); }
        }
        Mods.Clear();
    }
}
