using Dalamud.Bindings.ImGui;
using LaciSynchroni.Services.ServerConfiguration;

namespace LaciSynchroni.UI.Components
{
    /// <summary>
    /// Quick and dirty server selector that can be used in all places where we need a server selector now, to be replaced with a better
    /// concept down the line
    /// </summary>
    /// <param name="onServerIndexChange">Event called when the server index selector in this selector has changed</param>
    public class ServerSelectorSmall(Action<int> onServerIndexChange, int currentServerIndex = 0)
    {
        private int _currentServerIndex = currentServerIndex;

        public void Draw(List<ServerInfoDto> availableServers, int[] connectedServers, float width)
        {
            if (connectedServers.Length <= 0)
            {
                // This component should not be rendered without any connected servers! Doesn't make much sense!
                return;
            }
            // Check if the current server is actually selectable, if not, swap to the first connected we find
            if (!connectedServers.Contains(_currentServerIndex))
            {
                ChangeSelectedIndex(connectedServers[0]);
            }
            
            var selectedServer = availableServers.First(dto => dto.Id == _currentServerIndex);
            ImGui.SetNextItemWidth(width);
            if (ImGui.BeginCombo("", selectedServer.Name))
            {
                foreach (var server in availableServers)
                {
                    var isSelected = _currentServerIndex == server.Id;
                    var isConnected = connectedServers.Contains(server.Id);
                    if (ImGui.Selectable(server.Name, isSelected, isConnected ? ImGuiSelectableFlags.None : ImGuiSelectableFlags.Disabled))
                    {
                        ChangeSelectedIndex(server.Id);
                    }
                    if (!isConnected)
                    {
                        UiSharedService.AttachToolTip($"You are currently not connected to {server.Name} service.");
                    }
                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }
                ImGui.EndCombo();
            }
        }

        private void ChangeSelectedIndex(int index)
        {
            if (_currentServerIndex != index)
            {
                _currentServerIndex = index;
                onServerIndexChange.Invoke(index);
            }
        }
    }
}