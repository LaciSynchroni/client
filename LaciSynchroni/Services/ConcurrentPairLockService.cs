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

        public int GetRenderLock(PlayerNameHash? playerNameHash, ServerIndex? serverIndex, string? characterName, int serverPriority)
        {
            if (serverIndex is null || playerNameHash.IsNullOrWhitespace()) return -1;

            lock (_resourceLock)
            {
                var newLock = new LockData(characterName ?? "", playerNameHash, serverIndex.Value, serverPriority);
                // Check priority system
                var existingLock = _renderLocks.GetOrAdd(playerNameHash, newLock);
                if (newLock.HasPriorityOver(existingLock))
                {
                    // The other server has priority. We can't really prevent the ongoing stuff, but we can attempt further attempts to write data
                    _renderLocks[playerNameHash] = newLock;
                    return newLock.Index;
                }

                return existingLock.Index;
            }
        }

        public bool HasRenderLock(PlayerNameHash? playerNameHash, ServerIndex serverIndex)
        {
            if (playerNameHash.IsNullOrWhitespace())
            {
                return true;
            }
            var existingLock = _renderLocks.GetValueOrDefault(playerNameHash, null);
            if (existingLock == null)
            {
                return true;
            }
            return existingLock.Index == serverIndex;
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

        public record LockData(string CharName, PlayerNameHash PlayerHash, ServerIndex Index, int ServerPriority)
        {
            public bool HasPriorityOver(LockData other)
            {
                // No need to check for same server, since the same server will have the same priority
                // Note: If priorities were changed without a clean restart, it might be different.
                return ServerPriority > other.ServerPriority;
            }
        }
    }
}