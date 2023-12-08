// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Elcome.Blazor.Virtualization;

public readonly struct FlexibleVirtualizeItemContext<TItem>
{
    /// <summary>
    /// The item index.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// If true, this context represents a placeholder, and accessing <see cref="Item"/> will throw an exception.
    /// </summary>
    public bool IsPlaceholder { get; }

    private readonly TItem _item;

    public TItem Item => IsPlaceholder ? throw new InvalidOperationException() : _item;

    public float MinimumSize { get; }

    public float AverageSize { get; }

    /// <summary>
    /// If true, the component should have the `overflow-anchor` style set to `none`.
    /// This should reduce unintended scrolling when a placeholder above the viewport resizes.
    /// </summary>
    public bool OverflowAnchorNone { get; }

    public FlexibleVirtualizeItemContext(int index, bool isPlaceholder, TItem item, float minimumSize, float averageSize, bool overflowAnchorNone)
    {
        Index = index;
        IsPlaceholder = isPlaceholder;
        _item = item;
        MinimumSize = minimumSize;
        AverageSize = averageSize;
        OverflowAnchorNone = overflowAnchorNone;
    }
}