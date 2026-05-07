using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using LaciSynchroni.Services;
using LaciSynchroni.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace LaciSynchroni.Interop.Ipc;

public sealed class IpcCallerMoodles : IIpcCaller
{
    private readonly IDalamudPluginInterface _pi;
    private readonly ICallGateSubscriber<int> _moodlesApiVersion;
    private readonly ICallGateSubscriber<nint, object> _moodlesOnChange;
    private readonly ICallGateSubscriber<nint, string> _moodlesGetStatus;
    private readonly ICallGateSubscriber<nint, string, object> _moodlesSetStatus;
    private readonly ICallGateSubscriber<nint, object> _moodlesRevertStatus;
    private readonly ILogger<IpcCallerMoodles> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly SyncMediator _syncMediator;

    public IpcCallerMoodles(ILogger<IpcCallerMoodles> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil,
        SyncMediator syncMediator)
    {
        _pi = pi;
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _syncMediator = syncMediator;

        _moodlesApiVersion = pi.GetIpcSubscriber<int>("Moodles.Version");
        _moodlesOnChange = pi.GetIpcSubscriber<nint, object>("Moodles.StatusManagerModified");
        _moodlesGetStatus = pi.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtrV2");
        _moodlesSetStatus = pi.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtrV2");
        _moodlesRevertStatus = pi.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtrV2");

        _moodlesOnChange.Subscribe(OnMoodlesChange);

        CheckAPI();
    }

    private void OnMoodlesChange(nint characterAddress)
    {
        _syncMediator.Publish(new MoodlesMessage(characterAddress));
    }

    public bool APIAvailable { get; private set; } = false;

    public void CheckAPI()
    {
        bool apiAvailable = false;
        try
        {

            bool pluginAvailable =
                (_pi.InstalledPlugins
                    .FirstOrDefault(p => string.Equals(p.InternalName, "Moodles", StringComparison.OrdinalIgnoreCase))
                    ?.Version ?? new Version(0, 0, 0, 0)) >= new Version(1, 1, 3, 1);

            apiAvailable = pluginAvailable &&
                (_moodlesApiVersion.InvokeFunc() is 4);
        }
        catch
        {
            apiAvailable = false;
        }
        finally
        {
            APIAvailable = apiAvailable;
        }
    }

    public void Dispose()
    {
        _moodlesOnChange.Unsubscribe(OnMoodlesChange);
    }

    public async Task<string?> GetStatusAsync(nint address)
    {
        if (!APIAvailable) return null;

        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(() => _moodlesGetStatus.InvokeFunc(address)).ConfigureAwait(false);

        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Get Moodles Status");
            return null;
        }
    }

    public async Task SetStatusAsync(nint pointer, string status)
    {
        if (!APIAvailable) return;
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _moodlesSetStatus.InvokeAction(pointer, status)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }

    public async Task RevertStatusAsync(nint pointer)
    {
        if (!APIAvailable) return;
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _moodlesRevertStatus.InvokeAction(pointer)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }
}
