using LaciSynchroni.Services.Mediator;
using LaciSynchroni.SyncConfiguration.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace LaciSynchroni.WebAPI.SignalR.Utils;

public class ForeverRetryPolicy : IRetryPolicy
{
    private readonly SyncMediator _mediator;
    private readonly int _serverIndex;
    private readonly string _serverName;
    private bool _sentDisconnected = false;

    public ForeverRetryPolicy(SyncMediator mediator, int serverIndex, string serverName)
    {
        _mediator = mediator;
        _serverIndex = serverIndex;
        _serverName = serverName;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        TimeSpan timeToWait = TimeSpan.FromSeconds(new Random().Next(10, 20));
        if (retryContext.PreviousRetryCount == 0)
        {
            _sentDisconnected = false;
            timeToWait = TimeSpan.FromSeconds(3);
        }
        else if (retryContext.PreviousRetryCount == 1) timeToWait = TimeSpan.FromSeconds(5);
        else if (retryContext.PreviousRetryCount == 2) timeToWait = TimeSpan.FromSeconds(10);
        else
        {
            if (!_sentDisconnected)
            {
                _mediator.Publish(new NotificationMessage("Connection lost", $"Connection lost to service {_serverName}", NotificationType.Warning, TimeSpan.FromSeconds(10)));
                _mediator.Publish(new DisconnectedMessage(_serverIndex));
            }
            _sentDisconnected = true;
        }

        return timeToWait;
    }
}