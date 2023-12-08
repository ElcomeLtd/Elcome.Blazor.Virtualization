// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Elcome.Blazor.Virtualization;

/// <summary>
/// Contains context for a placeholder in a virtualized list.
/// </summary>
public readonly struct FlexibleVirtualizePlaceholderContext
{
    /// <summary>
    /// The item index of the placeholder.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// The size of the placeholder in pixels.
    /// <para>
    /// For virtualized components with vertical scrolling, this would be the height of the placeholder in pixels.
    /// For virtualized components with horizontal scrolling, this would be the width of the placeholder in pixels.
    /// </para>
    /// </summary>
    public float Size { get; }

    public bool OverflowAnchorNone { get; }

    /// <summary>
    /// Constructs a new <see cref="FlexibleVirtualizePlaceholderContext"/> instance.
    /// </summary>
    /// <param name="index">The item index of the placeholder.</param>
    /// <param name="size">The size of the placeholder in pixels.</param>
    public FlexibleVirtualizePlaceholderContext(int index, float size = 0f, bool overflowAnchorNone = false)
    {
        Index = index;
        Size = size;
        OverflowAnchorNone = overflowAnchorNone;
    }
}
