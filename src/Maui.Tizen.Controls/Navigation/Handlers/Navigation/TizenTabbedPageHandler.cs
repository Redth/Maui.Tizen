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
	public class TizenTabbedPageHandler : TizenViewHandler<TabbedPage, NView>, ITabbedViewHandler
	{
		/// <summary>
		/// Property mapper for <see cref="TabbedPage"/>.
		/// </summary>
		public static IPropertyMapper<TabbedPage, TizenTabbedPageHandler> TabbedPageMapper =
			new PropertyMapper<TabbedPage, TizenTabbedPageHandler>(TizenViewMappers.ViewMapper)
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

				// Badge attached properties, added upstream by dotnet/maui#37755.
				//
				// The keys are string literals rather than nameof(TabbedPage.BadgeTextProperty)
				// on purpose. The compile-verification lane deliberately builds against the
				// repository's behaviourBaseline (MAUI 9.0.120), which predates these properties,
				// so a nameof would not compile there. The literals match
				// BindableProperty.CreateAttached("BadgeText"/"BadgeColor"/"BadgeTextColor", ...)
				// exactly. Switch them to nameof once the validation baseline carries the API.
				["BadgeText"] = MapBadgeText,
				["BadgeColor"] = MapBadgeColor,
				["BadgeTextColor"] = MapBadgeTextColor,
			};

		/// <summary>
		/// Command mapper for <see cref="TabbedPage"/> commands.
		/// </summary>
		public static CommandMapper<TabbedPage, TizenTabbedPageHandler> TabbedPageCommandMapper =
			new CommandMapper<TabbedPage, TizenTabbedPageHandler>(TizenViewMappers.ViewCommandMapper);

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

		ITabbedView ITabbedViewHandler.VirtualView => VirtualView;

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
		/// Unsupported: Tizen has no tab badge affordance.
		/// </summary>
		/// <remarks>
		/// Upstream (dotnet/maui#37755) added <c>BadgeText</c>, <c>BadgeColor</c> and
		/// <c>BadgeTextColor</c> as attached properties on <see cref="TabbedPage"/> and states that
		/// "Tizen exposes the shared API without a platform renderer, matching Shell's current
		/// support matrix". Tizen's NUI tab strip is a plain
		/// <c>Tizen.UIExtensions.NUI.CollectionView</c> with a text label and a selection bar; there
		/// is no badge decoration to drive.
		/// <para>
		/// The mapping is declared rather than omitted so that the gap is an explicit, reviewable
		/// classification in the parity artifact instead of a silent miss. Setting a badge on Tizen
		/// binds and raises property changes normally; nothing is drawn.
		/// </para>
		/// </remarks>
		public static void MapBadgeText(TizenTabbedPageHandler handler, TabbedPage view)
		{
		}

		/// <summary>
		/// Unsupported: Tizen has no tab badge affordance, so there is no badge to colour.
		/// </summary>
		/// <remarks>
		/// See <see cref="MapBadgeText"/> for the full rationale and the upstream reference.
		/// </remarks>
		public static void MapBadgeColor(TizenTabbedPageHandler handler, TabbedPage view)
		{
		}

		/// <summary>
		/// Unsupported: Tizen has no tab badge affordance, so there is no badge text to colour.
		/// </summary>
		/// <remarks>
		/// See <see cref="MapBadgeText"/> for the full rationale and the upstream reference.
		/// </remarks>
		public static void MapBadgeTextColor(TizenTabbedPageHandler handler, TabbedPage view)
		{
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
