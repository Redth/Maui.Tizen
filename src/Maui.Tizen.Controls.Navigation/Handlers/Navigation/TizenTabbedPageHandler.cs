using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Platform;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Handler for <see cref="TabbedPage"/> in the Tizen backend.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Provides tab-based navigation with a tab bar at the top displaying all child pages.
	/// </para>
	/// <para>
	/// No-op properties (bar styling has limited platform support):
	/// - BarBackground
	/// - BarBackgroundColor
	/// - BarTextColor
	/// - UnselectedTabColor
	/// - SelectedTabColor
	/// - ItemsSource
	/// - ItemTemplate
	/// - SelectedItem
	/// </para>
	/// </remarks>
	public class TizenTabbedPageHandler : ViewHandler<TabbedPage, NView>
	{
		/// <summary>
		/// Property mapper for <see cref="TabbedPage"/>.
		/// </summary>
		public static IPropertyMapper<TabbedPage, TizenTabbedPageHandler> TabbedPageMapper =
			new PropertyMapper<TabbedPage, TizenTabbedPageHandler>(ViewMapper)
			{
				[nameof(TabbedPage.BarBackground)] = MapBarBackground,
				[nameof(TabbedPage.BarBackgroundColor)] = MapBarBackgroundColor,
				[nameof(TabbedPage.BarTextColor)] = MapBarTextColor,
				[nameof(TabbedPage.UnselectedTabColor)] = MapUnselectedTabColor,
				[nameof(TabbedPage.SelectedTabColor)] = MapSelectedTabColor,
				[nameof(TabbedPage.ItemsSource)] = MapItemsSource,
				[nameof(TabbedPage.ItemTemplate)] = MapItemTemplate,
				[nameof(TabbedPage.SelectedItem)] = MapSelectedItem,
				[nameof(TabbedPage.CurrentPage)] = MapCurrentPage,
			};

		/// <summary>
		/// Command mapper for <see cref="TabbedPage"/> commands.
		/// </summary>
		public static CommandMapper<TabbedPage, TizenTabbedPageHandler> TabbedPageCommandMapper =
			new CommandMapper<TabbedPage, TizenTabbedPageHandler>(ViewCommandMapper);

		/// <summary>
		/// Initializes a new instance of <see cref="TizenTabbedPageHandler"/> using default mappers.
		/// </summary>
		public TizenTabbedPageHandler()
			: base(TabbedPageMapper, TabbedPageCommandMapper)
		{
		}

		/// <summary>
		/// Initializes a new instance of <see cref="TizenTabbedPageHandler"/> with custom mappers.
		/// </summary>
		/// <param name="mapper">The property mapper.</param>
		/// <param name="commandMapper">Optional command mapper.</param>
		public TizenTabbedPageHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper)
		{
		}

		/// <summary>
		/// Gets the typed platform view.
		/// </summary>
		protected new TizenTabbedPageView? PlatformView => base.PlatformView as TizenTabbedPageView;

		/// <inheritdoc/>
		protected override NView CreatePlatformView()
		{
			return new TizenTabbedPageView(VirtualView);
		}

		/// <inheritdoc/>
		protected override void DisconnectHandler(NView platformView)
		{
			PlatformView?.DisconnectHandler();
			base.DisconnectHandler(platformView);
		}

		#region Mapper Methods

		/// <summary>
		/// No-op: BarBackground styling is handled via bindings in the TabbedItem.
		/// </summary>
		/// <remarks>
		/// The BarBackground is bound directly to the tab items via XAML bindings.
		/// This mapper exists for API completeness but performs no additional operation.
		/// </remarks>
		public static void MapBarBackground(TizenTabbedPageHandler handler, TabbedPage view)
		{
			// Handled via bindings in TizenTabbedItem
		}

		/// <summary>
		/// No-op: BarBackgroundColor styling is handled via bindings in the TabbedItem.
		/// </summary>
		/// <remarks>
		/// The BarBackgroundColor is bound directly to the tab items via XAML bindings.
		/// This mapper exists for API completeness but performs no additional operation.
		/// </remarks>
		public static void MapBarBackgroundColor(TizenTabbedPageHandler handler, TabbedPage view)
		{
			// Handled via bindings in TizenTabbedItem
		}

		/// <summary>
		/// No-op: BarTextColor styling is handled via bindings in the TabbedItem.
		/// </summary>
		/// <remarks>
		/// The BarTextColor is bound directly to the tab items via XAML bindings.
		/// This mapper exists for API completeness but performs no additional operation.
		/// </remarks>
		public static void MapBarTextColor(TizenTabbedPageHandler handler, TabbedPage view)
		{
			// Handled via bindings in TizenTabbedItem
		}

		/// <summary>
		/// No-op: UnselectedTabColor styling is handled via bindings in the TabbedItem.
		/// </summary>
		/// <remarks>
		/// The UnselectedTabColor is bound directly to the tab items via XAML bindings.
		/// This mapper exists for API completeness but performs no additional operation.
		/// </remarks>
		public static void MapUnselectedTabColor(TizenTabbedPageHandler handler, TabbedPage view)
		{
			// Handled via bindings in TizenTabbedItem
		}

		/// <summary>
		/// No-op: SelectedTabColor styling is handled via bindings in the TabbedItem.
		/// </summary>
		/// <remarks>
		/// The SelectedTabColor is bound directly to the tab items via XAML bindings.
		/// This mapper exists for API completeness but performs no additional operation.
		/// </remarks>
		public static void MapSelectedTabColor(TizenTabbedPageHandler handler, TabbedPage view)
		{
			// Handled via bindings in TizenTabbedItem
		}

		/// <summary>
		/// No-op: ItemsSource is managed through Children collection.
		/// </summary>
		/// <remarks>
		/// TabbedPage uses the Children collection directly rather than ItemsSource.
		/// This mapper exists for API completeness but performs no operation.
		/// </remarks>
		public static void MapItemsSource(TizenTabbedPageHandler handler, TabbedPage view)
		{
			// TabbedPage uses Children, not ItemsSource
		}

		/// <summary>
		/// No-op: ItemTemplate is not used by TabbedPage on Tizen.
		/// </summary>
		/// <remarks>
		/// TabbedPage uses a fixed template for tab items.
		/// This mapper exists for API completeness but performs no operation.
		/// </remarks>
		public static void MapItemTemplate(TizenTabbedPageHandler handler, TabbedPage view)
		{
			// Fixed template, not configurable
		}

		/// <summary>
		/// No-op: SelectedItem is managed through CurrentPage.
		/// </summary>
		/// <remarks>
		/// TabbedPage uses CurrentPage rather than SelectedItem.
		/// This mapper exists for API completeness but performs no operation.
		/// </remarks>
		public static void MapSelectedItem(TizenTabbedPageHandler handler, TabbedPage view)
		{
			// Selection is managed through CurrentPage
		}

		/// <summary>
		/// Maps <see cref="TabbedPage.CurrentPage"/> to update the displayed content.
		/// </summary>
		public static void MapCurrentPage(TizenTabbedPageHandler handler, TabbedPage view)
		{
			handler.PlatformView?.UpdateCurrentPage();
		}

		#endregion
	}
}
