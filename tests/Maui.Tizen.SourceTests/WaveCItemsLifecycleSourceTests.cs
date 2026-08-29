namespace Maui.Tizen.SourceTests;

public class WaveCItemsLifecycleSourceTests
{
	static string Read(string fileName) =>
		File.ReadAllText(WaveCSource.Files.Single(path => Path.GetFileName(path) == fileName));

	[Fact]
	public void EmptyViewChangesUseTheOwnedAdaptorTransition()
	{
		var source = Read("TizenItemsViewHandler.cs");
		var start = source.IndexOf("protected virtual void UpdateEmptyView()", StringComparison.Ordinal);
		var end = source.IndexOf("protected virtual void OnAdaptorSelectionChanged", start, StringComparison.Ordinal);
		var body = source[start..end];

		Assert.Contains("TransitionToEmptyAdaptor();", body, StringComparison.Ordinal);
		Assert.DoesNotContain("collectionView.Adaptor = new", body, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("TizenItemTemplateAdaptor.cs")]
	[InlineData("TizenGroupItemTemplateAdaptor.cs")]
	public void TemporaryMeasurementCreatesThenDisposesTheRealHandler(string fileName)
	{
		var source = Read(fileName);
		var measure = source.IndexOf(
			"public override TSize MeasureItem(int index",
			StringComparison.Ordinal);
		var create = source.IndexOf("ToPlatformView(MauiContext)", measure, StringComparison.Ordinal);
		var dispose = source.IndexOf("(view.Handler as IDisposable)?.Dispose();", create, StringComparison.Ordinal);

		Assert.True(measure >= 0 && create > measure && dispose > create);
	}

	[Theory]
	[InlineData("TizenItemTemplateAdaptor.cs")]
	[InlineData("TizenGroupItemTemplateAdaptor.cs")]
	[InlineData("TizenEmptyItemAdaptor.cs")]
	public void RemovedRowsDropTheirNativeRegistrationBeforeDisposal(string fileName)
	{
		var source = Read(fileName);
		var remove = source.IndexOf("RemoveNativeView", StringComparison.Ordinal);
		var body = source[remove..];

		Assert.True(
			body.Contains("UnregisterNativeView(native)", StringComparison.Ordinal)
				|| body.Contains("Remove(native, out", StringComparison.Ordinal));
		Assert.Contains("(view.Handler as IDisposable)?.Dispose();", body, StringComparison.Ordinal);
	}

	[Fact]
	public void GroupedAndEmptyAdaptorsShareExplicitHeaderFooterOwnership()
	{
		Assert.Contains("TizenHeaderFooterPresenter", Read("TizenGroupItemTemplateAdaptor.cs"), StringComparison.Ordinal);
		Assert.Contains("TizenHeaderFooterPresenter", Read("TizenEmptyItemAdaptor.cs"), StringComparison.Ordinal);
	}

	[Fact]
	public void ReusedItemsPlatformViewsAreReboundBeforeMapperUpdates()
	{
		Assert.Contains("platformView.Rebind(itemsView)", Read("TizenItemsViewHandler.cs"), StringComparison.Ordinal);
		Assert.Contains("public virtual void Rebind", Read("TizenCollectionViewControl.cs"), StringComparison.Ordinal);
	}

	[Fact]
	public void NativeScrollAndOrientationAwareScrollbarsAreWired()
	{
		var source = Read("TizenItemsViewHandler.cs");

		Assert.Contains("collectionView.Scrolled += OnCollectionViewScrolled", source, StringComparison.Ordinal);
		Assert.Contains("collectionView.Scrolled -= OnCollectionViewScrolled", source, StringComparison.Ordinal);
		Assert.Contains("LayoutManager.IsHorizontal", source, StringComparison.Ordinal);
		Assert.Contains("SendRemainingItemsThresholdReached", source, StringComparison.Ordinal);
	}

	[Fact]
	public void CarouselUsesNativeFeedbackAndSwipeCapability()
	{
		var source = Read("TizenCarouselViewHandler.cs");

		Assert.Contains("carousel.Scrolled += OnCarouselScrolled", source, StringComparison.Ordinal);
		Assert.Contains("CarouselFeedbackCoordinator", source, StringComparison.Ordinal);
		Assert.Contains("ScrollEnabled = VirtualView.IsSwipeEnabled", source, StringComparison.Ordinal);
	}

	[Fact]
	public void EveryAdaptorInstallationReappliesManagedSelection()
	{
		var items = Read("TizenItemsViewHandler.cs");
		var selectable = Read("TizenSelectableItemsViewHandler.cs");

		Assert.Contains("OnAdaptorInstalled();", items, StringComparison.Ordinal);
		Assert.Contains("UpdateSelectedItem();", selectable, StringComparison.Ordinal);
		Assert.Contains("UpdateSelectedItems();", selectable, StringComparison.Ordinal);
	}

	[Fact]
	public void DisconnectUsesTheCapturedPlatformViewAndSkipsInstallSynchronization()
	{
		var source = Read("TizenItemsViewHandler.cs");
		var start = source.IndexOf("protected override void DisconnectHandler", StringComparison.Ordinal);
		var end = source.IndexOf("public override void SetVirtualView", start, StringComparison.Ordinal);
		var body = source[start..end];

		Assert.Contains("platformView as TizenItemsViewControl", body, StringComparison.Ordinal);
		Assert.Contains("notifyInstalled: false", body, StringComparison.Ordinal);
		Assert.DoesNotContain("NativeCollectionView", body, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("TizenShellFlyoutItemAdaptor.cs")]
	[InlineData("TizenShellSectionItemAdaptor.cs")]
	[InlineData("TizenShellContentItemAdaptor.cs")]
	public void ShellAdaptorCategoryAndMeasurementShareTheRealRowFactory(string fileName)
	{
		var source = Read(fileName);

		Assert.Contains("GetViewCategory", source, StringComparison.Ordinal);
		Assert.Contains("CreateItemView", source, StringComparison.Ordinal);
		Assert.DoesNotContain("override NView CreateNativeView", source, StringComparison.Ordinal);
		if (fileName == "TizenShellFlyoutItemAdaptor.cs")
			Assert.Contains("return typeof(TizenShellFlyoutItemView);", source, StringComparison.Ordinal);
	}

	[Fact]
	public void RuntimeItemsLayoutChangesRebuildLayoutAndSnapSettings()
	{
		var collection = Read("TizenCollectionViewControl.cs");
		var carousel = Read("TizenCarouselViewControl.cs");

		Assert.Contains("PropertyChanged += OnItemsLayoutPropertyChanged", collection, StringComparison.Ordinal);
		Assert.Contains("SnapPointsType", collection, StringComparison.Ordinal);
		Assert.Contains(
			"CollectionView.SnapPointsAlignment = (TSnapPointsAlignment)layout.SnapPointsAlignment;",
			collection,
			StringComparison.Ordinal);
		Assert.Contains("PropertyChanged -= OnItemsLayoutPropertyChanged", collection, StringComparison.Ordinal);
		Assert.Contains("PropertyChanged += OnItemsLayoutPropertyChanged", carousel, StringComparison.Ordinal);
		Assert.Contains("SnapPointsAlignment", carousel, StringComparison.Ordinal);
		Assert.Contains("Relayout += OnRelayout", carousel, StringComparison.Ordinal);
	}
}
