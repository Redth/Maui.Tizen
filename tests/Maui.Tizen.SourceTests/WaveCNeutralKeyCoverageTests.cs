using Microsoft.Maui.Controls;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Load-bearing tests for the neutral mapper keys Wave C must declare explicitly.
/// </summary>
/// <remarks>
/// <para>
/// Wave C declares its own complete mappers rather than chaining onto the neutral ones, because the
/// internal <c>RemapForControls</c> hook is unreachable out-of-tree. The cost of that decision is
/// that any key Controls adds to a neutral mapper at runtime has to be declared here too, or it is
/// silently never dispatched.
/// </para>
/// <para>
/// These tests exist because that failure is invisible: the handler compiles, the app runs, and one
/// property just stops working. They execute against real Controls types to prove the mapped
/// behaviour is genuinely needed, rather than asserting that a mapping exists.
/// </para>
/// </remarks>
public class WaveCNeutralKeyCoverageTests
{
	// -----------------------------------------------------------------
	// FlyoutLayoutBehavior
	// -----------------------------------------------------------------

	/// <summary>
	/// Proves the mapping is necessary: changing <c>FlyoutLayoutBehavior</c> changes the value the
	/// Tizen handler actually renders from.
	/// </summary>
	/// <remarks>
	/// <c>FlyoutLayoutBehavior</c> is a Controls-level property with no platform counterpart; it is
	/// projected into <see cref="IFlyoutView.FlyoutBehavior"/>. If Wave C did not map it, a runtime
	/// switch between Popover and Split would leave the drawer in its previous mode - the projected
	/// value changes but nothing tells the handler to re-read it.
	/// </remarks>
	[Fact]
	public void FlyoutLayoutBehaviorChangesTheProjectedFlyoutBehavior()
	{
		var page = new FlyoutPage
		{
			Flyout = new ContentPage { Title = "Flyout" },
			Detail = new ContentPage(),
			FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover,
		};

		// Split mode is only meaningful once the page has a size to split.
		((IView)page).Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, 1024, 768));

		var popover = ((IFlyoutView)page).FlyoutBehavior;

		page.FlyoutLayoutBehavior = FlyoutLayoutBehavior.Split;
		((IView)page).Arrange(new Microsoft.Maui.Graphics.Rect(0, 0, 1024, 768));

		var split = ((IFlyoutView)page).FlyoutBehavior;

		Assert.NotEqual(popover, split);
		Assert.Equal(FlyoutBehavior.Flyout, popover);
		Assert.Equal(FlyoutBehavior.Locked, split);
	}

	/// <summary>
	/// The Tizen flyout handler must declare the key, since it does not chain the neutral mapper.
	/// </summary>
	[Fact]
	public void TheFlyoutHandlerDeclaresFlyoutLayoutBehavior()
	{
		var handler = WaveCSource.Handlers.Single(h => h.TypeName == "TizenFlyoutViewHandler");

		Assert.Contains(handler.PropertyMappers, m => m.Key == "FlyoutLayoutBehavior");
	}

	/// <summary>
	/// The mapping must re-dispatch <c>FlyoutBehavior</c> rather than doing nothing.
	/// </summary>
	/// <remarks>
	/// A no-op body would satisfy "the key is declared" while still leaving the drawer stale, which
	/// is precisely the false-green this suite exists to prevent.
	/// </remarks>
	[Fact]
	public void TheFlyoutLayoutBehaviorMappingReDispatchesFlyoutBehavior()
	{
		var source = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Controls", "Navigation", "Handlers", "Navigation", "TizenFlyoutViewHandler.cs"));

		Assert.Contains("MapFlyoutLayoutBehavior", source, StringComparison.Ordinal);
		Assert.Contains("UpdateValue(nameof(IFlyoutView.FlyoutBehavior))", source, StringComparison.Ordinal);
	}

	// -----------------------------------------------------------------
	// ItemsView.IsVisible
	// -----------------------------------------------------------------

	/// <summary>
	/// Controls routes <c>IsVisible</c> through the items handler, so Wave C must map it.
	/// </summary>
	/// <remarks>
	/// The neutral <c>ItemsViewHandler</c> declares
	/// <c>[Controls.ItemsView.IsVisibleProperty.PropertyName] = MapIsVisible</c> rather than leaving
	/// it to the chained view mapper, because the platform view is the scrolling container. Wave C's
	/// port had dropped it, so hiding a CollectionView did nothing at all.
	/// </remarks>
	[Fact]
	public void TheItemsViewHandlerDeclaresIsVisible()
	{
		var handler = WaveCSource.Handlers.Single(h => h.TypeName == "TizenItemsViewHandler");

		Assert.Contains(handler.PropertyMappers, m => m.Key == "IsVisible");
	}

	/// <summary>
	/// The mapping must actually show or hide the platform view.
	/// </summary>
	[Fact]
	public void TheIsVisibleMappingShowsOrHidesThePlatformView()
	{
		var source = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Controls", "Navigation", "Handlers", "Items", "TizenItemsViewHandler.cs"));

		Assert.Contains("MapIsVisible", source, StringComparison.Ordinal);
		Assert.Contains("PlatformView.Show()", source, StringComparison.Ordinal);
		Assert.Contains("PlatformView.Hide()", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// No Wave C handler may leave a neutral key uncovered without it being recorded.
	/// </summary>
	/// <remarks>
	/// The recorded set is deliberately asserted to be empty. Wave C has no legitimate uncovered
	/// key today, and an empty expectation means a newly added neutral key surfaces as a failure
	/// here rather than being quietly appended to the manifest on the next regeneration.
	/// </remarks>
	[Fact]
	public void WaveCLeavesNoNeutralMapperKeyUncovered()
	{
		var manifest = File.ReadAllText(RepoPaths.Combine("docs", "wave-c-mapper-parity.json"));

		using var document = System.Text.Json.JsonDocument.Parse(manifest);

		var uncovered = document.RootElement
			.GetProperty("Handlers")
			.EnumerateArray()
			.SelectMany(h => h.GetProperty("UncoveredNeutralKeys")
				.EnumerateArray()
				.Select(k => $"{h.GetProperty("Handler").GetString()}.{k.GetString()}"))
			.ToList();

		Assert.True(
			uncovered.Count == 0,
			"These neutral mapper keys are not covered by Wave C. Implement them, or document a "
				+ "feature-specific no-op with a test that proves the behaviour is genuinely absent: "
				+ string.Join(", ", uncovered));
	}
}
