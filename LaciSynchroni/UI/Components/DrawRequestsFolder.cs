using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using LaciSynchroni.PlayerData.Pairs;
using LaciSynchroni.UI.Handlers;
using System.Collections.Immutable;
using System.Globalization;

namespace LaciSynchroni.UI.Components;

/// <summary>
/// The "Pending Requests" folder: draws every registered <see cref="IPendingRequestsProvider"/>.
/// Only shown while something is pending, and open unless the user collapsed it.
/// </summary>
public class DrawRequestsFolder(
    IReadOnlyList<IPendingRequestsProvider> providers,
    TagHandler tagHandler,
    UiSharedService uiSharedService)
    : DrawFolderBase(ImmutableList<DrawUserPair>.Empty, ImmutableList<Pair>.Empty, uiSharedService)
{
    // Stored like the other folders' open state, but marks the folder as collapsed, so it starts open
    private const string CollapsedTag = "Laci_RequestsCollapsed";

    private int PendingCount => providers.Sum(p => p.PendingCount);

    protected override bool RenderIfEmpty => PendingCount > 0;
    protected override bool RenderMenu => false;
    protected override string ComponentId => CollapsedTag;
    protected override bool IsOpen => !tagHandler.IsGlobalTagOpen(CollapsedTag);

    protected override void ToggleOpen() => tagHandler.ToggleGlobalTagOpen(CollapsedTag);

    protected override float DrawIcon()
    {
        ImGui.AlignTextToFramePadding();
        _uiSharedService.IconText(FontAwesomeIcon.Envelope);
        ImGui.SameLine();
        return ImGui.GetCursorPosX();
    }

    protected override void DrawMenu(float menuWidth)
    {
        // No menu
    }

    protected override void DrawName(float width)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Pending Requests");
    }

    protected override float DrawRightSide(float currentRightSideX)
    {
        var pending = PendingCount;
        var text = pending.ToString(CultureInfo.InvariantCulture);
        var start = currentRightSideX - ImGui.CalcTextSize(text).X;
        ImGui.SameLine(start);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(ImGuiColors.DalamudYellow, text);
        UiSharedService.AttachToolTip($"{pending} pending request(s)");
        return start;
    }

    protected override void DrawOpenedContent()
    {
        foreach (var provider in providers)
        {
            provider.Draw();
        }

        ImGui.Separator();
    }
}
