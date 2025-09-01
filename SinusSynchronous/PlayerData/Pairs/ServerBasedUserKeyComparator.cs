using SinusSynchronous.API.Data;
using SinusSynchronous.API.Data.Comparer;

namespace SinusSynchronous.PlayerData.Pairs;

public class ServerBasedUserKeyComparator : IEqualityComparer<ServerBasedUserKey>
{
    private ServerBasedUserKeyComparator()
    { }

    public static ServerBasedUserKeyComparator Instance { get; } = new();

    public bool Equals(ServerBasedUserKey? x, ServerBasedUserKey? y)
    {
        if (x == null || y == null) return false;
        return x.UserData.UID.Equals(y.UserData.UID, StringComparison.Ordinal) && x.ServerIndex == y.ServerIndex;
    }

    public int GetHashCode(ServerBasedUserKey obj)
    {
        HashCode hashCode = new();
        hashCode.Add(obj.UserData.UID);
        hashCode.Add(obj.ServerIndex);
        return hashCode.ToHashCode();
    }
}