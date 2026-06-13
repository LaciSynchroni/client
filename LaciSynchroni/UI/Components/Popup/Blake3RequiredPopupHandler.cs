using Dalamud.Bindings.ImGui;
using LaciSynchroni.FileCache;
using LaciSynchroni.Services.ServerConfiguration;
using LaciSynchroni.SyncConfiguration;
using System.Numerics;

namespace LaciSynchroni.UI.Components.Popup;

public class Blake3RequiredPopupHandler(
    ServerConfigurationManager serverConfigurationManager,
    UiSharedService uiSharedService,
    SyncConfigService configService,
    CacheMonitor cacheMonitor)
    : IPopupHandler
{
    public Vector2 PopupSize { get; } = new(600, 450);

    public bool ShowClose => false;

    private int _serverIndex = -1;
    private bool _autoConnect = true;


    public void DrawContent()
    {
        if (_serverIndex == -1)
        {
            return;
        }
        var serverName = serverConfigurationManager.GetServerNameByIndex(_serverIndex);

        DrawHeader();
        // Happens when you want to connect too early
        if (cacheMonitor.IsScanRunning && !configService.Current.BetaEnableBlake3)
        {
            DrawNormalScanRunning(serverName);
            return;
        }
        
        // Scan running and blake3 enabled? User clicked on connect while scan is running, show this popup with progress
        if (cacheMonitor.IsScanRunning && configService.Current.BetaEnableBlake3)
        {
            DrawHashingScanRunning(serverName);
            return;
        }
        
        DrawHashingPrompt(serverName);
    }

    public void ForIndex(int index)
    {
        _serverIndex = index;
    }

    private void DrawHeader()
    {
        using (uiSharedService.UidFont.Push())
        {
            UiSharedService.TextWrapped("BLAKE3 Support Required");
        }
    }

    private void DrawNormalScanRunning(string serverName)
    {
        UiSharedService.TextWrapped($"Startup scan is currently running. This scan has to finish before BLAKE3 support can be enabled for {serverName}");
        UiSharedService.TextWrapped($"You can monitor in this dialog - or close it and try again in a few moments.");
        uiSharedService.DrawFileScanState();
        if (ImGui.Button("Close"))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawHashingScanRunning(string serverName)
    {
        UiSharedService.TextWrapped($"Support for the BLAKE3 hashing to connect to {serverName} is being done.");
        UiSharedService.TextWrapped($"You can monitor in this dialog, in Settings -> Storage or close it and try again in a few moments.");
        uiSharedService.DrawFileScanState();
        if (ImGui.Button("Close"))
        {
            ImGui.CloseCurrentPopup();
        }
    }
    
    private void DrawHashingPrompt(string serverName)
    {
        UiSharedService.TextWrapped($"Support for the BLAKE3 hashing method is required to connect to {serverName}");
        UiSharedService.TextWrapped(
            "In order to properly support this, Laci Synchroni has to re-scan your Penumbra directory. Depending on your Penumbra directories size, this may take several minutes");
        UiSharedService.TextWrapped(
            "The process will run in the background. Once that is done, you will be able to connect.");
        UiSharedService.TextWrapped("You can check progress any time in the Settings -> Storage tab!");
        ImGui.Checkbox("Automatically connect after scanning", ref _autoConnect);
        
        if (ImGui.Button("Enable Support"))
        {
            configService.Current.BetaEnableBlake3 = true;
            configService.Save();
            if (_autoConnect)
            {   
                cacheMonitor.InvokeScan(_serverIndex);
            }
            else
            {
                cacheMonitor.InvokeScan();
            }
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            ImGui.CloseCurrentPopup();
        }
    }
}