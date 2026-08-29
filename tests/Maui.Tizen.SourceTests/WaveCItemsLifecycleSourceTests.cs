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
}
