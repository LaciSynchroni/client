using Dalamud.Utility;
using System.Collections.Concurrent;

namespace LaciSynchroni.Services
{
    using PlayerNameHash = string;
    using ServerIndex = int;

    public class ConcurrentPairLockService
    {
        private readonly ConcurrentDictionary<PlayerNameHash, LockData> _renderLocks = new(StringComparer.Ordinal);
        private readonly Lock _resourceLock = new();

        public int GetRenderLock(PlayerNameHash? playerNameHash, ServerIndex? serverIndex, string? characterName)
        {
            if (serverIndex is null || playerNameHash.IsNullOrWhitespace()) return -1;

            lock (_resourceLock)
            {
                var lockData = new LockData(characterName ?? "", playerNameHash, serverIndex.Value);
                return _renderLocks.GetOrAdd(playerNameHash, lockData).Index;
            }
        }

        public bool ReleaseRenderLock(PlayerNameHash? playerNameHash, ServerIndex? serverIndex)
        {
            if (serverIndex is null || playerNameHash.IsNullOrWhitespace()) return false;

            lock (_resourceLock)
            {
                ServerIndex existingServerIndex = _renderLocks.GetValueOrDefault(playerNameHash)?.Index ?? -1;
                return (serverIndex == existingServerIndex) && _renderLocks.Remove(playerNameHash, out _);
            }
        }

        public ICollection<LockData> GetCurrentRenderLocks()
        {
            return _renderLocks.Values;
        }

        public record LockData(string CharName, PlayerNameHash PlayerHash, ServerIndex Index);
    }
}