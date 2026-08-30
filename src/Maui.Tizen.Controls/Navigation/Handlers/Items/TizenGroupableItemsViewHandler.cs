using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Platform;
using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Handler for <see cref="GroupableItemsView"/> in the Tizen backend.
	/// </summary>
	/// <typeparam name="TItemsView">The MAUI GroupableItemsView type.</typeparam>
	/// <remarks>
	/// Adds support for IsGrouped, GroupHeaderTemplate, and GroupFooterTemplate properties.
	/// </remarks>
	public abstract class TizenGroupableItemsViewHandler<TItemsView> : TizenSelectableItemsViewHandler<TItemsView>
		where TItemsView : GroupableItemsView
	{
		/// <summary>
		/// Property mapper for <see cref="GroupableItemsView"/> properties.
		/// </summary>
		public static IPropertyMapper<TItemsView, TizenGroupableItemsViewHandler<TItemsView>> GroupableItemsViewMapper =
			new PropertyMapper<TItemsView, TizenGroupableItemsViewHandler<TItemsView>>(SelectableItemsViewMapper)
			{
				[nameof(GroupableItemsView.IsGrouped)] = MapIsGrouped,
				[nameof(GroupableItemsView.GroupHeaderTemplate)] = MapGroupHeaderTemplate,
				[nameof(GroupableItemsView.GroupFooterTemplate)] = MapGroupFooterTemplate,
			};

		/// <summary>
		/// Command mapper for <see cref="GroupableItemsView"/> commands.
		/// </summary>
		public static CommandMapper<TItemsView, TizenGroupableItemsViewHandler<TItemsView>> GroupableItemsViewCommandMapper =
			new CommandMapper<TItemsView, TizenGroupableItemsViewHandler<TItemsView>>(SelectableItemsViewCommandMapper);

		protected TizenGroupableItemsViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper ?? GroupableItemsViewCommandMapper)
		{
		}

		protected override ItemAdaptor CreateAdaptor()
		{
			if (VirtualView.IsGrouped)
			{
				return new TizenGroupItemTemplateAdaptor(VirtualView);
			}
			return base.CreateAdaptor();
		}

		#region Mapper Methods

		/// <summary>
		/// Maps <see cref="GroupableItemsView.IsGrouped"/> to the platform.
		/// </summary>
		public static void MapIsGrouped(TizenGroupableItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateItemsSource();
		}

		/// <summary>
		/// Maps <see cref="GroupableItemsView.GroupHeaderTemplate"/> to the platform.
		/// </summary>
		public static void MapGroupHeaderTemplate(TizenGroupableItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateItemsSource();
		}

		/// <summary>
		/// Maps <see cref="GroupableItemsView.GroupFooterTemplate"/> to the platform.
		/// </summary>
		public static void MapGroupFooterTemplate(TizenGroupableItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateItemsSource();
		}

		#endregion
	}
}
