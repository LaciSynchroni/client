namespace LaciSynchroni.UI.Components;

/// <summary>
/// A sync service's incoming requests (for example pair requests or syncshell invites), shown in the
/// "Pending Requests" folder. Register implementations in DI; the folder draws every registered one.
/// </summary>
public interface IPendingRequestsProvider
{
    /// <summary>Number of pending entries. The folder is shown, with a count badge, while any provider is above zero.</summary>
    int PendingCount { get; }

    /// <summary>Draws the entries inside the opened folder. Called every frame while the folder is open.</summary>
    void Draw();
}
