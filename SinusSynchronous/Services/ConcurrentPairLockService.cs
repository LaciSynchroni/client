using Dalamud.Utility;
using System.Collections.Concurrent;

namespace SinusSynchronous.Services
{
    using PlayerNameHash = string;
    using ServerIndex = int;

    public class ConcurrentPairLockService
    {
        private readonly ConcurrentDictionary<PlayerNameHash, ServerIndex> _renderLocks = new();
        private readonly Lock _resourceLock = new();

        public int GetRenderLock(PlayerNameHash? playerNameHash, ServerIndex? serverIndex)
        {
            if (serverIndex is null || playerNameHash.IsNullOrWhitespace()) return -1;

            lock (_resourceLock)
            {
                bool renderLockExists = _renderLocks.TryGetValue(playerNameHash, out ServerIndex existingServerIndex);
                if (renderLockExists && existingServerIndex == serverIndex) return existingServerIndex;
                return _renderLocks.TryAdd(playerNameHash, serverIndex.Value) ? serverIndex.Value : -1;
            }
        }

        public bool ReleaseRenderLock(PlayerNameHash? playerNameHash, ServerIndex? serverIndex)
        {
            if (serverIndex is null || playerNameHash.IsNullOrWhitespace()) return false;

            lock (_resourceLock)
            {
                ServerIndex existingServerIndex = _renderLocks.GetValueOrDefault(playerNameHash, -1);
                return (serverIndex == existingServerIndex) && _renderLocks.Remove(playerNameHash, out _);
            }
        }
    }
}