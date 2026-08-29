namespace Maui.Tizen.SourceTests;

public class WaveCShellWiringTests
{
	static string Read(string fileName) =>
		File.ReadAllText(WaveCSource.Files.Single(path => Path.GetFileName(path) == fileName));

	[Fact]
	public void ShellAndNavigationHandlersExposeReachableToolbarMappings()
	{
		foreach (var handlerName in new[] { "TizenShellHandler", "TizenNavigationViewHandler" })
		{
			var handler = WaveCSource.Handlers.Single(source => source.TypeName == handlerName);
			Assert.Contains(handler.PropertyMappers, mapper => mapper.Key == "Toolbar");
		}
	}

	[Fact]
	public void NavigationToolbarAttachAndDetachAreSymmetric()
	{
		var source = Read("TizenNavigationViewHandler.cs");
		var flyout = Read("TizenFlyoutViewHandler.cs");

		Assert.Contains("PlatformView.SetToolbar", source, StringComparison.Ordinal);
		Assert.Contains("container.DetachToolbar(platformToolbar)", source, StringComparison.Ordinal);
		Assert.Contains("elementHandler?.DisconnectHandler()", source, StringComparison.Ordinal);
		Assert.Contains("platformToolbar.Dispose", source, StringComparison.Ordinal);
		Assert.Contains("container.SetToolbar(platformToolbar)", flyout, StringComparison.Ordinal);
		Assert.Contains("container.DetachToolbar(platformToolbar)", flyout, StringComparison.Ordinal);
		Assert.Contains("elementHandler?.DisconnectHandler()", flyout, StringComparison.Ordinal);
	}

	[Fact]
	public void NavigationHandlerRebindsAndResynchronizesItsStack()
	{
		var source = Read("TizenNavigationViewHandler.cs");
		var start = source.IndexOf("public override void SetVirtualView", StringComparison.Ordinal);
		var end = source.IndexOf("protected override TizenStackNavigationManager CreatePlatformView", start, StringComparison.Ordinal);
		var body = source[start..end];

		Assert.Contains("platformView.Disconnect()", body, StringComparison.Ordinal);
		Assert.Contains("base.SetVirtualView(view)", body, StringComparison.Ordinal);
		Assert.Contains("platformView.Connect(VirtualView)", body, StringComparison.Ordinal);
		Assert.Contains("SyncNavigationStack(platformView)", body, StringComparison.Ordinal);
	}

	[Fact]
	public void ShellSearchViewOwnsQueryResultsAndCommandSubscriptions()
	{
		var source = Read("TizenShellSearchView.cs");

		Assert.Contains("TizenSearchBarView", source, StringComparison.Ordinal);
		Assert.Contains("SearchButtonPressed += OnSearchButtonPressed", source, StringComparison.Ordinal);
		Assert.Contains("ListProxyChanged += OnListProxyChanged", source, StringComparison.Ordinal);
		Assert.Contains("PropertyChanged += OnSearchHandlerPropertyChanged", source, StringComparison.Ordinal);
		Assert.Contains("FocusChangeRequested += OnFocusChangeRequested", source, StringComparison.Ordinal);
		Assert.Contains("SetIsFocused(true)", source, StringComparison.Ordinal);
		Assert.Contains("SearchHandler.ShowsResults", source, StringComparison.Ordinal);
		Assert.Contains("IsSearchEnabled: true, ShowsResults: true", source, StringComparison.Ordinal);
		Assert.Contains("SearchResultsLayout.IsCollapsed", source, StringComparison.Ordinal);
		Assert.Contains("SetCollapsed(false)", source, StringComparison.Ordinal);
		Assert.Contains("UnfocusEntry();", source, StringComparison.Ordinal);
		Assert.Contains("ItemSelected", source, StringComparison.Ordinal);
	}

