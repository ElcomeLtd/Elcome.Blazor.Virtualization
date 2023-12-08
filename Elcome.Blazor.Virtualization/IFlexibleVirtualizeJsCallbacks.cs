// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Elcome.Blazor.Virtualization;

internal interface IFlexibleVirtualizeJsCallbacks
{
    void OnBeforeSpacerVisible(float spacerSize, float spacerSeparation, float containerSize, float scrollDistance, float[] rowBounds);
    void OnAfterSpacerVisible(float spacerSize, float spacerSeparation, float containerSize, float scrollDistance, float[] rowBounds);
}
