using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCDataTemplateResolverTests
{
	sealed class Selector(DataTemplate? selected) : DataTemplateSelector
	{
		protected override DataTemplate? OnSelectTemplate(object item, BindableObject container) => selected;
	}

	[Fact]
	public void EmptyHeaderAndFooterSelectorsCreateFromTheSelectedConcreteTemplate()
	{
		var expected = new Label();
		var selector = new Selector(new DataTemplate(() => expected));

		var actual = selector.CreateViewFromTemplate("value", new CollectionView(), "test");

		Assert.Same(expected, actual);
	}

	[Fact]
	public void NullSelectorResultIsRejectedBeforeCreateContent()
	{
		var selector = new Selector(null);

		Assert.Throws<InvalidOperationException>(() =>
			selector.CreateViewFromTemplate("value", new CollectionView(), "test"));
	}

	[Fact]
	public void EmptyAndGlobalDecorationPresentersUseTheConcreteTemplateResolver()
	{
		var empty = File.ReadAllText(WaveCSource.Files.Single(
			path => Path.GetFileName(path) == "TizenEmptyItemAdaptor.cs"));
		var decorations = File.ReadAllText(WaveCSource.Files.Single(
			path => Path.GetFileName(path) == "TizenHeaderFooterPresenter.cs"));
		var ungrouped = File.ReadAllText(WaveCSource.Files.Single(
			path => Path.GetFileName(path) == "TizenItemTemplateAdaptor.cs"));

		Assert.Contains("CreateViewFromTemplate", empty, StringComparison.Ordinal);
		Assert.Contains("CreateViewFromTemplate", decorations, StringComparison.Ordinal);
		Assert.Equal(2, ungrouped.Split("CreateViewFromTemplate").Length - 1);
		Assert.DoesNotContain("EmptyViewTemplate.CreateContent", empty, StringComparison.Ordinal);
		Assert.DoesNotContain("template?.CreateContent", decorations, StringComparison.Ordinal);
	}
}
