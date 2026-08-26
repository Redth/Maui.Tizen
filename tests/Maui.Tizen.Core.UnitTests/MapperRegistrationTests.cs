using System;
using System.Collections.Generic;
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
		public void HandlerMappersChainTheTizenBaseMapperNotMauis()
		{
			// MAUI's neutral ViewMapper is compiled with PlatformView aliased to System.Object and
			// calls the Standard no-op extensions, so chaining it reports every key as "present"
			// while doing nothing at all. Every handler must chain the Tizen-owned base instead.
			foreach (var key in TizenViewMappers.ViewMapper.GetKeys())
			{
				Assert.NotNull(TizenLabelHandler.Mapper.GetProperty(key));
				Assert.NotNull(TizenLayoutHandler.Mapper.GetProperty(key));
				Assert.NotNull(TizenContentViewHandler.Mapper.GetProperty(key));
				Assert.NotNull(TizenPageHandler.PageMapper.GetProperty(key));
			}
		}

		[Fact]
		public void TizenBaseMapperCoversMauisViewMapperExceptDocumentedExclusions()
		{
			// Guards against quietly losing a core IView property when re-basing off MAUI's mapper.
			// The exclusions are deliberate and each has a recorded reason.
			// Force MAUI Controls to run RemapForControls before comparing. It mutates the STATIC
			// ViewHandler.ViewMapper at runtime, so without this the comparison silently depends on
			// whether some other test happened to touch Controls first - which is exactly how this
			// test passed locally and failed in CI.
			_ = new Microsoft.Maui.Controls.Label();

			var excluded = new HashSet<string>(StringComparer.Ordinal)
			{
				// Both require a container view, which an out-of-repo backend cannot construct -
				// ViewHandler.ContainerView has a private protected setter. See G1. These are the
				// ONLY exclusions; anything else must be genuinely reachable.
				"ContainerView",
				"Border",
			};

			var covered = new HashSet<string>(TizenViewMappers.ViewMapper.GetKeys(), StringComparer.Ordinal);

			var missing = Microsoft.Maui.Handlers.ViewHandler.ViewMapper.GetKeys()
				.Where(k => !covered.Contains(k) && !excluded.Contains(k))
				.ToArray();

			Assert.Empty(missing);
		}

		[Fact]
		public void TizenBaseCommandMapperCoversTheCoreViewCommands()
		{
			foreach (var key in new[]
			{
				nameof(IView.InvalidateMeasure),
				nameof(IView.Frame),
				nameof(IView.Focus),
				nameof(IView.Unfocus),
			})
			{
				Assert.NotNull(TizenViewMappers.ViewCommandMapper.GetCommand(key));
			}
		}

		[Theory]
		[InlineData(nameof(ILayoutHandler.Add))]
		[InlineData(nameof(ILayoutHandler.Remove))]
		[InlineData(nameof(ILayoutHandler.Clear))]
		[InlineData(nameof(ILayoutHandler.Insert))]
		[InlineData(nameof(ILayoutHandler.Update))]
		[InlineData(nameof(ILayoutHandler.UpdateZIndex))]
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
		public void HandlerMappersAreNotMauisOwn()
		{
			// This backend must not reuse MAUI's static mappers: theirs dispatch to MAUI's Tizen
			// Map* implementations, which live in an assembly this package deliberately does not
			// depend on.
			Assert.NotSame(Microsoft.Maui.Handlers.LabelHandler.Mapper, TizenLabelHandler.Mapper);
			Assert.NotSame(Microsoft.Maui.Handlers.LayoutHandler.Mapper, TizenLayoutHandler.Mapper);
			Assert.NotSame(Microsoft.Maui.Handlers.ContentViewHandler.Mapper, TizenContentViewHandler.Mapper);
			Assert.NotSame(Microsoft.Maui.Handlers.PageHandler.Mapper, TizenPageHandler.PageMapper);
			Assert.NotSame(Microsoft.Maui.Handlers.WindowHandler.Mapper, TizenWindowHandler.Mapper);
			Assert.NotSame(Microsoft.Maui.Handlers.ApplicationHandler.Mapper, TizenApplicationHandler.Mapper);

			Assert.NotSame(Microsoft.Maui.Handlers.LayoutHandler.CommandMapper, TizenLayoutHandler.CommandMapper);
			Assert.NotSame(Microsoft.Maui.Handlers.ApplicationHandler.CommandMapper, TizenApplicationHandler.CommandMapper);
		}

		[Fact]
		public void DefaultConstructedHandlerDispatchesThroughThisBackendsMapper()
		{
			// A real regression guard, not a smoke test.
			//
			// MAUI's LabelHandler.Mapper is a PropertyMapper<ILabel, ILabelHandler>, so dispatching
			// through it casts the handler to MAUI's ILabelHandler. This backend deliberately does
			// NOT implement that interface (it binds PlatformView to a per-TFM alias - see
			// docs/net11-status.md G2), so if the parameterless constructor were ever changed to
			// pass MAUI's mapper, this throws InvalidCastException.
			//
			// The virtual view matters: PropertyMapper.UpdateProperty short-circuits on a null
			// virtual view, which would make this pass vacuously.
			var handler = new TizenLabelHandler();
			handler.SetVirtualView(new StubLabel { Text = "hello" });

			var exception = Record.Exception(() => handler.UpdateValue(nameof(ILabel.Text)));

			Assert.Null(exception);
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
