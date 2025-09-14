// WRModKit.Loader/Patching/PatchInstaller.cs

using HarmonyLib;
using WRModKit.Loader.Contracts;
using WRModKit.Loader.Runtime;

namespace WRModKit.Loader;

public static class PatchInstaller
{
    public static void InstallBridgeAndStateLog(Harmony h, bool bridgeUpdate, bool redirectStateLog, ILogSource log)
    {
        if (bridgeUpdate)
        {
            var mainType = AccessTools.TypeByName("STM.Main");
            var gameTime = AccessTools.TypeByName("Microsoft.Xna.Framework.GameTime");
            var update = AccessTools.Method(mainType, "Update", new[] { gameTime });
            if (update != null)
            {
                var prefix = SymbolExtensions.GetMethodInfo(() => BridgePrefix());
                h.Patch(update, prefix: new HarmonyMethod(prefix));
                log.Info("Bridge Update patch installed.");
            }
            else log.Warn("Could not locate STM.Main.Update(GameTime) for bridge patch.");
        }

        if (redirectStateLog)
        {
            var stateType = AccessTools.TypeByName("STMG.Utility.StateLog");
            var setState = AccessTools.Method(stateType, "SetState", new[] { typeof(string), typeof(bool) });
            if (setState != null)
            {
                var postfix = SymbolExtensions.GetMethodInfo(() => StateLogPostfix(default!));
                h.Patch(setState, postfix: new HarmonyMethod(postfix));
                log.Info("StateLog redirection patch installed.");
            }
            else log.Warn("Could not locate STMG.Utility.StateLog.SetState(string,bool).");
        }
    }

    public static void BridgePrefix()
    {
        ModHost.EnsureStartedOnce();
        ModHost.Tick();
    }

    public static void StateLogPostfix(string state)
    {
        LoggerManager.Publish(new LogEvent(DateTime.Now, "StateLog", LogLevel.Info, state));
    }
}