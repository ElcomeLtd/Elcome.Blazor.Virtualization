// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Elcome.Blazor.Virtualization;

/// <summary>
/// Provides functionality for rendering a virtualized list of items with varying heights.
/// </summary>
/// <typeparam name="TItem">The <c>context</c> type for the items being rendered.</typeparam>
public sealed partial class FlexibleVirtualize<TItem> : ComponentBase, IFlexibleVirtualizeJsCallbacks, IAsyncDisposable
{
    private FlexibleVirtualizeJsInterop? _jsInterop;

    private ElementReference _spacerBefore;

    private ElementReference _spacerAfter;

    private int _itemsBefore;

    private int _visibleItemCapacity = 1;

    private int _itemCount = 1;

    private int _loadedItemsStartIndex;

    private int _lastRenderedItemCount;

    private int _lastRenderedPlaceholderCount;

    private float _itemSize;

    private float _minimumItemSize;

    private int _measuredItemsBefore;

    private int _measuredItemsAfter;

    private float _measuredHeightBefore;

    private float _measuredHeightAfter;

    private IEnumerable<TItem>? _loadedItems;

    private CancellationTokenSource? _refreshCts;

    private Exception? _refreshException;

    private ItemsProviderDelegate<TItem> _itemsProvider = default!;

    private RenderFragment<TItem>? _itemTemplate;

    private RenderFragment<FlexibleVirtualizePlaceholderContext>? _placeholder;

    private RenderFragment<FlexibleVirtualizeItemContext<TItem>>? _itemOrPlaceholder;

    private RenderFragment? _emptyContent;

    private bool _loading;

    [Inject]
    private ILogger<FlexibleVirtualize<TItem>> Logger { get; set; } = default!;

    [Inject]
    private ElcomeBlazorVirtualizationJsInterop ElcomeBlazorVirtualizationJsInterop { get; set; } = default!;

    /// <summary>
    /// Gets or sets the item template for the list.
    /// </summary>
    [Parameter]
    public RenderFragment<TItem>? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets the item template for the list.
    /// </summary>
    [Parameter]
    public RenderFragment<TItem>? ItemContent { get; set; }

    /// <summary>
    /// Gets or sets the template for items that have not yet been loaded in memory.
    /// </summary>
    [Parameter]
    public RenderFragment<FlexibleVirtualizePlaceholderContext>? Placeholder { get; set; }

    [Parameter]
    public RenderFragment<FlexibleVirtualizeItemContext<TItem>>? ItemOrPlaceholder { get; set; }

    /// <summary>
    /// Gets or sets the content to show when <see cref="Items"/> is empty
    /// or when the <see cref="ItemsProviderResult&lt;TItem&gt;.TotalItemCount"/> is zero.
    /// </summary>
    [Parameter]
    public RenderFragment? EmptyContent { get; set; }

    /// <summary>
    /// Gets the size of each item in pixels. Defaults to 50px.
    /// </summary>
    [Parameter]
    public float ItemSize { get; set; } = 50f;

    /// <summary>
    /// Gets or sets the function providing items to the list.
    /// </summary>
    [Parameter]
    public ItemsProviderDelegate<TItem>? ItemsProvider { get; set; }

    /// <summary>
    /// Gets or sets the fixed item source.
    /// </summary>
    [Parameter]
    public ICollection<TItem>? Items { get; set; }

    /// <summary>
    /// Gets or sets a value that determines how many additional items will be rendered
    /// before and after the visible region. This help to reduce the frequency of rendering
    /// during scrolling. However, higher values mean that more elements will be present
    /// in the page.
    /// </summary>
    [Parameter]
    public int OverscanCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the tag name of the HTML element that will be used as the virtualization spacer.
    /// One such element will be rendered before the visible items, and one more after them, using
    /// an explicit "height" style to control the scroll range.
    ///
    /// The default value is "div". If you are placing the <see cref="FlexibleVirtualize{TItem}"/> instance inside
    /// an element that requires a specific child tag name, consider setting that here. For example when
    /// rendering inside a "tbody", consider setting <see cref="SpacerElement"/> to the value "tr".
    /// </summary>
    [Parameter]
    public string SpacerElement { get; set; } = "div";

