using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCItemsScrollCoordinatorTests
{
	[Fact]
	public void PublishesMetricsAndThreshold()
	{
		Microsoft.Maui.Controls.ItemsViewScrolledEventArgs? observed = null;
		var threshold = 0;

		ItemsScrollCoordinator.Publish(
			10, 2, 1, 2, 3, 4, 5, 6, 7,
			args => observed = args,
			() => threshold++);

		Assert.NotNull(observed);
		Assert.Equal(6, observed.CenterItemIndex);
		Assert.Equal(1, threshold);
	}

	[Fact]
	public void CarouselNativeFeedbackIsBidirectionalAndBoundsChecked()
	{
		var coordinator = new CarouselFeedbackCoordinator();
		var position = -1;
		object? current = null;

		Assert.True(coordinator.ApplyNative(
			1,
			2,
			index => new[] { "a", "b" }[index],
			value => position = value,
			value => current = value));

		Assert.Equal(1, position);
		Assert.Equal("b", current);
		Assert.False(coordinator.ApplyNative(2, 2, _ => null, _ => { }, _ => { }));
	}

	[Fact]
	public void CarouselManagedPushSuppressesNativeEcho()
	{
		var coordinator = new CarouselFeedbackCoordinator();
		var applied = true;

		coordinator.ApplyManaged(0, () =>
			applied = coordinator.ApplyNative(0, 1, _ => "a", _ => { }, _ => { }));

		Assert.False(applied);
	}

	[Fact]
	public void DeferredNativeEchoOfManagedPositionIsSuppressed()
	{
		var coordinator = new CarouselFeedbackCoordinator();
		coordinator.ApplyManaged(3, () => { });

		var applied = coordinator.ApplyNative(3, 5, index => index, _ => { }, _ => { });

		Assert.False(applied);
	}

	[Fact]
	public void ManagedCurrentItemUpdatesItsCompanionPosition()
	{
		var coordinator = new CarouselFeedbackCoordinator();
		var item = new object();
		var position = -1;
		var nativeUpdates = 0;

		Assert.True(coordinator.ApplyManagedCurrentItem(
			item,
			3,
			value => ReferenceEquals(value, item) ? 2 : -1,
			value => position = value,
			() => nativeUpdates++));

		Assert.Equal(2, position);
		Assert.Equal(1, nativeUpdates);
		Assert.False(coordinator.ApplyNative(2, 3, _ => item, _ => { }, _ => { }));
	}

	[Fact]
	public void ManagedPositionUpdatesItsCompanionCurrentItem()
	{
		var coordinator = new CarouselFeedbackCoordinator();
		var items = new object[] { "a", "b" };
		object? current = null;

		Assert.True(coordinator.ApplyManagedPosition(
			1,
			items.Length,
			index => items[index],
			value => current = value,
			() => { }));

		Assert.Equal("b", current);
	}

	[Fact]
	public void EmptyCarouselPlaceholderIsNeverProjectedAsCurrentItem()
	{
		var coordinator = new CarouselFeedbackCoordinator();
		var current = new object();
		var nativeUpdates = 0;

		Assert.Equal(0, LogicalItemsProjection.Count(physicalCount: 1, isInternalPlaceholder: true));
		Assert.False(coordinator.ApplyManagedPosition(
			0,
			count: 0,
			_ => throw new InvalidOperationException("The placeholder must not be projected."),
			value => current = value,
			() => nativeUpdates++));
		Assert.False(coordinator.ApplyNative(
			0,
			count: 0,
			_ => throw new InvalidOperationException("The placeholder must not be projected."),
			_ => { },
			value => current = value));
		Assert.NotNull(current);
		Assert.Equal(0, nativeUpdates);

		current = null;
		Assert.False(coordinator.ApplyManagedPosition(
			0,
			count: 0,
			_ => throw new InvalidOperationException("The placeholder must not be projected."),
			value => current = value,
			() => nativeUpdates++));
		Assert.Null(current);
	}

	[Theory]
	[InlineData(2, 2, true, false, false)]
	[InlineData(2, 1, false, true, false)]
	[InlineData(2, 1, true, true, true)]
	public void CarouselNoOpPositionRefreshDoesNotStartScrolling(
		int target,
		int last,
		bool animate,
		bool shouldScroll,
		bool startsScrolling)
	{
		Assert.Equal(shouldScroll, CarouselPositionDecision.ShouldScroll(target, last));
		Assert.Equal(startsScrolling, CarouselPositionDecision.StartsScrolling(shouldScroll, animate));
	}

	[Fact]
	public void CarouselPositionWaitsForLayoutAndRetries()
	{
		var coordinator = new DeferredCarouselPosition();
		var scrolled = new List<int>();
		coordinator.SetPosition(2);

		Assert.False(coordinator.TryApply(false, 4, _ => -1, (index, _) => scrolled.Add(index)));
		Assert.Empty(scrolled);
		Assert.True(coordinator.TryApply(true, 4, _ => -1, (index, _) => scrolled.Add(index)));
		Assert.Equal([2], scrolled);
	}

	[Fact]
	public void CurrentItemSupersedesAPendingPosition()
	{
		var coordinator = new DeferredCarouselPosition();
		var item = new object();
		var scrolled = new List<int>();
		coordinator.SetPosition(1);
		coordinator.SetCurrentItem(item);

		Assert.True(coordinator.TryApply(
			true,
			4,
			value => ReferenceEquals(value, item) ? 3 : -1,
			(index, _) => scrolled.Add(index)));
		Assert.Equal([3], scrolled);
	}

	[Fact]
	public void CarouselDeferredTargetsRetainTheirAnimationChoice()
	{
		var coordinator = new DeferredCarouselPosition();
		var observed = (Index: -1, Animate: false);
		coordinator.SetPosition(2, animate: true);

		coordinator.TryApply(true, 3, _ => -1, (index, animate) => observed = (index, animate));

		Assert.Equal((2, true), observed);
	}

	[Theory]
	[InlineData(2, 2, CarouselView.CurrentItemVisualState)]
	[InlineData(1, 2, CarouselView.PreviousItemVisualState)]
	[InlineData(3, 2, CarouselView.NextItemVisualState)]
	[InlineData(0, 2, CarouselView.DefaultItemVisualState)]
	public void CarouselVisualStateUsesCurrentAndAdjacentPositions(int index, int current, string expected) =>
		Assert.Equal(expected, CarouselVisualState.ForIndex(index, current));

	[Fact]
	public void CarouselScrollingRemainsTrueBetweenDragEndAndAnimationEnd()
	{
		var state = new CarouselInteractionState();

		state.BeginDrag();
		Assert.True(state.IsDragging);
		Assert.True(state.IsScrolling);

		state.BeginAnimation();
		state.EndDrag();
		Assert.False(state.IsDragging);
		Assert.True(state.IsScrolling);

		state.EndAnimation();
		Assert.False(state.IsScrolling);
	}

	[Fact]
	public void CarouselRetriesItsTargetOnlyForValidOrChangedBounds()
	{
		var viewport = new CarouselViewportTracker();

		Assert.False(viewport.Update(0, 100));
		Assert.True(viewport.Update(100, 200));
		Assert.False(viewport.Update(100, 200));
		Assert.True(viewport.Update(200, 100));
		viewport.Reset();
		Assert.True(viewport.Update(200, 100));
	}

	[Fact]
	public void ItemsLayoutSnapshotReflectsRuntimeSpanSpacingAndSnapChanges()
	{
		var layout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical)
		{
			HorizontalItemSpacing = 3,
			VerticalItemSpacing = 4,
			SnapPointsType = SnapPointsType.Mandatory,
			SnapPointsAlignment = SnapPointsAlignment.Center,
		};

		var initial = ItemsLayoutSnapshot.Capture(layout);
		layout.Span = 4;
		layout.HorizontalItemSpacing = 7;
		layout.VerticalItemSpacing = 8;
		layout.SnapPointsType = SnapPointsType.MandatorySingle;
		layout.SnapPointsAlignment = SnapPointsAlignment.End;
		var updated = ItemsLayoutSnapshot.Capture(layout);

		Assert.Equal(2, initial.Span);
		Assert.Equal(3, initial.HorizontalItemSpacing);
		Assert.Equal(4, initial.VerticalItemSpacing);
		Assert.Equal(SnapPointsType.Mandatory, initial.SnapPointsType);
		Assert.Equal(4, updated.Span);
		Assert.Equal(7, updated.HorizontalItemSpacing);
		Assert.Equal(8, updated.VerticalItemSpacing);
		Assert.Equal(SnapPointsType.MandatorySingle, updated.SnapPointsType);
		Assert.Equal(SnapPointsAlignment.End, updated.SnapPointsAlignment);
		Assert.Equal(1, updated.EffectiveSpan(forceSingleSpan: true));
		Assert.Equal(4, updated.EffectiveSpan(forceSingleSpan: false));
	}

	[Theory]
	[InlineData("query", 1, true, true, false, true)]
	[InlineData("", 1, true, true, false, false)]
	[InlineData("query", 0, true, true, false, false)]
	[InlineData("query", 1, false, true, false, false)]
	[InlineData("query", 1, true, false, false, false)]
	[InlineData("query", 1, true, true, true, false)]
	public void SearchResultsRequireAnActiveQueryAndVisibleSearchBox(
		string query,
		int count,
		bool enabled,
		bool showsResults,
		bool hidden,
		bool expected) =>
		Assert.Equal(expected, SearchResultsLayout.IsVisible(query, count, enabled, showsResults, hidden));

	[Fact]
	public void SearchResultsAreCappedAtHalfTheScreen()
	{
		Assert.Equal(300, SearchResultsLayout.ConstrainHeight(500, 600));
		Assert.Equal(200, SearchResultsLayout.ConstrainHeight(200, 600));
	}

	[Fact]
	public void SearchResultMeasurementStopsAtTheViewportCap()
	{
		var realized = 0;

		var measured = SearchResultsLayout.MeasureUntilCap(Heights(), 250);

		Assert.Equal(250, measured);
		Assert.Equal(3, realized);

		IEnumerable<double> Heights()
		{
			foreach (var height in new[] { 100d, 100d, 100d, 100d })
			{
				realized++;
				yield return height;
			}
		}
	}

	[Fact]
	public void SearchResultMeasurementIsCachedUntilInvalidatedOrWidthChanges()
	{
		var cache = new SearchResultsMeasurementCache();
		var calls = 0;

		Assert.Equal(120, cache.GetOrMeasure(300, Measure));
		Assert.Equal(120, cache.GetOrMeasure(300, Measure));
		Assert.Equal(1, calls);

		Assert.Equal(120, cache.GetOrMeasure(400, Measure));
		Assert.Equal(2, calls);

		cache.Invalidate();
		Assert.Equal(120, cache.GetOrMeasure(400, Measure));
		Assert.Equal(3, calls);

		double Measure()
		{
			calls++;
			return 120;
		}
	}

	[Theory]
	[InlineData(SearchBoxVisibility.Collapsible, false, "", true)]
	[InlineData(SearchBoxVisibility.Collapsible, true, "", false)]
	[InlineData(SearchBoxVisibility.Collapsible, false, "query", false)]
	[InlineData(SearchBoxVisibility.Expanded, false, "", false)]
	public void CollapsibleSearchExpandsForFocusOrAnActiveQuery(
		SearchBoxVisibility visibility,
		bool focused,
		string query,
		bool expected) =>
		Assert.Equal(expected, SearchResultsLayout.IsCollapsed(visibility, focused, query));

	[Theory]
	[InlineData(true, true, false, true)]
	[InlineData(true, false, false, false)]
	[InlineData(true, true, true, false)]
	[InlineData(false, true, false, false)]
	public void SearchFocusRequiresAnEnabledVisibleRequest(
		bool requested,
		bool enabled,
		bool hidden,
		bool expected) =>
		Assert.Equal(expected, SearchResultsLayout.ShouldFocusNative(requested, enabled, hidden));
}
