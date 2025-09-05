using Dalamud.Utility;
using System.Collections.Concurrent;

namespace SinusSynchronous.Services
{
    using PlayerName = string;
    
    public class ConcurrentPairLockService
    {
        private readonly ConcurrentDictionary<PlayerName, bool> _renderLocks = new();


        public bool TryAcquireLock(PlayerName? playerName)
        {
            if (playerName.IsNullOrWhitespace())
            {
                return false;
            }
            return _renderLocks.GetOrAdd(playerName, true);
        }

        public void ReleaseLock(PlayerName? playerName)
        {
            if (playerName != null)
            {
                _renderLocks.Remove(playerName, out _);
            }
        }
    }
}