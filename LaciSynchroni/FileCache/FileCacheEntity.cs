#nullable disable

namespace LaciSynchroni.FileCache;

public class FileCacheEntity
{
    public FileCacheEntity(string sha1hash, string path, string lastModifiedDateTicks, long? size = null, long? compressedSize = null)
    {
        Size = size;
        CompressedSize = compressedSize;
        Sha1Hash = sha1hash;
        PrefixedFilePath = path;
        LastModifiedDateTicks = lastModifiedDateTicks;
    }

    public long? CompressedSize { get; set; }
    public string CsvEntry => $"{Sha1Hash}{FileCacheManager.CsvSplit}{Blake3Hash}{FileCacheManager.CsvSplit}{PrefixedFilePath}{FileCacheManager.CsvSplit}{LastModifiedDateTicks}{FileCacheManager.CsvSplit}{Size ?? -1}{FileCacheManager.CsvSplit}{CompressedSize ?? -1}";
    public string Sha1Hash { get; set; }
    public string Blake3Hash { get; set; } 
    public bool IsCacheEntry => PrefixedFilePath.StartsWith(FileCacheManager.CachePrefix, StringComparison.OrdinalIgnoreCase);
    public string LastModifiedDateTicks { get; set; }
    public string PrefixedFilePath { get; init; }
    public string ResolvedFilepath { get; private set; } = string.Empty;
    public long? Size { get; set; }

    public void SetResolvedFilePath(string filePath)
    {
        ResolvedFilepath = filePath.ToLowerInvariant().Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}