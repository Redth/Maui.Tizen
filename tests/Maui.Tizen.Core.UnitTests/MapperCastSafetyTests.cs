using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Drives every mapper key that is reachable from a Tizen handler and asserts none of them
	/// throws <see cref="InvalidCastException"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Wave A raised that MAUI's mapper fields, though declared against handler INTERFACES, could
	/// be instantiated as <c>PropertyMapper&lt;IView, ConcreteHandler&gt;</c>, in which case an
	/// inherited chained key would hard-cast a Tizen handler and throw at runtime.
	/// </para>
	/// <para>
	/// Reflecting over the live package settles the instantiation question: on 11.0.0-preview.7
	/// every one of LabelHandler.Mapper, ViewHandler.ViewMapper, ContentViewHandler.Mapper,
	/// LayoutHandler.Mapper and WindowHandler.Mapper is instantiated with the INTERFACE handler
	/// argument, both before and after Controls' RemapForControls has run. So that specific
	/// mechanism does not currently apply.
	/// </para>
	/// <para>
	/// A mapper's generic argument is not the only way to get a bad cast, though - an individual
	/// entry's delegate body can cast to a concrete handler regardless of how the mapper is typed.
	/// Rather than reason about which of those is true in any given package version, these tests
	/// simply invoke the keys and see. That stays honest across upgrades, which reflection over
	/// today's types would not.
	/// </para>
	/// <para>
	/// Controls is initialised through a real host before anything is measured, because
	/// RemapForControls mutates the STATIC mappers: constructing a handler is not enough to pull
	/// those entries in, which is why earlier parity tests missed them.
	/// </para>
	/// </remarks>
	[Collection(StaticMapperCollection.Name)]
	public class MapperCastSafetyTests
	{
		class ControlsApp : Microsoft.Maui.Controls.Application
		{
			protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
				new(new ContentPage());
		}

		static MauiApp BuildControlsApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<ControlsApp>();
			builder.ConfigureTizen();
			builder.ConfigureMauiHandlers(handlers =>
			{
				handlers.AddHandler<Label, TizenLabelHandler>();
				handlers.AddHandler<ContentPage, TizenPageHandler>();
				handlers.AddHandler<Microsoft.Maui.Controls.Layout, TizenLayoutHandler>();
			});

			return builder.Build();
		}

		/// <summary>
		/// Forces Controls' static remapping to run, so the mappers under test are the ones a real
		/// app sees rather than the bare backend ones.
		/// </summary>
		static void ForceControlsRemap()
		{
			_ = new Label();
			_ = new ContentPage();
			_ = new Grid();
			_ = new VerticalStackLayout();
		}

		static IEnumerable<string> KeysOf(IPropertyMapper mapper) => mapper.GetKeys();

		[Fact]
		public void EveryReachableLabelPropertyKeyToleratesATizenHandler()
		{
			ForceControlsRemap();
			using var app = BuildControlsApp();

			var handler = new TizenLabelHandler();
			var label = new Label { Text = "cast safety" };

			AssertNoBadCast(TizenLabelHandler.Mapper, handler, label);
		}

		[Fact]
		public void EveryReachableViewPropertyKeyToleratesATizenHandler()
		{
			ForceControlsRemap();
			using var app = BuildControlsApp();

			var handler = new TizenLabelHandler();
			var label = new Label { Text = "cast safety" };

			AssertNoBadCast(TizenViewMappers.ViewMapper, handler, label);
		}

		[Fact]
		public void EveryReachableLayoutPropertyKeyToleratesATizenHandler()
		{
			ForceControlsRemap();
			using var app = BuildControlsApp();

			var handler = new TizenLayoutHandler();
			var layout = new VerticalStackLayout();

			AssertNoBadCast(TizenLayoutHandler.Mapper, handler, layout);
		}

		/// <summary>
		/// Invokes every key and fails only on <see cref="InvalidCastException"/>.
		/// </summary>
		/// <remarks>
		/// Other exceptions are expected and ignored on purpose: most of these mappings reach for a
		/// native NUI view that cannot exist on the host, so they throw NullReference or
		/// PlatformNotSupported. Those say nothing about cast safety, which is the single question
		/// being asked here. A test that demanded every mapping succeed would be a device test.
		/// </remarks>
		static void AssertNoBadCast(IPropertyMapper mapper, IElementHandler handler, IElement view)
		{
			var offenders = new List<string>();

			foreach (var key in KeysOf(mapper))
			{
				try
				{
					mapper.UpdateProperty(handler, view, key);
				}
				catch (InvalidCastException e)
				{
					offenders.Add($"{key}: {e.Message}");
				}
				catch
				{
					// Not a cast problem; see the remarks.
				}
			}

			Assert.Empty(offenders);
		}

		[Fact]
		public void ControlsRemapContributesKeysToTheMappersUnderTest()
		{
			// Guards the guard. If RemapForControls stopped being triggered, or the backend stopped
			// chaining the static mapper, the tests above would still pass - by driving a much
			// smaller set of keys than a real app does. This pins that the Controls-only keys are
			// genuinely present.
			ForceControlsRemap();
			using var app = BuildControlsApp();

			var keys = KeysOf(TizenViewMappers.ViewMapper).ToArray();

			Assert.Contains(nameof(IView.Background), keys);
			Assert.Contains("BackgroundColor", keys);
			Assert.Contains("IsInAccessibleTree", keys);
		}
	}
}
