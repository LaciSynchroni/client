using Microsoft.Extensions.DependencyInjection;

namespace LaciSynchroni.UI.Components;

public static class PendingRequestsProviderExtensions
{
    /// <summary>
    /// Registers every <see cref="IPendingRequestsProvider"/> in this assembly, so sync services can add their own
    /// without being named here.
    /// </summary>
    public static IServiceCollection AddPendingRequestsProviders(this IServiceCollection collection)
    {
        var providers = typeof(IPendingRequestsProvider).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IPendingRequestsProvider)));
        foreach (var provider in providers)
        {
            collection.AddScoped(typeof(IPendingRequestsProvider), provider);
        }
        return collection;
    }
}
