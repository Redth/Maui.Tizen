using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Platform;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Handler for <see cref="StructuredItemsView"/> in the Tizen backend.
	/// </summary>
	/// <typeparam name="TItemsView">The MAUI StructuredItemsView type.</typeparam>
	/// <remarks>
	/// Adds support for Header, Footer, HeaderTemplate, FooterTemplate, and ItemsLayout properties.
	/// </remarks>
	public abstract class TizenStructuredItemsViewHandler<TItemsView> : TizenItemsViewHandler<TItemsView>
		where TItemsView : StructuredItemsView
	{
		/// <summary>
		/// Property mapper for <see cref="StructuredItemsView"/> properties.
		/// </summary>
		public static IPropertyMapper<TItemsView, TizenStructuredItemsViewHandler<TItemsView>> StructuredItemsViewMapper =
			new PropertyMapper<TItemsView, TizenStructuredItemsViewHandler<TItemsView>>(ItemsViewMapper)
			{
				[nameof(StructuredItemsView.Header)] = MapHeader,
				[nameof(StructuredItemsView.HeaderTemplate)] = MapHeaderTemplate,
				[nameof(StructuredItemsView.Footer)] = MapFooter,
				[nameof(StructuredItemsView.FooterTemplate)] = MapFooterTemplate,
				[nameof(StructuredItemsView.ItemsLayout)] = MapItemsLayout,
				[nameof(StructuredItemsView.ItemSizingStrategy)] = MapItemSizingStrategy,
			};

		/// <summary>
		/// Command mapper for <see cref="StructuredItemsView"/> commands.
		/// </summary>
		public static CommandMapper<TItemsView, TizenStructuredItemsViewHandler<TItemsView>> StructuredItemsViewCommandMapper =
			new CommandMapper<TItemsView, TizenStructuredItemsViewHandler<TItemsView>>(ItemsViewCommandMapper);

		protected TizenStructuredItemsViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper ?? StructuredItemsViewCommandMapper)
		{
		}

		/// <summary>
		/// Gets the typed platform view for structured items.
		/// </summary>
		protected new TizenStructuredItemsViewControl<TItemsView>? PlatformView
			=> base.PlatformView as TizenStructuredItemsViewControl<TItemsView>;

		protected override void ConnectHandler(NView platformView)
		{
			base.ConnectHandler(platformView);
			UpdateItemsLayout();
		}

		protected virtual void UpdateItemsLayout()
		{
			PlatformView?.UpdateLayoutManager(VirtualView.ItemsLayout ?? LinearItemsLayout.Vertical);
		}

		protected virtual void UpdateHeader()
		{
			// Header is handled via the adaptor's GetHeaderView
			// Force a refresh of the adaptor to pick up header changes
			UpdateItemsSource();
		}

		protected virtual void UpdateFooter()
		{
			// Footer is handled via the adaptor's GetFooterView
			// Force a refresh of the adaptor to pick up footer changes
			UpdateItemsSource();
		}

		#region Mapper Methods

		/// <summary>
		/// Maps <see cref="StructuredItemsView.Header"/> to the platform.
		/// </summary>
		public static void MapHeader(TizenStructuredItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateHeader();
		}

		/// <summary>
		/// Maps <see cref="StructuredItemsView.HeaderTemplate"/> to the platform.
		/// </summary>
		public static void MapHeaderTemplate(TizenStructuredItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateHeader();
		}

		/// <summary>
		/// Maps <see cref="StructuredItemsView.Footer"/> to the platform.
		/// </summary>
		public static void MapFooter(TizenStructuredItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateFooter();
		}

		/// <summary>
		/// Maps <see cref="StructuredItemsView.FooterTemplate"/> to the platform.
		/// </summary>
		public static void MapFooterTemplate(TizenStructuredItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateFooter();
		}

		/// <summary>
		/// Maps <see cref="StructuredItemsView.ItemsLayout"/> to the platform.
		/// </summary>
		public static void MapItemsLayout(TizenStructuredItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateItemsLayout();
		}

		/// <summary>
		/// Maps <see cref="StructuredItemsView.ItemSizingStrategy"/> to the platform.
		/// </summary>
		public static void MapItemSizingStrategy(TizenStructuredItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateItemsLayout();
		}

		#endregion
	}
}
