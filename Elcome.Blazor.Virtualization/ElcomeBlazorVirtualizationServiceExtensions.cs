namespace Microsoft.Extensions.DependencyInjection;

public static class ElcomeBlazorVirtualizationServiceExtensions
{
    public static IServiceCollection AddElcomeBlazorVirtualization(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<Elcome.Blazor.Virtualization.ElcomeBlazorVirtualizationJsInterop>();
        return serviceCollection;
    }
}
