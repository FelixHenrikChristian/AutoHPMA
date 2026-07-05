using System.Text.Json;
using AutoHPMA.Core.Models;

namespace AutoHPMA.Core.Services;

public sealed class TaskCoordinateConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    private readonly object _gate = new();
    private IReadOnlyDictionary<string, TaskCoordinatePoint> _points;
    private IReadOnlyDictionary<string, TaskCoordinateRegion> _regions;
    private int _canonicalWidth;
    private int _canonicalHeight;
    private string _sourcePath;

    private TaskCoordinateConfigStore(
        int canonicalWidth,
        int canonicalHeight,
        IReadOnlyDictionary<string, TaskCoordinatePoint> points,
        IReadOnlyDictionary<string, TaskCoordinateRegion> regions,
        string sourcePath)
    {
        _canonicalWidth = canonicalWidth;
        _canonicalHeight = canonicalHeight;
        _points = points;
        _regions = regions;
        _sourcePath = sourcePath;
    }

    public int CanonicalWidth
    {
        get
        {
            lock (_gate)
            {
                return _canonicalWidth;
            }
        }
    }

    public int CanonicalHeight
    {
        get
        {
            lock (_gate)
            {
                return _canonicalHeight;
            }
        }
    }

    public string SourcePath
    {
        get
        {
            lock (_gate)
            {
                return _sourcePath;
            }
        }
    }

    public static TaskCoordinateConfigStore Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"任务坐标配置文件不存在：{path}", path);
        }

        var sourcePath = Path.GetFullPath(path);
        var config = ReadConfig(sourcePath);
        var (points, regions) = ValidateAndBuildIndexes(config, sourcePath);
        return new TaskCoordinateConfigStore(
            config.CanonicalWidth,
            config.CanonicalHeight,
            points,
            regions,
            sourcePath);
    }

    public TaskCoordinatePoint GetRequiredPoint(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
        {
            return _points.TryGetValue(id, out var point)
                ? point
                : throw new KeyNotFoundException($"任务坐标配置缺少固定点：{id}。配置文件：{SourcePath}");
        }
    }

    public TaskCoordinateRegion GetRequiredRegion(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_gate)
        {
            return _regions.TryGetValue(id, out var region)
                ? region
                : throw new KeyNotFoundException($"任务坐标配置缺少固定区域：{id}。配置文件：{SourcePath}");
        }
    }

    public TaskCoordinateConfig CreateSnapshot()
    {
        lock (_gate)
        {
            return new TaskCoordinateConfig
            {
                CanonicalWidth = _canonicalWidth,
                CanonicalHeight = _canonicalHeight,
                Points = _points
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => new TaskCoordinatePointDefinition
                    {
                        Id = pair.Key,
                        X = pair.Value.X,
                        Y = pair.Value.Y,
                    })
                    .ToList(),
                Regions = _regions
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => new TaskCoordinateRegionDefinition
                    {
                        Id = pair.Key,
                        X = pair.Value.X,
                        Y = pair.Value.Y,
                        Width = pair.Value.Width,
                        Height = pair.Value.Height,
                    })
                    .ToList(),
            };
        }
    }

    public void Reload()
    {
        var sourcePath = SourcePath;
        var config = ReadConfig(sourcePath);
        Apply(config, sourcePath);
    }

    public void LoadFrom(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"任务坐标配置文件不存在：{path}", path);
        }

        var sourcePath = Path.GetFullPath(path);
        var config = ReadConfig(sourcePath);
        Apply(config, sourcePath);
    }

    public void Save(TaskCoordinateConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var sourcePath = SourcePath;
        var (points, regions) = ValidateAndBuildIndexes(config, sourcePath);

        var directory = Path.GetDirectoryName(sourcePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = sourcePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(config, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, sourcePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        Apply(config, points, regions, sourcePath);
    }

    private void Apply(TaskCoordinateConfig config, string path)
    {
        var (points, regions) = ValidateAndBuildIndexes(config, path);
        Apply(config, points, regions, path);
    }

    private void Apply(
        TaskCoordinateConfig config,
        IReadOnlyDictionary<string, TaskCoordinatePoint> points,
        IReadOnlyDictionary<string, TaskCoordinateRegion> regions,
        string sourcePath)
    {
        lock (_gate)
        {
            _canonicalWidth = config.CanonicalWidth;
            _canonicalHeight = config.CanonicalHeight;
            _points = points;
            _regions = regions;
            _sourcePath = sourcePath;
        }
    }

    private static TaskCoordinateConfig ReadConfig(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<TaskCoordinateConfig>(stream, SerializerOptions)
            ?? throw new InvalidDataException($"任务坐标配置为空：{path}");
    }

    private static (
        IReadOnlyDictionary<string, TaskCoordinatePoint> Points,
        IReadOnlyDictionary<string, TaskCoordinateRegion> Regions)
        ValidateAndBuildIndexes(TaskCoordinateConfig config, string path)
    {
        if (config.CanonicalWidth <= 0 || config.CanonicalHeight <= 0)
        {
            throw new InvalidDataException($"任务坐标配置的基准分辨率无效：{path}");
        }

        var points = BuildPointIndex(config, path);
        var regions = BuildRegionIndex(config, path);
        var duplicateId = points.Keys.FirstOrDefault(regions.ContainsKey);
        if (duplicateId is not null)
        {
            throw new InvalidDataException($"任务固定点与固定区域 ID 重复：{duplicateId}。配置文件：{path}");
        }

        return (points, regions);
    }

    private static IReadOnlyDictionary<string, TaskCoordinatePoint> BuildPointIndex(
        TaskCoordinateConfig config,
        string path)
    {
        var points = new Dictionary<string, TaskCoordinatePoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in config.Points ?? [])
        {
            var id = ValidateId(definition.Id, "固定点", path);
            if (definition.X < 0 ||
                definition.Y < 0 ||
                definition.X >= config.CanonicalWidth ||
                definition.Y >= config.CanonicalHeight)
            {
                throw new InvalidDataException($"任务固定点超出基准画面：{id}。配置文件：{path}");
            }

            if (!points.TryAdd(id, new TaskCoordinatePoint(definition.X, definition.Y)))
            {
                throw new InvalidDataException($"任务固定点 ID 重复：{id}。配置文件：{path}");
            }
        }

        return points;
    }

    private static IReadOnlyDictionary<string, TaskCoordinateRegion> BuildRegionIndex(
        TaskCoordinateConfig config,
        string path)
    {
        var regions = new Dictionary<string, TaskCoordinateRegion>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in config.Regions ?? [])
        {
            var id = ValidateId(definition.Id, "固定区域", path);
            if (definition.X < 0 ||
                definition.Y < 0 ||
                definition.Width <= 0 ||
                definition.Height <= 0 ||
                definition.X + definition.Width > config.CanonicalWidth ||
                definition.Y + definition.Height > config.CanonicalHeight)
            {
                throw new InvalidDataException($"任务固定区域超出基准画面：{id}。配置文件：{path}");
            }

            var region = new TaskCoordinateRegion(
                definition.X,
                definition.Y,
                definition.Width,
                definition.Height);
            if (!regions.TryAdd(id, region))
            {
                throw new InvalidDataException($"任务固定区域 ID 重复：{id}。配置文件：{path}");
            }
        }

        return regions;
    }

    private static string ValidateId(string id, string coordinateType, string path)
    {
        var normalized = id.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidDataException($"任务{coordinateType}存在空 ID。配置文件：{path}");
        }

        return normalized;
    }
}
