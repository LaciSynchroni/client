namespace SinusSynchronous.WebAPI.SignalR;

public class SinusAuthFailureException : Exception
{
    public SinusAuthFailureException(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}