	[Fact]
	public void FlyoutChromeUsesControllerRealizedViews()
	{
		var shell = Read("TizenShellView.cs");
		var adaptor = Read("TizenShellFlyoutItemAdaptor.cs");

		Assert.Contains("ShellController.FlyoutHeader", shell, StringComparison.Ordinal);
		Assert.Contains("ShellController.FlyoutFooter", shell, StringComparison.Ordinal);
		Assert.Contains("ShellController.FlyoutContent", shell, StringComparison.Ordinal);
		Assert.Contains("ItemSizingStrategy.MeasureAllItems", shell, StringComparison.Ordinal);
		Assert.DoesNotContain("ResolveFlyoutItemTemplate", adaptor[..adaptor.IndexOf("CreateItemView", StringComparison.Ordinal)], StringComparison.Ordinal);
	}

	[Fact]
	public void ClearingCustomFlyoutContentRestoresTheDefaultCollection()
	{
		var source = Read("TizenShellView.cs");
		var start = source.IndexOf("public void UpdateFlyoutContent()", StringComparison.Ordinal);
		var end = source.IndexOf("public void UpdateToolbar()", start, StringComparison.Ordinal);
		var body = source[start..end];

		Assert.Contains("_customFlyoutContent.Handler", body, StringComparison.Ordinal);
		Assert.Equal(2, body.Split("UpdateFlyoutHeader(Shell)").Length - 1);
	}

	[Fact]
	public void TabbedPageDoesNotDuplicateMultiPageAppearanceOrCreateHandlersDuringDisconnect()
	{
		var source = Read("TizenTabbedPageView.cs");

		Assert.DoesNotContain("SendAppearing", source, StringComparison.Ordinal);
		Assert.DoesNotContain("SendDisappearing", source, StringComparison.Ordinal);
		var disconnect = source[source.IndexOf("public void DisconnectHandler()", StringComparison.Ordinal)..];
		Assert.DoesNotContain("ToHandler(", disconnect, StringComparison.Ordinal);
		Assert.Contains("_realizedPageHandlers", disconnect, StringComparison.Ordinal);
	}

	[Fact]
	public void CustomFlyoutNeverRebuildsGeneratedContentAndFlyoutItemsHasAMapper()
	{
		var handler = WaveCSource.Handlers.Single(source => source.TypeName == "TizenShellHandler");
		var source = Read("TizenShellView.cs");

		Assert.Contains(handler.PropertyMappers, mapper => mapper.Key == "FlyoutItems");
		var start = source.IndexOf("public void UpdateFlyoutHeader", StringComparison.Ordinal);
		var end = source.IndexOf("public void UpdateFlyoutFooter", start, StringComparison.Ordinal);
		Assert.Contains("FlyoutHeaderOwnership.UseScrollingHeader", source[start..end], StringComparison.Ordinal);
		Assert.Contains("FlyoutHeaderOwnership.UseFixedHeader", source[start..end], StringComparison.Ordinal);
		Assert.Contains("ReleaseFlyoutAdaptor();", source, StringComparison.Ordinal);
	}

	[Fact]
	public void FlyoutSelectionAwaitsPublicControllerAndResynchronizesHierarchy()
	{
		var source = Read("TizenShellView.cs");

		var start = source.IndexOf("async Task HandleFlyoutItemSelectedAsync", StringComparison.Ordinal);
		var end = source.IndexOf("void SynchronizeFlyoutSelection", start, StringComparison.Ordinal);
		var body = source[start..end];
		Assert.Contains("OnFlyoutItemSelectedAsync", body, StringComparison.Ordinal);
		Assert.Contains("_flyoutSelectionResynchronizer.RunAsync", body, StringComparison.Ordinal);
		Assert.Contains("HierarchySelectionResolver.Resolve", source, StringComparison.Ordinal);
		Assert.Contains("SynchronizeFlyoutSelection", body, StringComparison.Ordinal);
	}

	[Fact]
	public void SearchOwnsOnlyItsToolbarSlotAndCleansInheritedEvents()
	{
		var shell = Read("TizenShellView.cs");
		var search = Read("TizenShellSearchView.cs");
		var toolbar = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenToolbarView.cs"));

		Assert.Contains("ReferenceEquals(toolbar.SearchBar, _searchView)", shell, StringComparison.Ordinal);
		Assert.Contains("_contentSlot.Current", toolbar, StringComparison.Ordinal);
		Assert.Contains("DisconnectEvents,", search, StringComparison.Ordinal);
		Assert.Contains("LayoutUpdated += OnShellLayoutUpdated", search, StringComparison.Ordinal);
		Assert.Contains("protected override void LayoutContent", search, StringComparison.Ordinal);
		Assert.Contains("ItemMeasureInvalidated += OnResultMeasureInvalidated", search, StringComparison.Ordinal);
		Assert.Contains("SizeHeight = desiredHeight", search, StringComparison.Ordinal);
		Assert.Contains("RequestLayout();", search, StringComparison.Ordinal);
	}

