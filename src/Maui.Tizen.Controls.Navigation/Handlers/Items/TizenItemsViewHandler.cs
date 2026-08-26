using System;
using System.Collections;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Platform;
using Tizen.UIExtensions.NUI;
using NView = Tizen.NUI.BaseComponents.View;
using NCollectionView = Tizen.UIExtensions.NUI.CollectionView;
using TScrollToPosition = Tizen.UIExtensions.Common.ScrollToPosition;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Base handler for <see cref="ItemsView"/> in the Tizen backend.
	/// </summary>
	/// <typeparam name="TItemsView">The MAUI ItemsView type.</typeparam>
	/// <remarks>
	/// <para>
	/// This handler manages the core ItemsSource, ItemTemplate, EmptyView, and scrollbar properties.
	/// Derived handlers add selection, grouping, header/footer, and layout management.
	/// </para>
	/// <para>
	/// The in-tree backend used partial classes on the framework handlers. This out-of-tree version
	/// is a standalone handler that owns the full mapper and platform view lifecycle.
	/// </para>
	/// </remarks>
	public abstract class TizenItemsViewHandler<TItemsView> : ViewHandler<TItemsView, NView>
		where TItemsView : ItemsView
	{
		ITizenItemTemplateAdaptor? _adaptor;
		INotifyCollectionChanged? _observableCollection;

		/// <summary>
		/// Property mapper for <see cref="ItemsView"/> properties.
		/// </summary>
		public static IPropertyMapper<TItemsView, TizenItemsViewHandler<TItemsView>> ItemsViewMapper =
			new PropertyMapper<TItemsView, TizenItemsViewHandler<TItemsView>>(ViewMapper)
			{
				[nameof(ItemsView.ItemsSource)] = MapItemsSource,
				[nameof(ItemsView.ItemTemplate)] = MapItemTemplate,
				[nameof(ItemsView.EmptyView)] = MapEmptyView,
				[nameof(ItemsView.EmptyViewTemplate)] = MapEmptyViewTemplate,
				[nameof(ItemsView.RemainingItemsThreshold)] = MapRemainingItemsThreshold,
				[nameof(ItemsView.HorizontalScrollBarVisibility)] = MapHorizontalScrollBarVisibility,
				[nameof(ItemsView.VerticalScrollBarVisibility)] = MapVerticalScrollBarVisibility,
				[nameof(ItemsView.ItemsUpdatingScrollMode)] = MapItemsUpdatingScrollMode,
			};

		/// <summary>
		/// Command mapper for <see cref="ItemsView"/> commands.
		/// </summary>
		public static CommandMapper<TItemsView, TizenItemsViewHandler<TItemsView>> ItemsViewCommandMapper =
			new CommandMapper<TItemsView, TizenItemsViewHandler<TItemsView>>(ViewCommandMapper)
			{
				[nameof(ItemsView.ScrollTo)] = MapScrollTo,
			};

		protected TizenItemsViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper ?? ItemsViewCommandMapper)
		{
		}

		/// <summary>
		/// Gets the native CollectionView from the platform view.
		/// </summary>
		protected NCollectionView? NativeCollectionView => (PlatformView as TizenItemsViewControl<TItemsView>)?.CollectionView;

		/// <summary>
		/// Gets or sets the current item adaptor.
		/// </summary>
		protected ITizenItemTemplateAdaptor? Adaptor
		{
			get => _adaptor;
			set
			{
				if (_adaptor != null)
				{
					_adaptor.SelectionChanged -= OnAdaptorSelectionChanged;
				}
				_adaptor = value;
				if (_adaptor != null)
				{
					_adaptor.SelectionChanged += OnAdaptorSelectionChanged;
				}
			}
		}

		protected override void ConnectHandler(NView platformView)
		{
			base.ConnectHandler(platformView);
			UpdateItemsSource();
		}

		protected override void DisconnectHandler(NView platformView)
		{
			UnsubscribeFromCollectionChanges();
			if (Adaptor != null)
			{
				(Adaptor as IDisposable)?.Dispose();
				Adaptor = null;
			}
			base.DisconnectHandler(platformView);
		}

		protected virtual void UpdateItemsSource()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null)
				return;

			UnsubscribeFromCollectionChanges();

			var itemsSource = VirtualView?.ItemsSource;
			if (itemsSource == null || !HasItems(itemsSource))
			{
				// Show empty view
				UpdateEmptyView();
				return;
			}

			// Subscribe to collection changes for INotifyCollectionChanged sources
			if (itemsSource is INotifyCollectionChanged observable)
			{
				_observableCollection = observable;
				_observableCollection.CollectionChanged += OnCollectionChanged;
			}

			// Create and set the adaptor
			Adaptor = CreateAdaptor();
			collectionView.Adaptor = Adaptor as ItemAdaptor;
		}

		protected virtual ITizenItemTemplateAdaptor CreateAdaptor()
		{
			return new TizenItemTemplateAdaptor(VirtualView);
		}

		protected virtual void UpdateEmptyView()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null || VirtualView == null)
				return;

			bool hasItems = VirtualView.ItemsSource != null && HasItems(VirtualView.ItemsSource);
			if (!hasItems && (VirtualView.EmptyView != null || VirtualView.EmptyViewTemplate != null))
			{
				collectionView.Adaptor = new TizenEmptyItemAdaptor(VirtualView);
			}
		}

		protected virtual void OnAdaptorSelectionChanged(object? sender, TizenCollectionViewSelectionChangedEventArgs e)
		{
			// Override in SelectableItemsViewHandler
		}

		void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null || VirtualView == null)
				return;

			// For Reset actions (clear, replace all) - recreate the adaptor
			// The ItemAdaptor internally tracks the IEnumerable/INotifyCollectionChanged
			// and updates automatically via its internal binding to the collection.
			// For non-Reset actions, check if we need to switch from/to empty view.
			if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				UpdateItemsSource();
			}

			// Check if we need to show/hide empty view
			bool hasItems = VirtualView.ItemsSource != null && HasItems(VirtualView.ItemsSource);
			if (!hasItems)
			{
				UpdateEmptyView();
			}
		}

		void UnsubscribeFromCollectionChanges()
		{
			if (_observableCollection != null)
			{
				_observableCollection.CollectionChanged -= OnCollectionChanged;
				_observableCollection = null;
			}
		}

		static bool HasItems(IEnumerable itemsSource)
		{
			var enumerator = itemsSource.GetEnumerator();
			return enumerator.MoveNext();
		}

		#region Mapper Methods

		/// <summary>
		/// Maps <see cref="ItemsView.ItemsSource"/> to the platform.
		/// </summary>
		public static void MapItemsSource(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateItemsSource();
		}

		/// <summary>
		/// Maps <see cref="ItemsView.ItemTemplate"/> to the platform.
		/// </summary>
		public static void MapItemTemplate(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateItemsSource();
		}

		/// <summary>
		/// Maps <see cref="ItemsView.EmptyView"/> to the platform.
		/// </summary>
		public static void MapEmptyView(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateEmptyView();
		}

		/// <summary>
		/// Maps <see cref="ItemsView.EmptyViewTemplate"/> to the platform.
		/// </summary>
		public static void MapEmptyViewTemplate(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateEmptyView();
		}

		/// <summary>
		/// No-op: RemainingItemsThreshold is not supported on Tizen.
		/// </summary>
		/// <remarks>
		/// Tizen.UIExtensions.NUI.CollectionView does not currently expose an API for threshold-based
		/// notifications when approaching the end of content.
		/// </remarks>
		public static void MapRemainingItemsThreshold(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			// No-op: Tizen does not support RemainingItemsThreshold
		}

		/// <summary>
		/// No-op: HorizontalScrollBarVisibility is not configurable on Tizen CollectionView.
		/// </summary>
		public static void MapHorizontalScrollBarVisibility(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			// No-op: Tizen CollectionView does not expose scrollbar visibility settings
		}

		/// <summary>
		/// No-op: VerticalScrollBarVisibility is not configurable on Tizen CollectionView.
		/// </summary>
		public static void MapVerticalScrollBarVisibility(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			// No-op: Tizen CollectionView does not expose scrollbar visibility settings
		}

		/// <summary>
		/// No-op: ItemsUpdatingScrollMode is not supported on Tizen.
		/// </summary>
		public static void MapItemsUpdatingScrollMode(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			// No-op: Tizen does not support ItemsUpdatingScrollMode
		}

		/// <summary>
		/// Handles the ScrollTo command.
		/// </summary>
		public static void MapScrollTo(TizenItemsViewHandler<TItemsView> handler, TItemsView view, object? args)
		{
			if (args is not ScrollToRequestEventArgs scrollArgs)
				return;

			var collectionView = handler.NativeCollectionView;
			if (collectionView == null)
				return;

			if (scrollArgs.Mode == ScrollToMode.Position)
			{
				collectionView.ScrollTo(scrollArgs.Index, (TScrollToPosition)scrollArgs.ScrollToPosition, scrollArgs.IsAnimated);
			}
			else if (scrollArgs.Item != null && collectionView.Adaptor != null)
			{
				int index = collectionView.Adaptor.GetItemIndex(scrollArgs.Item);
				if (index >= 0)
				{
					collectionView.ScrollTo(index, (TScrollToPosition)scrollArgs.ScrollToPosition, scrollArgs.IsAnimated);
				}
			}
		}

		#endregion
	}
}
