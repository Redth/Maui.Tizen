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
		Assert.Contains("AnimatePositionChanges", source, StringComparison.Ordinal);
		Assert.Contains("AnimateCurrentItemChanges", source, StringComparison.Ordinal);

		var control = Read("TizenCarouselViewControl.cs");
		Assert.Contains("CarouselVisualState.ForIndex", control, StringComparison.Ordinal);
		Assert.Contains("Element.VisibleViews.Add", control, StringComparison.Ordinal);
		Assert.Contains("Element.SetIsDragging", control, StringComparison.Ordinal);
		Assert.Contains("ScrollAnimationEnded", control, StringComparison.Ordinal);
		Assert.Contains("PrepareForAdaptorReplacement", control, StringComparison.Ordinal);
		Assert.Contains("RefreshVisibleViews(index)", control, StringComparison.Ordinal);
		Assert.Contains("Scrolled?.Invoke(this, currentIndex)", control, StringComparison.Ordinal);
	}

	[Fact]
	public void EveryAdaptorInstallationReappliesManagedSelection()
	{
		var items = Read("TizenItemsViewHandler.cs");
		var selectable = Read("TizenSelectableItemsViewHandler.cs");

		Assert.Contains("OnAdaptorInstalled();", items, StringComparison.Ordinal);
		Assert.Contains("newAdaptor is not null", items, StringComparison.Ordinal);
		Assert.Contains("UpdateSelectedItem();", selectable, StringComparison.Ordinal);
		Assert.Contains("UpdateSelectedItems();", selectable, StringComparison.Ordinal);
	}

	[Fact]
	public void AdaptorAndSelectionModeConfigurationSuppressNativeFeedback()
	{
		var items = Read("TizenItemsViewHandler.cs");
		var selectable = Read("TizenSelectableItemsViewHandler.cs");

		Assert.Contains("ConfigureNativeAdaptor(() =>", items, StringComparison.Ordinal);
		Assert.Contains("_selection.SuppressNativeFeedback(configure)", selectable, StringComparison.Ordinal);
		Assert.Contains("_selection.SuppressNativeFeedback(() => PlatformView?.UpdateSelectionMode())", selectable, StringComparison.Ordinal);
		Assert.Contains("if (_selection.IsPushingToNative", selectable, StringComparison.Ordinal);
	}

	[Fact]
	public void PlatformViewRebindOnlyChangesItsElementBeforeMappersRun()
	{
		var collection = Read("TizenCollectionViewControl.cs");
		var carousel = Read("TizenCarouselViewControl.cs");
		var structuredStart = collection.IndexOf("public override void Rebind(TItemsView element)", StringComparison.Ordinal);
		var structuredEnd = collection.IndexOf("protected override void Dispose", structuredStart, StringComparison.Ordinal);
		var selectableClass = collection.IndexOf("public class TizenSelectableItemsViewControl", StringComparison.Ordinal);
		var selectableStart = collection.IndexOf("public override void Rebind(TItemsView element)", selectableClass, StringComparison.Ordinal);
		var selectableEnd = collection.IndexOf("}", selectableStart, StringComparison.Ordinal);
		var carouselStart = carousel.IndexOf("public override void Rebind(CarouselView element)", StringComparison.Ordinal);
		var carouselEnd = carousel.IndexOf("public event EventHandler<int>", carouselStart, StringComparison.Ordinal);

		Assert.DoesNotContain("UpdateLayoutManager", collection[structuredStart..structuredEnd], StringComparison.Ordinal);
		Assert.DoesNotContain("UpdateSelectionMode", collection[selectableStart..selectableEnd], StringComparison.Ordinal);
		Assert.DoesNotContain("UpdateLayoutManager", carousel[carouselStart..carouselEnd], StringComparison.Ordinal);
		Assert.True(
			carousel[carouselStart..carouselEnd].IndexOf("base.Rebind(element)", StringComparison.Ordinal)
				< carousel[carouselStart..carouselEnd].IndexOf("ClearVisibleViews(previous)", StringComparison.Ordinal));
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
		var detach = body.IndexOf("SetAdaptorCore(collectionView, null", StringComparison.Ordinal);
		var unsubscribe = body.IndexOf("UnsubscribeFromCollectionChanges", StringComparison.Ordinal);
		var baseCleanup = body.IndexOf("base.DisconnectHandler(platformView)", StringComparison.Ordinal);
		Assert.True(detach >= 0 && unsubscribe > detach && baseCleanup > unsubscribe);
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

		var shared = Read("TizenItemTemplateAdaptor.cs");
		Assert.Contains("var view = CreateItemView(index);", shared, StringComparison.Ordinal);
	}

	[Fact]
	public void RuntimeItemsLayoutChangesRebuildLayoutAndSnapSettings()
	{
		var collection = Read("TizenCollectionViewControl.cs");
		var carousel = Read("TizenCarouselViewControl.cs");

		Assert.Contains("PropertyChanged += OnItemsLayoutPropertyChanged", collection, StringComparison.Ordinal);
		Assert.Contains("SnapPointsType", collection, StringComparison.Ordinal);
		Assert.Contains(
			"CollectionView.SnapPointsAlignment = (TSnapPointsAlignment)state.SnapPointsAlignment;",
			collection,
			StringComparison.Ordinal);
		Assert.Contains("ItemsLayoutSnapshot.Capture(itemsLayout)", collection, StringComparison.Ordinal);
		Assert.Contains("PropertyChanged -= OnItemsLayoutPropertyChanged", collection, StringComparison.Ordinal);
		Assert.Contains("PropertyChanged += OnItemsLayoutPropertyChanged", carousel, StringComparison.Ordinal);
		Assert.Contains("SnapPointsAlignment", carousel, StringComparison.Ordinal);
		Assert.Contains("Relayout += OnRelayout", carousel, StringComparison.Ordinal);
	}

	[Fact]
	public void ItemsControlUsesTheProductionMeasurementCoordinator()
	{
		var source = Read("TizenCollectionViewControl.cs");

		Assert.Contains(": NView, IMeasurable", source, StringComparison.Ordinal);
		Assert.Contains("ItemsViewMeasure.Resolve", source, StringComparison.Ordinal);
		Assert.Contains("GetScrollCanvasSize()", source, StringComparison.Ordinal);
	}

	[Fact]
	public void CarouselEmptyAdaptorKeepsItsPlaceholderInternal()
	{
		var empty = Read("TizenEmptyItemAdaptor.cs");
		var carousel = Read("TizenCarouselViewHandler.cs");

		Assert.Contains("ITizenLogicalItemAdaptor", empty, StringComparison.Ordinal);
		Assert.Contains("isInternalPlaceholder: true", empty, StringComparison.Ordinal);
		Assert.Contains("SetLogicalItemCount(LogicalItemCount)", carousel, StringComparison.Ordinal);
		Assert.Contains("LogicalItemCount", carousel, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("TizenItemTemplateAdaptor.cs")]
	[InlineData("TizenGroupItemTemplateAdaptor.cs")]
	public void RealizedRowsAreIndexedByHolderAndAbsolutePosition(string fileName)
	{
		var source = Read(fileName);

		Assert.Contains("RealizedRowIndexMap<NView, View>", source, StringComparison.Ordinal);
		Assert.DoesNotContain("Dictionary<object, View?>", source, StringComparison.Ordinal);
		Assert.Contains("_realizedRows.Unbind(native)", source, StringComparison.Ordinal);
	}

	[Fact]
	public void UngroupedAdaptorUsesTheObservableNormalizationBoundary()
	{
		var source = Read("TizenItemTemplateAdaptor.cs");

		Assert.Contains("new TizenObservableItemSource(itemsView.ItemsSource)", source, StringComparison.Ordinal);
		Assert.Contains("_ownedItemsSource?.Dispose()", source, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("TizenItemTemplateAdaptor.cs")]
	[InlineData("TizenGroupItemTemplateAdaptor.cs")]
	public void RowMeasurementReturnsTheRealMeasuredViewSize(string fileName)
	{
		var source = Read(fileName);

		Assert.Contains(
			"return ((IView)view).Measure(widthConstraint, heightConstraint).ToPixel();",
			source,
			StringComparison.Ordinal);
	}
}
