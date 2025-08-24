using SinusSynchronous.API.Data;
using SinusSynchronous.FileCache;
using SinusSynchronous.Services.CharaData.Models;

namespace SinusSynchronous.Services.CharaData;

public sealed class MareCharaFileDataFactory
{
    private readonly FileCacheManager _fileCacheManager;

    public MareCharaFileDataFactory(FileCacheManager fileCacheManager)
    {
        _fileCacheManager = fileCacheManager;
    }

    public MareCharaFileData Create(string description, CharacterData characterCacheDto)
    {
        return new MareCharaFileData(_fileCacheManager, description, characterCacheDto);
    }
}