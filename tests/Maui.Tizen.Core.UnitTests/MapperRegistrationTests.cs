using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class MapperRegistrationTests
	{
		[Theory]
		[InlineData(nameof(ILabel.Text))]
		[InlineData(nameof(ITextStyle.TextColor))]
		[InlineData(nameof(ITextStyle.Font))]
		[InlineData(nameof(ITextStyle.CharacterSpacing))]
		[InlineData(nameof(ITextAlignment.HorizontalTextAlignment))]
		[InlineData(nameof(ITextAlignment.VerticalTextAlignment))]
		[InlineData(nameof(ILabel.LineHeight))]
		[InlineData(nameof(ILabel.Padding))]
		[InlineData(nameof(ILabel.TextDecorations))]
		[InlineData(nameof(ILabel.Background))]
		[InlineData(nameof(ILabel.Opacity))]
		[InlineData(nameof(ILabel.Shadow))]
		public void LabelMapperDefinesTizenKey(string key) =>
			Assert.NotNull(TizenLabelHandler.Mapper.GetProperty(key));

		[Theory]
		[InlineData(nameof(IContentView.Content))]
		[InlineData(nameof(IContentView.Background))]
		public void ContentViewMapperDefinesTizenKey(string key) =>
			Assert.NotNull(TizenContentViewHandler.Mapper.GetProperty(key));

		[Theory]
		[InlineData(nameof(ILayout.Background))]
		[InlineData(nameof(ILayout.ClipsToBounds))]
		[InlineData(nameof(IView.InputTransparent))]
		public void LayoutMapperDefinesTizenKey(string key) =>
			Assert.NotNull(TizenLayoutHandler.Mapper.GetProperty(key));

		[Theory]
		[InlineData(nameof(IWindow.Title))]
		[InlineData(nameof(IWindow.Content))]
		[InlineData(nameof(IWindow.X))]
		[InlineData(nameof(IWindow.Y))]
		[InlineData(nameof(IWindow.Width))]
		[InlineData(nameof(IWindow.Height))]
		public void WindowMapperDefinesTizenKey(string key) =>
			Assert.NotNull(TizenWindowHandler.Mapper.GetProperty(key));

		[Theory]
		[InlineData(nameof(ITitledElement.Title))]
		[InlineData(nameof(IContentView.Background))]
		public void PageMapperOverridesContentViewKey(string key)
		{
			Assert.NotNull(TizenPageHandler.PageMapper.GetProperty(key));

			// The page mapper must shadow the content-view mapper, not merely chain to it.
			Assert.NotSame(
				TizenContentViewHandler.Mapper.GetProperty(key),
				TizenPageHandler.PageMapper.GetProperty(key));
		}

		[Fact]
		public void PageMapperChainsContentViewMapper()
		{
			var chained = ((Microsoft.Maui.PropertyMapper)TizenPageHandler.PageMapper).Chained;

			Assert.NotNull(chained);
			Assert.Contains(TizenContentViewHandler.Mapper, chained!);
		}

		[Fact]
		public void ViewMapperKeysAreInheritedThroughTheChain()
		{
			// Anything MAUI's ViewMapper defines must remain reachable from every handler mapper,
			// otherwise core view properties silently stop being applied.
			foreach (var key in Microsoft.Maui.Handlers.ViewHandler.ViewMapper.GetKeys())
			{
				Assert.NotNull(TizenLabelHandler.Mapper.GetProperty(key));
				Assert.NotNull(TizenLayoutHandler.Mapper.GetProperty(key));
				Assert.NotNull(TizenContentViewHandler.Mapper.GetProperty(key));
			}
		}

		[Theory]
		[InlineData(nameof(ITizenLayoutHandler.Add))]
		[InlineData(nameof(ITizenLayoutHandler.Remove))]
		[InlineData(nameof(ITizenLayoutHandler.Clear))]
		[InlineData(nameof(ITizenLayoutHandler.Insert))]
		[InlineData(nameof(ITizenLayoutHandler.Update))]
		[InlineData(nameof(ITizenLayoutHandler.UpdateZIndex))]
		public void LayoutCommandMapperDefinesKey(string key) =>
			Assert.NotNull(TizenLayoutHandler.CommandMapper.GetCommand(key));

		[Fact]
		public void LayoutCommandKeysMatchMauiControlsContract()
		{
			// MAUI Controls raises these by string via Handler.Invoke(nameof(ILayoutHandler.Add)).
			// If these names ever drift, layout children silently stop updating.
			var expected = new[] { "Add", "Remove", "Clear", "Insert", "Update", "UpdateZIndex" };

			foreach (var key in expected)
				Assert.NotNull(TizenLayoutHandler.CommandMapper.GetCommand(key));
		}

		[Theory]
		[InlineData(TizenApplicationHandler.TerminateCommandKey)]
		[InlineData(nameof(IApplication.OpenWindow))]
		[InlineData(nameof(IApplication.CloseWindow))]
		[InlineData(nameof(IApplication.ActivateWindow))]
		public void ApplicationCommandMapperDefinesKey(string key) =>
			Assert.NotNull(TizenApplicationHandler.CommandMapper.GetCommand(key));

		[Fact]
		public void WindowCommandMapperDefinesDisplayDensityRequest() =>
			Assert.NotNull(TizenWindowHandler.CommandMapper.GetCommand(nameof(IWindow.RequestDisplayDensity)));

		[Fact]
		public void HandlersUseTheirOwnMapperByDefault()
		{
			// Guards against a handler accidentally being constructed with MAUI's mapper.
			Assert.NotNull(new TizenLabelHandler());
			Assert.NotNull(new TizenLayoutHandler());
			Assert.NotNull(new TizenContentViewHandler());
			Assert.NotNull(new TizenPageHandler());
			Assert.NotNull(new TizenWindowHandler());
			Assert.NotNull(new TizenApplicationHandler());
		}

		[Fact]
		public void LabelMapperDoesNotDropAnyPortedKey()
		{
			// The exact Tizen key set from dotnet/maui's LabelHandler.Mapper.
			var portedKeys = new[]
			{
				nameof(ILabel.Background),
				nameof(ILabel.Opacity),
				nameof(ILabel.Shadow),
				nameof(ITextStyle.CharacterSpacing),
				nameof(ITextStyle.Font),
				nameof(ITextAlignment.HorizontalTextAlignment),
				nameof(ITextAlignment.VerticalTextAlignment),
				nameof(ILabel.LineHeight),
				nameof(ILabel.Padding),
				nameof(ILabel.Text),
				nameof(ITextStyle.TextColor),
				nameof(ILabel.TextDecorations),
			};

			var declared = TizenLabelHandler.Mapper.GetKeys().ToArray();

			foreach (var key in portedKeys)
				Assert.Contains(key, declared);
		}
	}
}
