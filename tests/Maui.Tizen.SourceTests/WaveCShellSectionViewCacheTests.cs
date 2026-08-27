using System.Collections.Generic;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Executable tests for lazy shell content creation and content switching.
/// </summary>
/// <remarks>
/// <c>TizenShellItemView</c> is an <c>NView</c> and cannot be instantiated off-device, so the
/// lazy-creation and current-section rules live in a NUI-free helper and are executed here. Mounting
/// the resulting view into the native tree remains device-only.
/// </remarks>
public class WaveCShellSectionViewCacheTests
{
	sealed class Section
	{
		public Section(string name) => Name = name;

		public string Name { get; }

		public override string ToString() => Name;
	}

	sealed class PlatformView
	{
		public PlatformView(Section section) => Section = section;

		public Section Section { get; }

		public bool Disposed { get; set; }
	}

	static ShellSectionViewCache<Section, PlatformView> NewCache() => new();

	// -----------------------------------------------------------------
	// Lazy creation
	// -----------------------------------------------------------------

	/// <summary>
	/// Nothing is created until a section actually becomes current.
	/// </summary>
	[Fact]
	public void NoViewIsCreatedBeforeASectionIsShown()
	{
		Assert.Equal(0, NewCache().CreatedCount);
	}

	[Fact]
	public void ShowingASectionCreatesItsViewOnce()
	{
		var cache = NewCache();
		var section = new Section("a");
		var created = 0;

		cache.SetCurrent(section, s => { created++; return new PlatformView(s); });

		Assert.Equal(1, created);
		Assert.Same(section, cache.CurrentSection);
		Assert.True(cache.IsCreated(section));
	}

	/// <summary>
	/// Returning to a section reuses its view rather than rebuilding it.
	/// </summary>
	/// <remarks>
	/// Rebuilding would silently discard that section's navigation stack and scroll position, which
	/// is the whole reason the cache exists.
	/// </remarks>
	[Fact]
	public void ReturningToASectionReusesItsView()
	{
		var cache = NewCache();
		var first = new Section("a");
		var second = new Section("b");
		var created = 0;

		var firstView = cache.SetCurrent(first, s => { created++; return new PlatformView(s); });
		cache.SetCurrent(second, s => { created++; return new PlatformView(s); });
		var reused = cache.SetCurrent(first, s => { created++; return new PlatformView(s); });

		Assert.Equal(2, created);
		Assert.Same(firstView, reused);
	}

	/// <summary>
	/// Only sections that have been shown are ever created.
	/// </summary>
	[Fact]
	public void UnvisitedSectionsAreNeverCreated()
	{
		var cache = NewCache();
		var shown = new Section("a");
		var never = new Section("b");

		cache.SetCurrent(shown, s => new PlatformView(s));

		Assert.True(cache.IsCreated(shown));
		Assert.False(cache.IsCreated(never));
		Assert.Equal(1, cache.CreatedCount);
	}

	// -----------------------------------------------------------------
	// Content switching
	// -----------------------------------------------------------------

	/// <summary>
	/// Switching unmounts the previous view - and must NOT dispose it.
	/// </summary>
	[Fact]
	public void SwitchingUnmountsThePreviousViewWithoutDisposingIt()
	{
		var cache = NewCache();
		var first = new Section("a");
		var unmounted = new List<PlatformView>();

		var firstView = cache.SetCurrent(first, s => new PlatformView(s), unmount: unmounted.Add);
		cache.SetCurrent(new Section("b"), s => new PlatformView(s), unmount: unmounted.Add);

		Assert.Equal(new[] { firstView }, unmounted);
		Assert.False(firstView!.Disposed);
	}

	[Fact]
	public void TheFirstSwitchUnmountsNothing()
	{
		var cache = NewCache();
		var unmounted = new List<PlatformView>();

		cache.SetCurrent(new Section("a"), s => new PlatformView(s), unmount: unmounted.Add);

		Assert.Empty(unmounted);
	}

	/// <summary>
	/// Re-selecting the section that is already current still round-trips cleanly.
	/// </summary>
	[Fact]
	public void ReselectingTheCurrentSectionKeepsTheSameView()
	{
		var cache = NewCache();
		var section = new Section("a");

		var first = cache.SetCurrent(section, s => new PlatformView(s));
		var again = cache.SetCurrent(section, s => new PlatformView(s));

		Assert.Same(first, again);
		Assert.Equal(1, cache.CreatedCount);
	}

	/// <summary>
	/// A null current section unmounts without creating anything.
	/// </summary>
	[Fact]
	public void ANullSectionClearsTheCurrentView()
	{
		var cache = NewCache();
		cache.SetCurrent(new Section("a"), s => new PlatformView(s));

		var result = cache.SetCurrent(null, s => new PlatformView(s));

		Assert.Null(result);
		Assert.Null(cache.CurrentSection);
		Assert.Null(cache.CurrentView);
	}

	// -----------------------------------------------------------------
	// Disposal
	// -----------------------------------------------------------------

	/// <summary>
	/// Removing a section disposes its view and forgets it.
	/// </summary>
	[Fact]
	public void RemovingASectionDisposesItsView()
	{
		var cache = NewCache();
		var section = new Section("a");
		var view = cache.SetCurrent(section, s => new PlatformView(s));

		Assert.True(cache.Remove(section, v => v.Disposed = true));

		Assert.True(view!.Disposed);
		Assert.False(cache.IsCreated(section));
	}

