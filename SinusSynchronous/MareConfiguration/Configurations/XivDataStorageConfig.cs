using System.Collections.Concurrent;

namespace SinusSynchronous.MareConfiguration.Configurations;

public class XivDataStorageConfig : IMareConfiguration
{
    public ConcurrentDictionary<string, long> TriangleDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, Dictionary<string, List<ushort>>> BonesDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}