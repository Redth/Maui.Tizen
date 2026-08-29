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
	public void CarouselPositionWaitsForLayoutAndRetries()
	{
		var coordinator = new DeferredCarouselPosition();
		var scrolled = new List<int>();
		coordinator.SetPosition(2);

		Assert.False(coordinator.TryApply(false, 4, _ => -1, scrolled.Add));
		Assert.Empty(scrolled);
		Assert.True(coordinator.TryApply(true, 4, _ => -1, scrolled.Add));
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

		Assert.True(coordinator.TryApply(true, 4, value => ReferenceEquals(value, item) ? 3 : -1, scrolled.Add));
		Assert.Equal([3], scrolled);
	}

	[Theory]
	[InlineData("query", 1, false, true)]
	[InlineData("", 1, false, false)]
	[InlineData("query", 0, false, false)]
	[InlineData("query", 1, true, false)]
	public void SearchResultsRequireAnActiveQueryAndVisibleSearchBox(
		string query,
		int count,
		bool hidden,
		bool expected) =>
		Assert.Equal(expected, SearchResultsLayout.IsVisible(query, count, hidden));

	[Fact]
	public void SearchResultsAreCappedAtHalfTheScreen()
	{
		Assert.Equal(300, SearchResultsLayout.ConstrainHeight(500, 600));
		Assert.Equal(200, SearchResultsLayout.ConstrainHeight(200, 600));
	}
}
