// WRModKit.Loader/Program.cs
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using HarmonyLib;
using WRModKit.Loader.Contracts;
using WRModKit.Loader.Runtime;

namespace WRModKit.Loader;

public sealed class LoaderConfig
{
    public string GameAssembly { get; set; } = "Worldwide Rush.dll";
    public string ModsDirectory { get; set; } = "Mods";
    public bool InstallBridge { get; set; } = true;
    public bool RedirectStateLog { get; set; } = true;
    public bool EnableConsole { get; set; } = true;
    public bool EnableFileLog { get; set; } = true;
    public string FileLogPath { get; set; } = "Logs/modloader.log";
    public string[]? EnabledMods { get; set; } = null;
    public Dictionary<string,string>? LogLevels { get; set; } = null;
}

internal static class Program
{
    [DllImport("kernel32.dll")] static extern bool AllocConsole();
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            Directory.SetCurrentDirectory(baseDir);

            var cfgPath = Path.Combine(baseDir, "ModLoader.json");
            var cfg = File.Exists(cfgPath)
                ? JsonSerializer.Deserialize<LoaderConfig>(File.ReadAllText(cfgPath)) ?? new LoaderConfig()
                : new LoaderConfig();

            if (cfg.EnableConsole && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                if (GetConsoleWindow() == IntPtr.Zero) AllocConsole();

            ILogSink consoleSink = new ConsoleSink(LogLevel.Debug);
            if (cfg.EnableFileLog)
            {
                var path = Path.Combine(baseDir, cfg.FileLogPath);
                var fileSink = new RollingFileSink(path, LogLevel.Debug, 10_000_000);
                var filter = new FilterSink(consoleSink);
                if (cfg.LogLevels is not null)
                    foreach (var kv in cfg.LogLevels)
                        if (Enum.TryParse<LogLevel>(kv.Value, true, out var lvl)) filter.SetMin(kv.Key, lvl);
                LoggerManager.AddSink(filter);
                LoggerManager.AddSink(fileSink);
            }
            else
            {
                LoggerManager.AddSink(consoleSink);
            }

            var loaderLog = LoggerManager.CreateSource("Loader");
            loaderLog.Info("=== WRModKit.Loader start ===");
            loaderLog.Info("Target game: Worldwide Rush");

            var cfgRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                       "Worldwide Rush", "ModLoader");
            var cfgService = new ConfigService(cfgRoot);

            var gamePath = Path.GetFullPath(Path.Combine(baseDir, cfg.GameAssembly));
            if (!File.Exists(gamePath)) { loaderLog.Error($"Game assembly not found: {gamePath}"); return -3; }
            var gameDir = baseDir;
            string Game(string name) => Path.Combine(gameDir, name);

            var depsAnchors = new[]
            {
                "Worldwide Rush.dll", // has Worldwide Rush.deps.json next to it
                "STMG.dll",           // has STMG.deps.json
                "STVisual.dll"        // has STVisual.deps.json
            };

            // Build resolvers for every anchor that exists
            var resolvers = depsAnchors
                .Select(n => Game(n))
                .Where(File.Exists)
                .Select(p => new AssemblyDependencyResolver(p))
                .ToArray();

            // Keep your mods/<id>/libs fallback
            var modsDir = Path.Combine(baseDir, cfg.ModsDirectory);
            Directory.CreateDirectory(modsDir);
            var modLibDirs = Directory.GetDirectories(modsDir)
                                      .Select(d => Path.Combine(d, "libs"))
                                      .Where(Directory.Exists)
                                      .ToArray();

            Assembly? ResolveManaged(AssemblyLoadContext ctx, AssemblyName name)
            {
                // Try each deps.json map in order
                foreach (var r in resolvers)
                {
                    var path = r.ResolveAssemblyToPath(name);
                    if (path != null) return ctx.LoadFromAssemblyPath(path);
                }

                // Fallback: Mods/<id>/libs/Name.dll
                var dll = name.Name + ".dll";
                foreach (var dir in modLibDirs)
                {
                    var cand = Path.Combine(dir, dll);
                    if (File.Exists(cand)) return ctx.LoadFromAssemblyPath(cand);
                }
                return null;
            }

            nint ResolveNative(Assembly _, string libName)
            {
                foreach (var r in resolvers)
                {
                    var p = r.ResolveUnmanagedDllToPath(libName);
                    if (p != null && File.Exists(p)) return NativeLibrary.Load(p);
                }

                // Common native probe locations inside the game folder
                var file = libName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? libName : libName + ".dll";
                string[] probes =
                {
                    Path.Combine(gameDir, file),
                    Path.Combine(gameDir, "runtimes", "win-x64", "native", file),
                    Path.Combine(gameDir, "runtimes", "win", "native", file)
                };
                foreach (var p in probes)
                    if (File.Exists(p)) return NativeLibrary.Load(p);

                return 0;
            }

            AssemblyLoadContext.Default.Resolving += ResolveManaged;
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNative;

            // Load game FIRST
            var gameAsm = Assembly.LoadFrom(gamePath);

            var hBridge = new Harmony("com.wrmodkit.bridge");
            PatchInstaller.InstallBridgeAndStateLog(hBridge, cfg.InstallBridge, cfg.RedirectStateLog, loaderLog);

            // Discover mods
            var modFolders = Directory.GetDirectories(modsDir);
            var enabledSet = (cfg.EnabledMods is null || cfg.EnabledMods.Length == 0)
                                ? null
                                : new HashSet<string>(cfg.EnabledMods, StringComparer.OrdinalIgnoreCase);

            int loaded = 0;
            foreach (var folder in modFolders)
            {
                var id = Path.GetFileName(folder);
                if (enabledSet != null && !enabledSet.Contains(id)) continue;

                var dll = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (dll is null) { loaderLog.Warn($"Skipping {id} (no dll)."); continue; }

                try
                {
                    var asm = Assembly.LoadFrom(dll);

                    // Optional convention: HarmonyBootstrap
                    var hb = asm.GetTypes()
                                .Select(t => t.GetMethod("HarmonyBootstrap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                                .FirstOrDefault(m => m != null);
                    hb?.Invoke(null, null);

                    var tMod = asm.GetTypes().FirstOrDefault(t => typeof(IMod).IsAssignableFrom(t) && !t.IsAbstract);
                    if (tMod == null) { loaderLog.Warn($"No IMod in {id}."); continue; }

                    var instance = (IMod)Activator.CreateInstance(tMod)!;
                    var ctx = new ModContext
                    {
                        Id = id,
                        ModDirectory = folder,
                        Log = LoggerManager.CreateSource($"Mod.{id}")
                    };
                    ctx.Attach(cfgService);

                    instance.Load(ctx);
                    ModHost.Register(instance, ctx);
                    ctx.Log.Info("Load() OK");
                    loaded++;
                }
                catch (Exception ex)
                {
                    loaderLog.Error($"ERROR loading mod '{id}': {ex}");
                }
            }
            loaderLog.Info($"Loaded {loaded} mods.");

            // Invoke STM.Program.Main in Worldwide Rush.dll
            var progType = gameAsm.GetType("STM.Program", throwOnError: false);
            var main = progType?.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
            if (main == null) { loaderLog.Error("STM.Program.Main not found; aborting."); return -4; }

            loaderLog.Info("Invoking STM.Program.Main()");
            main.Invoke(null, null);

            loaderLog.Info("Game exited; stopping mods.");
            ModHost.StopAll();
            loaderLog.Info("=== WRModKit.Loader end ===");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return -1;
        }
    }
}