	/// <summary>
	/// Removing the mounted section must not leave the cache pointing at a disposed view.
	/// </summary>
	[Fact]
	public void RemovingTheCurrentSectionClearsTheCurrentTracking()
	{
		var cache = NewCache();
		var section = new Section("a");
		cache.SetCurrent(section, s => new PlatformView(s));

		cache.Remove(section, v => v.Disposed = true);

		Assert.Null(cache.CurrentSection);
		Assert.Null(cache.CurrentView);
	}

	[Fact]
	public void RemovingAnUnknownSectionIsANoOp()
	{
		Assert.False(NewCache().Remove(new Section("ghost"), v => v.Disposed = true));
	}

	/// <summary>
	/// Disposing the shell item disposes every cached section view, not just the mounted one.
	/// </summary>
	[Fact]
	public void ClearDisposesEveryCachedView()
	{
		var cache = NewCache();
		var views = new List<PlatformView>();

		foreach (var name in new[] { "a", "b", "c" })
		{
			var view = cache.SetCurrent(new Section(name), s => new PlatformView(s));
			views.Add(view!);
		}

		cache.Clear(v => v.Disposed = true);

		Assert.All(views, v => Assert.True(v.Disposed));
		Assert.Equal(0, cache.CreatedCount);
		Assert.Null(cache.CurrentView);
	}
}

/// <summary>
/// Source invariants for the Shell mounting path.
/// </summary>
/// <remarks>
/// The views involved are NUI types that cannot be instantiated in a host test, so these pin the
/// call sites at source level rather than leaving the fixes unguarded. Anything that CAN be executed
/// is executed above instead.
/// </remarks>
public class WaveCShellContentSourceTests
{
	static string ReadWaveCSource(string fileName)
		=> File.ReadAllText(WaveCSource.Files.Single(f => Path.GetFileName(f) == fileName));

	static string BodyOf(string source, string signature)
	{
		var body = source[source.IndexOf(signature, StringComparison.Ordinal)..];
		return body[..body.IndexOf("\n\t\t}", StringComparison.Ordinal)];
	}

	/// <summary>
	/// The shell root must actually mount its current item.
	/// </summary>
	/// <remarks>
	/// This was an empty method whose only content was a comment claiming the handler did the work.
	/// Nothing did, so the shell root rendered blank.
	/// </remarks>
	[Fact]
	public void TheShellViewMountsItsCurrentItem()
	{
		var body = BodyOf(ReadWaveCSource("TizenShellView.cs"), "public void UpdateCurrentItem(ShellItem? item)");

		Assert.Contains("ToHandler(MauiContext)", body, StringComparison.Ordinal);
		Assert.Contains("CurrentShellItemView", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// The shell item must mount its current section through the cache, so content is lazy.
	/// </summary>
	[Fact]
	public void TheShellItemMountsItsCurrentSectionThroughTheCache()
	{
		var body = BodyOf(ReadWaveCSource("TizenShellItemView.cs"), "public void UpdateCurrentItem(ShellSection");

		Assert.Contains("_shellSectionStackCache.SetCurrent", body, StringComparison.Ordinal);
		Assert.Contains("ToPlatform(MauiContext)", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// TabBarIsVisible must drive the tab bar, not a content-switch method.
	/// </summary>
	/// <remarks>
	/// The mapper used to call <c>UpdateCurrentItem()</c>, which at the time was itself an empty
	/// no-op - so toggling the tab bar at runtime did nothing at all.
	/// </remarks>
	[Fact]
	public void TabBarVisibilityDrivesTheTabBar()
	{
		var body = BodyOf(ReadWaveCSource("TizenShellItemHandler.cs"), "public static void MapTabBarIsVisible");

		Assert.Contains("UpdateTabBar", body, StringComparison.Ordinal);
		Assert.DoesNotContain("UpdateCurrentItem", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// Active tab bars select exactly one item and cannot be emptied by tapping the selection.
	/// </summary>
	[Fact]
	public void ActiveTabBarsUseSingleAlwaysSelection()
	{
		foreach (var file in new[] { "TizenShellItemView.cs", "TizenShellSectionView.cs" })
		{
			Assert.Contains("SingleAlways", ReadWaveCSource(file), StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// Shell item views must author Normal and Selected visuals.
	/// </summary>
	/// <remarks>
	/// Without them the default flyout and tab items render identically whether or not they are the
	/// current item, so there is no visible indication of where you are.
	/// </remarks>
	[Fact]
	public void DefaultShellItemViewsAuthorSelectionVisuals()
	{
		foreach (var file in new[]
		{
			"TizenShellFlyoutItemView.cs",
			"TizenShellSectionItemView.cs",
			"TizenShellContentItemView.cs",
		})
		{
			var source = ReadWaveCSource(file);

			Assert.Contains("VisualStateGroup", source, StringComparison.Ordinal);
			Assert.Contains("CommonStates.Selected", source, StringComparison.Ordinal);
			Assert.Contains("CommonStates.Normal", source, StringComparison.Ordinal);
		}
	}

	/// <summary>
	/// Shell adaptors must drive item state through the shared adapter, so recycled rows rebind.
	/// </summary>
	[Fact]
	public void ShellAdaptorsDriveItemStateThroughTheSharedAdapter()
	{
		foreach (var file in new[]
		{
			"TizenShellFlyoutItemAdaptor.cs",
			"TizenShellSectionItemAdaptor.cs",
			"TizenShellContentItemAdaptor.cs",
			"TizenShellSearchItemAdaptor.cs",
		})
		{
			var source = ReadWaveCSource(file);

			Assert.Contains("UpdateViewState", source, StringComparison.Ordinal);
			Assert.Contains("ItemSelectionState.", source, StringComparison.Ordinal);
			Assert.Contains("RemoveNativeView", source, StringComparison.Ordinal);
		}
	}
}
