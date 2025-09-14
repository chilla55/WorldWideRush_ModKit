// WRModKit.Loader/Config/ConfigService.cs

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using WRModKit.Loader.Contracts;

namespace WRModKit.Loader;

public sealed class ConfigService
{
    private readonly string _root;
    private readonly JsonSerializerOptions _json;
    private readonly ConcurrentDictionary<string, object> _cache = new();

    public ConfigService(string root)
    {
        _root = root;
        Directory.CreateDirectory(root);
        _json = new JsonSerializerOptions
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public IConfigSection<T> GetSection<T>(string modId, string? fileName = null) where T : class, new()
    {
        var key = $"{modId}:{fileName ?? "config.json"}";
        return (IConfigSection<T>)_cache.GetOrAdd(key, _ =>
        {
            var dir = Path.Combine(_root, "Mods", modId);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName ?? "config.json");
            return new ConfigSection<T>(path, _json);
        });
    }

    private sealed class ConfigSection<T> : IConfigSection<T> where T : class, new()
    {
        private readonly string _path;
        private readonly JsonSerializerOptions _json;
        private readonly FileSystemWatcher _fsw;
        private readonly object _ioLock = new();

        public event Action<T>? Changed;
        public T Value { get; private set; }

        public ConfigSection(string path, JsonSerializerOptions json)
        {
            _path = path; _json = json;
            Value = LoadOrCreate(path, json);
            var dir = Path.GetDirectoryName(path)!;
            _fsw = new FileSystemWatcher(dir, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _fsw.Changed += (_, __) => ReloadIfChanged();
            _fsw.Created += (_, __) => ReloadIfChanged();
            _fsw.Renamed += (_, __) => ReloadIfChanged();
        }

        public void Set(T newValue, bool save = true)
        {
            Value = newValue ?? new T();
            if (save) Save();
        }

        public void Update(Action<T> mutator, bool save = true)
        {
            mutator(Value);
            if (save) Save();
        }

        public void Save()
        {
            lock (_ioLock)
            {
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Value, _json));
                File.Copy(tmp, _path, true);
                File.Delete(tmp);
            }
        }

        private static T LoadOrCreate(string path, JsonSerializerOptions json)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<T>(File.ReadAllText(path), json) ?? new T();
            }
            catch { }
            var v = new T();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(v, json));
            return v;
        }

        private void ReloadIfChanged()
        {
            try
            {
                Thread.Sleep(25);
                T? fresh;
                lock (_ioLock)
                {
                    if (!File.Exists(_path)) return;
                    fresh = JsonSerializer.Deserialize<T>(File.ReadAllText(_path), _json);
                    if (fresh == null) return;
                    Value = fresh;
                }
                Changed?.Invoke(Value);
            }
            catch { }
        }
    }
}