    /// <summary>
    /// Instructs the component to re-request data from its <see cref="ItemsProvider"/>.
    /// This is useful if external data may have changed. There is no need to call this
    /// when using <see cref="Items"/>.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the completion of the operation.</returns>
    public async Task RefreshDataAsync()
    {
        // We don't auto-render after this operation because in the typical use case, the
        // host component calls this from one of its lifecycle methods, and will naturally
        // re-render afterwards anyway. It's not desirable to re-render twice.
        await RefreshDataCoreAsync(renderOnSuccess: false);
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (ItemSize <= 0)
        {
            throw new InvalidOperationException(
                $"{GetType()} requires a positive value for parameter '{nameof(ItemSize)}'.");
        }

        if (_itemSize <= 0)
        {
            _itemSize = ItemSize;
        }

        if (_minimumItemSize <= 0)
        {
            _minimumItemSize = ItemSize;
        }

        if (ItemsProvider != null)
        {
            if (Items != null)
            {
                throw new InvalidOperationException(
                    $"{GetType()} can only accept one item source from its parameters. " +
                    $"Do not supply both '{nameof(Items)}' and '{nameof(ItemsProvider)}'.");
            }

            _itemsProvider = ItemsProvider;
        }
        else if (Items != null)
        {
            _itemsProvider = DefaultItemsProvider;

            // When we have a fixed set of in-memory data, it doesn't cost anything to
            // re-query it on each cycle, so do that. This means the developer can add/remove
            // items in the collection and see the UI update without having to call RefreshDataAsync.
            var refreshTask = RefreshDataCoreAsync(renderOnSuccess: false);

            // We know it's synchronous and has its own error handling
            Debug.Assert(refreshTask.IsCompletedSuccessfully);
        }
        else
        {
            throw new InvalidOperationException(
                $"{GetType()} requires either the '{nameof(Items)}' or '{nameof(ItemsProvider)}' parameters to be specified " +
                $"and non-null.");
        }

        _itemTemplate = ItemContent ?? ChildContent;
        _placeholder = Placeholder ?? DefaultPlaceholder;
        _itemOrPlaceholder = ItemOrPlaceholder;
        _emptyContent = EmptyContent;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _jsInterop = new FlexibleVirtualizeJsInterop(this, ElcomeBlazorVirtualizationJsInterop);
            await _jsInterop.InitializeAsync(_spacerBefore, _spacerAfter);
        }
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_refreshException != null)
        {
            var oldRefreshException = _refreshException;
            _refreshException = null;

            throw oldRefreshException;
        }

        builder.OpenElement(0, SpacerElement);
        builder.AddAttribute(1, "style", GetSpacerStyle(_itemsBefore * _itemSize));
        builder.AddElementReferenceCapture(2, elementReference => _spacerBefore = elementReference);
        builder.CloseElement();

        var lastItemIndex = Math.Min(_itemsBefore + _visibleItemCapacity, _itemCount);
        var renderIndex = _itemsBefore;
        var placeholdersBeforeCount = Math.Min(_loadedItemsStartIndex, lastItemIndex);


        _lastRenderedItemCount = 0;

        if (_loadedItems != null && !_loading && _itemCount == 0 && _emptyContent != null)
        {
            builder.AddContent(3, _emptyContent);

            _lastRenderedPlaceholderCount = 0;
        }
        else if (_itemTemplate != null || _itemOrPlaceholder != null)
        {
            builder.OpenRegion(4);

            // Render placeholders before the loaded items.
            for (; renderIndex < placeholdersBeforeCount; renderIndex++)
            {
                // Set the sequence number equal to the renderIndex.
                // This means that elements aren't reused for different rows, which would break scroll-anchoring.

                // This is a rare case where it's valid for the sequence number to be programmatically incremented.
                // This is only true because we know for certain that no other content will be alongside it.

                // Set overflowAnchorNone to true.
                // When scrolling up, the new placeholders may resize - we don't want to be anchored to one of these.
                // Instead, we'd prefer to be anchored to one of the loaded items below.

                BuildItemOrPlaceholder(builder, renderIndex, new FlexibleVirtualizeItemContext<TItem>(renderIndex, true, default!, _minimumItemSize, _itemSize, true));
            }

            if (_loadedItems != null)
            {
                var itemsToShow = _loadedItems
                    .Skip(_itemsBefore - _loadedItemsStartIndex)
                    .Take(lastItemIndex - _loadedItemsStartIndex);

                // Render the loaded items.
                foreach (var item in itemsToShow)
                {
                    // Set the sequence number equal to the renderIndex.
                    // This means that elements aren't reused for different rows, which would break scroll-anchoring.

                    // Set overflowAnchorNone to false.
                    // If an element above the viewport resizes, we want to be anchored to one of the loaded items.

                    BuildItemOrPlaceholder(builder, renderIndex, new FlexibleVirtualizeItemContext<TItem>(renderIndex, false, item, _minimumItemSize, _itemSize, false));

                    _lastRenderedItemCount++;
                    renderIndex++;
                }
            }

            _lastRenderedPlaceholderCount = Math.Max(0, lastItemIndex - _itemsBefore - _lastRenderedItemCount);

            // Render the placeholders after the loaded items.
            for (; renderIndex < lastItemIndex; renderIndex++)
            {
                // Set overflowAnchorNone to false.
                // When scrolling down, either there will be loaded items for us to anchor on, or we will
                // anchor onto one of the first of these placeholders.

                BuildItemOrPlaceholder(builder, renderIndex, new FlexibleVirtualizeItemContext<TItem>(renderIndex, true, default!, _minimumItemSize, _itemSize, false));
            }

            builder.CloseRegion();
        }

        var itemsAfter = Math.Max(0, _itemCount - _visibleItemCapacity - _itemsBefore);

        builder.OpenElement(7, SpacerElement);
        builder.AddAttribute(8, "style", GetSpacerStyle(itemsAfter * _itemSize));
        builder.AddElementReferenceCapture(9, elementReference => _spacerAfter = elementReference);

        builder.CloseElement();

        Logger.LogTrace("BuildRenderTree: _itemsBefore: {_itemsBefore}, _visibleItemCapacity: {_visibleItemCapacity}, _loadedItemsStartIndex: {_loadedItemsStartIndex}, _itemCount: {_itemCount}, _lastRenderedItemCount: {_lastRenderedItemCount}",
            _itemsBefore, _visibleItemCapacity, _loadedItemsStartIndex, _itemCount, _lastRenderedItemCount);
    }

    // Optimisation: when falling back to old placeholder/itemTemplates, call AddContent on them directly
    // rather than wrapping them in RenderFragment<VirtualizeItemContext<TItem>> which would cause twice
    // as many RenderFragment delegates to be created.
    private void BuildItemOrPlaceholder(RenderTreeBuilder builder, int renderIndex, FlexibleVirtualizeItemContext<TItem> context)
    {
        builder.OpenRegion(renderIndex);
        if (_itemOrPlaceholder is not null)
        {
            builder.AddContent(0, _itemOrPlaceholder, context);
        }
        else
        {
            // Fallback to using the old-style placeholders and item templates.
            if (context.IsPlaceholder)
            {
                builder.AddContent(1, _placeholder, new FlexibleVirtualizePlaceholderContext(context.Index, context.MinimumSize, context.OverflowAnchorNone));
            }
            else
            {
                builder.AddContent(2, _itemTemplate, context.Item);
            }
        }
        builder.CloseRegion();
    }

    private static string GetSpacerStyle(float height)
        => $"height: {height.ToString(CultureInfo.InvariantCulture)}px; flex-shrink: 0; overflow-anchor:none;";

    void IFlexibleVirtualizeJsCallbacks.OnBeforeSpacerVisible(float spacerSize, float spacerSeparation, float containerSize, float scrollDistance, float[] rowBounds)
    {
        Logger.LogTrace("OnBeforeSpacerVisible: spacerSize: {spacerSize}, spacerSeparation: {spacerSeparation}, containerSize: {containerSize}, scrollDistance: {scrollDistance}, rowBounds: {rowBounds}",
            spacerSize, spacerSeparation, containerSize, scrollDistance, rowBounds.Length);

        RecalculateItemSize(rowBounds);

        var overscanSize = OverscanCount * _itemSize;

        AdjustVisibleRegion(-scrollDistance - overscanSize, -(spacerSeparation + scrollDistance - containerSize) + overscanSize, rowBounds);
    }

    void IFlexibleVirtualizeJsCallbacks.OnAfterSpacerVisible(float spacerSize, float spacerSeparation, float containerSize, float scrollDistance, float[] rowBounds)
    {
        Logger.LogTrace("OnAfterSpacerVisible: spacerSize: {spacerSize}, spacerSeparation: {spacerSeparation}, containerSize: {containerSize}, scrollDistance: {scrollDistance}, rowBounds: {rowBounds}",
            spacerSize, spacerSeparation, containerSize, scrollDistance, rowBounds.Length);

        RecalculateItemSize(rowBounds);

        var overscanSize = OverscanCount * _itemSize;

        AdjustVisibleRegion(spacerSeparation + scrollDistance - containerSize - overscanSize, scrollDistance + overscanSize, rowBounds);
    }

    private void RecalculateItemSize(float[] rowBounds)
    {
        // Estimate item size by looking at _measuredHeightBefore/After, _measuredItemsBefore/After, and the rendered items via rowBounds
        {
            int count = _measuredItemsBefore + _measuredItemsAfter;
            float size = _measuredHeightBefore + _measuredHeightAfter;
            if (rowBounds.Length > 0)
            {
                count += rowBounds.Length / 2;
                size += rowBounds[rowBounds.Length - 1] - rowBounds[0];
            }
            if (size > 0 && count > 0)
                _itemSize = size / count;
        }

        // Keep track of the minimum item size
        for (int i = 1; i < rowBounds.Length; i += 2)
        {
            var itemSize = rowBounds[i] - rowBounds[i - 1];
            if (itemSize > 0 && itemSize < _minimumItemSize)
                _minimumItemSize = itemSize;
        }

        if (_itemSize <= 0)
        {
            // At this point, something unusual has occurred, likely due to misuse of this component.
            // Reset the calculated item size to the user-provided item size.
            _itemSize = ItemSize;
        }
    }

    // RowBounds contains the top and bottom positions of each element between the spacers.
    private void AdjustVisibleRegion(float deltaTop, float deltaBottom, float[] rowBounds)
    {
        CalculateBoundaryShrink(deltaTop, rowBounds, isTopBoundary: true, out var deltaMeasuredItemsBefore, out var deltaUnmeasuredItemsBefore);
        CalculateBoundaryShrink(-deltaBottom, rowBounds.AsSpan(deltaMeasuredItemsBefore), isTopBoundary: false, out var deltaMeasuredItemsAfter, out var deltaUnmeasuredItemsAfter);

        var itemsBefore = _itemsBefore;

        // Initialise visibleItemCapacity to the number of rendered items, since this is what determines spacerSeparation
        int renderedItemsAndPlaceholders = Math.Min(_visibleItemCapacity, Math.Max(0, _itemCount - _itemsBefore));
        var visibleItemCapacity = renderedItemsAndPlaceholders;

        itemsBefore += deltaMeasuredItemsBefore + deltaUnmeasuredItemsBefore;
        visibleItemCapacity -= deltaMeasuredItemsBefore + deltaUnmeasuredItemsBefore;

        visibleItemCapacity -= deltaMeasuredItemsAfter + deltaUnmeasuredItemsAfter;

        UpdateMeasure(rowBounds, deltaMeasuredItemsBefore, deltaUnmeasuredItemsBefore, deltaMeasuredItemsAfter, deltaUnmeasuredItemsAfter);

        UpdateItemDistribution(itemsBefore, visibleItemCapacity);

        Logger.LogTrace("AdjustVisibleRegion({deltaTop}, {deltaBottom}): itemsBefore={itemsBefore} visibleItemCapacity={visibleItemCapacity} itemSize={itemSize}",
            deltaTop, deltaBottom, itemsBefore, visibleItemCapacity, _itemSize);
    }

    /// <summary>
    /// If we are shrinking a boundary by <paramref name="maxHeight"/>, how many items can we remove from the visible region.
    /// Considers both `measured` items from <paramref name="rowBounds"/>, and `unmeasured` using the estimated item size.
    /// If the boundary is expanding, <paramref name="maxHeight"/> can be negative.
    /// </summary>
    private void CalculateBoundaryShrink(float maxHeight, ReadOnlySpan<float> rowBounds, bool isTopBoundary, out int measuredItemCount, out int unmeasuredItemCount)
    {
        measuredItemCount = 0;
        float measuredHeight = 0;

        // Calculate how many measured items can we take before reaching maxHeight
        if (maxHeight > 0)
            while (measuredItemCount * 2 < rowBounds.Length)
            {
                // If we are dealing with the top boundary, consider items from the beginning of the list
                // For the bottom boundary, consider items from the end of the list
                var candidateHeight = MeasureVisibleItems(rowBounds, measuredItemCount + 1, measureFromFront: isTopBoundary);

                if (candidateHeight > maxHeight)
                    break;

                measuredItemCount++;
                measuredHeight = candidateHeight;
            }

        // If we took all visible items and still have height to spare
        // (or are moving the boundary in the negative direction)
        if (maxHeight < 0 || measuredItemCount * 2 == rowBounds.Length)
        {
            // Calculate how many extra items we can fit, using the estimated itemSize
            // NB. maxHeight will be negative if the viewport size is increased, but this logic still holds - shrinking by a negative amount is expanding.
            unmeasuredItemCount = (int)Math.Floor((maxHeight - measuredHeight) / _itemSize);
        }
        else
        {
            unmeasuredItemCount = 0;
        }
    }

    private float MeasureVisibleItems(ReadOnlySpan<float> rowBounds, int count, bool measureFromFront)
    {
        if (count <= 0)
            return 0;
        else if (measureFromFront)
            return rowBounds[count * 2 - 1] - rowBounds[0];
        else
            return rowBounds[rowBounds.Length - 1] - rowBounds[rowBounds.Length - count * 2];
    }

    private void UpdateMeasure(float[] rowBounds,
        int deltaMeasuredItemsBefore, int deltaUnmeasuredItemsBefore,
        int deltaMeasuredItemsAfter, int deltaUnmeasuredItemsAfter)
    {
        // To calculate the estimated item size, we want to use the actual sizes of each row (given to us via rowBounds)
        // As you scroll down, rows will leave the visible region - we want to keep their measured sizes around to calculate a more accurate estimate
        // Therefore, we update _measuredItemsBefore and _measuredHeightBefore as rows leave the top of the visible region
        // When we scroll back up, items are removed from _measuredBefore, and put into _measuredAfter

        float deltaMeasuredHeightBefore = MeasureVisibleItems(rowBounds, deltaMeasuredItemsBefore, measureFromFront: true);
        float deltaMeasuredHeightAfter = MeasureVisibleItems(rowBounds, deltaMeasuredItemsAfter, measureFromFront: false);

        {
            // Try to convert unmeasured items to measured
            // ie. If we scroll down and are adding N measured items with size S to `before`,
            //     and removing N unmeasured items with estimated size E from `after`,
            //     we can instead ignore the estimated size E, and use S when removing items from `after`
            if (deltaUnmeasuredItemsAfter < 0 && deltaMeasuredItemsBefore > 0)
            {
                int convertItems = Math.Min(deltaMeasuredItemsBefore, -deltaUnmeasuredItemsAfter);
                deltaMeasuredItemsAfter -= convertItems;
                deltaMeasuredHeightAfter -= MeasureVisibleItems(rowBounds, convertItems, measureFromFront: true);
                deltaUnmeasuredItemsAfter += convertItems;
            }
            if (deltaUnmeasuredItemsBefore < 0 && deltaMeasuredItemsAfter > 0)
            {
                int convertItems = Math.Min(deltaMeasuredItemsAfter, -deltaUnmeasuredItemsBefore);
                deltaMeasuredItemsBefore -= convertItems;
                deltaMeasuredHeightBefore -= MeasureVisibleItems(rowBounds, convertItems, measureFromFront: false);
                deltaUnmeasuredItemsBefore += convertItems;
            }
        }

        _measuredItemsBefore += deltaMeasuredItemsBefore;
        _measuredHeightBefore += deltaMeasuredHeightBefore;
        _measuredItemsAfter += deltaMeasuredItemsAfter;
        _measuredHeightAfter += deltaMeasuredHeightAfter;

        // If a measure becomes negative, we have transfered all we can between before and after - we are effectively measuring new items
        if (_measuredItemsBefore <= 0 || _measuredHeightBefore <= 0)
        {
            // Avoid going negative by clamping at zero
            // (_measuredHeightBefore + _measuredHeightAfter) will increase
            _measuredItemsBefore = 0;
            _measuredHeightBefore = 0;
        }
        if (_measuredItemsAfter <= 0 || _measuredHeightAfter <= 0)
        {
            _measuredItemsAfter = 0;
            _measuredHeightAfter = 0;
        }

        // Apply unmeasured height changes
        // This happens when we scroll a long way in one step
        // In this case, we want to transfer items between `before` and `after`, but not add any new items to either (since we don't know their exact size)
        int sharedItems = 0;
        float sharedHeight = 0;
        // First remove items from `before` or `after`, and place in `shared`
        if (deltaUnmeasuredItemsBefore < 0)
        {
            deltaUnmeasuredItemsBefore = Math.Max(deltaUnmeasuredItemsBefore, -_measuredItemsBefore);
            float deltaHeight = deltaUnmeasuredItemsBefore * _itemSize;
            deltaHeight = Math.Max(deltaHeight, -_measuredHeightBefore);
            sharedItems -= deltaUnmeasuredItemsBefore;
            sharedHeight -= deltaHeight;
            _measuredItemsBefore += deltaUnmeasuredItemsBefore;
            _measuredHeightBefore += deltaHeight;
            if (_measuredItemsBefore <= 0 || _measuredHeightBefore <= 0)
            {
                _measuredItemsBefore = 0;
                _measuredHeightBefore = 0;
            }
        }
        if (deltaUnmeasuredItemsAfter < 0)
        {
            deltaUnmeasuredItemsAfter = Math.Max(deltaUnmeasuredItemsAfter, -_measuredItemsAfter);
            float deltaHeight = deltaUnmeasuredItemsAfter * _itemSize;
            deltaHeight = Math.Max(deltaHeight, -_measuredHeightAfter);
            sharedItems -= deltaUnmeasuredItemsAfter;
            sharedHeight -= deltaHeight;
            _measuredItemsAfter += deltaUnmeasuredItemsAfter;
            _measuredHeightAfter += deltaHeight;
            if (_measuredItemsAfter <= 0 || _measuredHeightAfter <= 0)
            {
                _measuredItemsAfter = 0;
                _measuredHeightAfter = 0;
            }
        }
        // Now remove items from `shared`, and place into `before` or `after`
        if (deltaUnmeasuredItemsBefore > 0)
        {
            deltaUnmeasuredItemsBefore = Math.Min(deltaUnmeasuredItemsBefore, sharedItems);
            float deltaHeight = deltaUnmeasuredItemsBefore * _itemSize;
            deltaHeight = Math.Min(deltaHeight, sharedHeight);
            sharedItems -= deltaUnmeasuredItemsBefore;
            sharedHeight -= deltaHeight;
            _measuredItemsBefore += deltaUnmeasuredItemsBefore;
            _measuredHeightBefore += deltaHeight;
        }
        if (deltaUnmeasuredItemsAfter > 0)
        {
            deltaUnmeasuredItemsAfter = Math.Min(deltaUnmeasuredItemsAfter, sharedItems);
            float deltaHeight = deltaUnmeasuredItemsAfter * _itemSize;
            deltaHeight = Math.Min(deltaHeight, sharedHeight);
            sharedItems -= deltaUnmeasuredItemsAfter;
            sharedHeight -= deltaHeight;
            _measuredItemsAfter += deltaUnmeasuredItemsAfter;
            _measuredHeightAfter += deltaHeight;
        }
    }


    private void UpdateItemDistribution(int itemsBefore, int visibleItemCapacity)
    {
        // If the itemcount just changed to a lower number, and we're already scrolled past the end of the new
        // reduced set of items, clamp the scroll position to the new maximum
        int itemsPastEnd = itemsBefore + visibleItemCapacity - _itemCount;
        if (itemsPastEnd > 0)
            itemsBefore -= itemsPastEnd;

        // Don't allow scrolling back before the first item
        if (itemsBefore <= 0)
            itemsBefore = 0;

        // Ensure measured values don't exceed `itemsBefore` and `_itemCount`
        {
            if (_measuredItemsBefore > itemsBefore)
            {
                _measuredItemsBefore = itemsBefore;
                _measuredHeightBefore = _measuredItemsBefore * _itemSize;
            }

            if (_measuredItemsBefore + visibleItemCapacity + _measuredItemsAfter > _itemCount)
            {
                _measuredItemsAfter = Math.Max(0, _itemCount - (_measuredItemsBefore + visibleItemCapacity));
                _measuredHeightAfter = _measuredItemsAfter * _itemSize;
            }
        }

        // If anything about the offset changed, re-render
        if (itemsBefore != _itemsBefore || visibleItemCapacity != _visibleItemCapacity)
        {
            _itemsBefore = itemsBefore;
            _visibleItemCapacity = visibleItemCapacity;
            var refreshTask = RefreshDataCoreAsync(renderOnSuccess: true);

            if (!refreshTask.IsCompleted)
            {
                StateHasChanged();
            }
        }
    }

    private async ValueTask RefreshDataCoreAsync(bool renderOnSuccess)
    {
        _refreshCts?.Cancel();
        CancellationToken cancellationToken;

        if (_itemsProvider == DefaultItemsProvider)
        {
            // If we're using the DefaultItemsProvider (because the developer supplied a fixed
            // Items collection) we know it will complete synchronously, and there's no point
            // instantiating a new CancellationTokenSource
            _refreshCts = null;
            cancellationToken = CancellationToken.None;
        }
        else
        {
            _refreshCts = new CancellationTokenSource();
            cancellationToken = _refreshCts.Token;
            _loading = true;
        }

        var request = new ItemsProviderRequest(_itemsBefore, _visibleItemCapacity, cancellationToken);

        try
        {
            var result = await _itemsProvider(request);

            // Only apply result if the task was not canceled.
            if (!cancellationToken.IsCancellationRequested)
            {
                _itemCount = result.TotalItemCount;
                _loadedItems = result.Items;
                _loadedItemsStartIndex = request.StartIndex;
                _loading = false;

                if (renderOnSuccess)
                {
                    StateHasChanged();
                }
            }
        }
        catch (Exception e)
        {
            if (e is OperationCanceledException oce && oce.CancellationToken == cancellationToken)
            {
                // No-op; we canceled the operation, so it's fine to suppress this exception.
            }
            else
            {
                // Cache this exception so the renderer can throw it.
                _refreshException = e;

                // Re-render the component to throw the exception.
                StateHasChanged();
            }
        }
    }

    private ValueTask<ItemsProviderResult<TItem>> DefaultItemsProvider(ItemsProviderRequest request)
    {
        return ValueTask.FromResult(new ItemsProviderResult<TItem>(
            Items!.Skip(request.StartIndex).Take(request.Count),
            Items!.Count));
    }

    private RenderFragment DefaultPlaceholder(FlexibleVirtualizePlaceholderContext context) => (builder) =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "style", $"height: {_itemSize.ToString(CultureInfo.InvariantCulture)}px; flex-shrink: 0;{(context.OverflowAnchorNone ? " overflow-anchor:none;" : "")}");
        builder.CloseElement();
    };

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _refreshCts?.Cancel();

        if (_jsInterop != null)
        {
            await _jsInterop.DisposeAsync();
        }
    }
}
