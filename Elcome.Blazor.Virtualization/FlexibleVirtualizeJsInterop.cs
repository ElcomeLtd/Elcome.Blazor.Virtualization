// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Elcome.Blazor.Virtualization;

internal sealed class FlexibleVirtualizeJsInterop : IAsyncDisposable
{
    private const string JsFunctionsPrefix = "FlexibleVirtualize";

    private readonly IFlexibleVirtualizeJsCallbacks _owner;

    private readonly ElcomeBlazorVirtualizationJsInterop _elcomeBlazorVirtualizationJsInterop;

    private DotNetObjectReference<FlexibleVirtualizeJsInterop>? _selfReference;

    [DynamicDependency(nameof(OnSpacerBeforeVisible))]
    [DynamicDependency(nameof(OnSpacerAfterVisible))]
    public FlexibleVirtualizeJsInterop(IFlexibleVirtualizeJsCallbacks owner, ElcomeBlazorVirtualizationJsInterop elcomeBlazorVirtualizationJsInterop)
    {
        _owner = owner;
        _elcomeBlazorVirtualizationJsInterop = elcomeBlazorVirtualizationJsInterop;
    }

    public async ValueTask InitializeAsync(ElementReference spacerBefore, ElementReference spacerAfter)
    {
        _selfReference = DotNetObjectReference.Create(this);
        var module = await _elcomeBlazorVirtualizationJsInterop.GetModuleAsync();
        await module.InvokeVoidAsync($"{JsFunctionsPrefix}.init", _selfReference, spacerBefore, spacerAfter);
    }

    [JSInvokable]
    public void OnSpacerBeforeVisible(float spacerSize, float spacerSeparation, float containerSize, float scrollDistance, float[] rowBounds)
    {
        _owner.OnBeforeSpacerVisible(spacerSize, spacerSeparation, containerSize, scrollDistance, rowBounds);
    }

    [JSInvokable]
    public void OnSpacerAfterVisible(float spacerSize, float spacerSeparation, float containerSize, float scrollDistance, float[] rowBounds)
    {
        _owner.OnAfterSpacerVisible(spacerSize, spacerSeparation, containerSize, scrollDistance, rowBounds);
    }

    public async ValueTask DisposeAsync()
    {
        if (_selfReference != null)
        {
            try
            {
                var module = await _elcomeBlazorVirtualizationJsInterop.GetModuleAsync();
                await module.InvokeVoidAsync($"{JsFunctionsPrefix}.dispose", _selfReference);
            }
            catch (JSDisconnectedException)
            {
                // If the browser is gone, we don't need it to clean up any browser-side state
            }
        }
    }
}
