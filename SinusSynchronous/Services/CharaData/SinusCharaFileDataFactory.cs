using SinusSynchronous.API.Data;
using SinusSynchronous.FileCache;
using SinusSynchronous.Services.CharaData.Models;

namespace SinusSynchronous.Services.CharaData;

public sealed class SinusCharaFileDataFactory
{
    private readonly FileCacheManager _fileCacheManager;

    public SinusCharaFileDataFactory(FileCacheManager fileCacheManager)
    {
        _fileCacheManager = fileCacheManager;
    }

    public SinusCharaFileData Create(string description, CharacterData characterCacheDto)
    {
        return new SinusCharaFileData(_fileCacheManager, description, characterCacheDto);
    }
}