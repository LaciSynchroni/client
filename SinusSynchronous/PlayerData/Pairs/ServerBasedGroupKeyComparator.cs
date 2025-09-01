using SinusSynchronous.API.Data;
using SinusSynchronous.API.Data.Comparer;

namespace SinusSynchronous.PlayerData.Pairs;

public class ServerBasedGroupKeyComparator : IEqualityComparer<ServerBasedGroupKey>
{
    private ServerBasedGroupKeyComparator()
    { }

    public static ServerBasedGroupKeyComparator Instance { get; } = new();

    public bool Equals(ServerBasedGroupKey? x, ServerBasedGroupKey? y)
    {
        if (x == null || y == null) return false;
        return x.GroupData.GID.Equals(y.GroupData.GID, StringComparison.Ordinal) && x.ServerIndex == y.ServerIndex;
    }

    public int GetHashCode(ServerBasedGroupKey obj)
    {
        HashCode hashCode = new();
        hashCode.Add(obj.GroupData.GID);
        hashCode.Add(obj.ServerIndex);
        return hashCode.ToHashCode();
    }
}