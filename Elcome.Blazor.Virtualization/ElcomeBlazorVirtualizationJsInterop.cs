using Microsoft.JSInterop;

namespace Elcome.Blazor.Virtualization;

public class ElcomeBlazorVirtualizationJsInterop : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> moduleTask;

    public ElcomeBlazorVirtualizationJsInterop(IJSRuntime jsRuntime)
    {
        moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./_content/Elcome.Blazor.Virtualization/ts/bundle.ts.js").AsTask());
    }

    public async ValueTask<IJSObjectReference> GetModuleAsync()
    {
        return await moduleTask.Value;
    }

    public async ValueTask DisposeAsync()
    {
        if (moduleTask.IsValueCreated)
        {
            var module = await moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
