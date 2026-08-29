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

		Assert.Contains("PlatformView.SetToolbar", source, StringComparison.Ordinal);
		Assert.Contains("platformView.ClearToolbar()", source, StringComparison.Ordinal);
	}

	[Fact]
	public void ShellSearchViewOwnsQueryResultsAndCommandSubscriptions()
	{
		var source = Read("TizenShellSearchView.cs");

		Assert.Contains("TizenSearchBarView", source, StringComparison.Ordinal);
		Assert.Contains("SearchButtonPressed += OnSearchButtonPressed", source, StringComparison.Ordinal);
		Assert.Contains("ListProxyChanged += OnListProxyChanged", source, StringComparison.Ordinal);
		Assert.Contains("PropertyChanged += OnSearchHandlerPropertyChanged", source, StringComparison.Ordinal);
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
		Assert.DoesNotContain("ResolveFlyoutItemTemplate", adaptor[..adaptor.IndexOf("CreateNativeView", StringComparison.Ordinal)], StringComparison.Ordinal);
	}

	[Fact]
	public void ClearingCustomFlyoutContentRestoresTheDefaultCollection()
	{
		var source = Read("TizenShellView.cs");
		var start = source.IndexOf("public void UpdateFlyoutContent()", StringComparison.Ordinal);
		var end = source.IndexOf("public void UpdateToolbar()", start, StringComparison.Ordinal);
		var body = source[start..end];

		Assert.Contains("_customFlyoutContent.Handler", body, StringComparison.Ordinal);
		Assert.Contains("UpdateFlyoutItems(Shell)", body, StringComparison.Ordinal);
	}

	[Fact]
	public void TabbedPageDoesNotDuplicateMultiPageAppearanceOrCreateHandlersDuringDisconnect()
	{
		var source = Read("TizenTabbedPageView.cs");

		Assert.DoesNotContain("SendAppearing", source, StringComparison.Ordinal);
		Assert.DoesNotContain("SendDisappearing", source, StringComparison.Ordinal);
		var disconnect = source[source.IndexOf("public void DisconnectHandler()", StringComparison.Ordinal)..];
		Assert.DoesNotContain("ToHandler(", disconnect, StringComparison.Ordinal);
		Assert.Contains("var handler = child.Handler", disconnect, StringComparison.Ordinal);
	}
}