	[Fact]
	public void TabbedPageTracksRemovedHandlersAndRebuildsAfterMoves()
	{
		var source = Read("TizenTabbedPageView.cs");

		Assert.Contains("PagesChanged += OnPagesChanged", source, StringComparison.Ordinal);
		Assert.Contains("_realizedPageHandlers", source, StringComparison.Ordinal);
		Assert.Contains("ReleaseRemoved(_tabbedPage.Children", source, StringComparison.Ordinal);
		Assert.Contains("_tabbedView.Adaptor = null", source, StringComparison.Ordinal);
		Assert.Contains("UpdateCurrentPage();", source, StringComparison.Ordinal);
	}

	[Fact]
	public void AnimatedPopDetachesContentInsideWrapperDisposal()
	{
		var wrapper = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenNaviPage.cs"));
		var manager = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenStackNavigationManager.cs"));
		var awaitPop = manager.IndexOf("await PlatformNavigation.Pop(true)", StringComparison.Ordinal);
		var removal = manager.IndexOf("_pageMap.Remove(page)", awaitPop, StringComparison.Ordinal);
		var nextDetach = manager.IndexOf("wrapper.DetachContent()", awaitPop, StringComparison.Ordinal);

		var disposeStart = wrapper.IndexOf("protected override void Dispose", StringComparison.Ordinal);
		var disposeBody = wrapper[disposeStart..];
		Assert.Contains("DetachContent(resubscribe: false);", disposeBody, StringComparison.Ordinal);
		Assert.True(awaitPop >= 0 && removal > awaitPop);
		Assert.True(nextDetach < 0 || nextDetach > manager.IndexOf("else", awaitPop, StringComparison.Ordinal));
	}

	[Fact]
	public void ShellDetachesToolbarBeforeItsHandlerOwnsDisposal()
	{
		var source = Read("TizenShellView.cs");
		var start = source.IndexOf("void ReleaseToolbar()", StringComparison.Ordinal);
		var end = source.IndexOf("void ClearOwnedSearchBar", start, StringComparison.Ordinal);
		var body = source[start..end];

		var detach = body.IndexOf("container.DetachToolbar(platformToolbar)", StringComparison.Ordinal);
		var disconnect = body.IndexOf("elementHandler?.DisconnectHandler()", StringComparison.Ordinal);
		var dispose = body.IndexOf("platformToolbar.Dispose", StringComparison.Ordinal);
		Assert.True(detach >= 0 && disconnect > detach && dispose > disconnect);
		Assert.Contains("toolbarElement.Handler = null", body, StringComparison.Ordinal);
	}

	[Fact]
	public void TabAppearanceUsesEffectiveValuesAndSurvivesLazyRootCreation()
	{
		var itemHandler = Read("TizenShellItemHandler.cs");
		var sectionHandler = Read("TizenShellSectionHandler.cs");
		var stack = Read("TizenShellSectionStackManager.cs");

		Assert.Contains("EffectiveTabBarBackgroundColor", itemHandler, StringComparison.Ordinal);
		Assert.Contains("EffectiveTabBarTitleColor", itemHandler, StringComparison.Ordinal);
		Assert.Contains("EffectiveTabBarUnselectedColor", itemHandler, StringComparison.Ordinal);
		Assert.Contains("EffectiveTabBarBackgroundColor", sectionHandler, StringComparison.Ordinal);
		Assert.Contains("_pendingAppearance =", stack, StringComparison.Ordinal);
		Assert.Contains("if (_pendingAppearance is { } appearance)", stack, StringComparison.Ordinal);
		Assert.Contains("UpdateFlyoutBackground(view.FlyoutBackground)", Read("TizenShellHandler.cs"), StringComparison.Ordinal);
	}
}
