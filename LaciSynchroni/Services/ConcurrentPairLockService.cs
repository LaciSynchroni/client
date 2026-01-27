using Dalamud.Utility;
using LaciSynchroni.Services.ServerConfiguration;
using System.Collections.Concurrent;

namespace LaciSynchroni.Services
{
    using PlayerNameHash = string;
    using ServerIndex = int;

    public class ConcurrentPairLockService(ServerConfigurationManager serverConfigurationManager)
    {
        private readonly ConcurrentDictionary<PlayerNameHash, LockData> _renderLocks = new(StringComparer.Ordinal);
        private readonly Lock _resourceLock = new();

        public int GetRenderLock(PlayerNameHash? playerNameHash, ServerIndex? serverIndex, string? characterName)
        {
            if (serverIndex is null || playerNameHash.IsNullOrWhitespace()) return -1;

            lock (_resourceLock)
            {
                var newLock = new LockData(characterName ?? "", playerNameHash, serverIndex.Value);
                // Check priority system
                var existingLock = _renderLocks.GetOrAdd(playerNameHash, newLock);
                if (existingLock.Index == newLock.Index)
                {
                    // No need to evaluate priorities -> server already has lock
                    return existingLock.Index;
                }

                var existingPriority = serverConfigurationManager.GetServerPriorityByIndex(existingLock.Index);
                var newPriority = serverConfigurationManager.GetServerPriorityByIndex(newLock.Index);
                if (newPriority > existingPriority)
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

        public record LockData(string CharName, PlayerNameHash PlayerHash, ServerIndex Index);
    }
}