using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Adapters;
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
	public abstract class TizenItemsViewHandler<TItemsView> : TizenViewHandler<TItemsView, NView>
		where TItemsView : ItemsView
	{
		readonly OwnedReplacementCoordinator<ItemAdaptor> _adaptorOwnership = new();
		readonly GenerationGuard _adaptorGeneration = new();
		INotifyCollectionChanged? _observableCollection;
		bool _isShowingEmptyAdaptor;
		int _remainingItemsThreshold = -1;

		/// <summary>
		/// Property mapper for <see cref="ItemsView"/> properties.
		/// </summary>
		public static IPropertyMapper<TItemsView, TizenItemsViewHandler<TItemsView>> ItemsViewMapper =
			new PropertyMapper<TItemsView, TizenItemsViewHandler<TItemsView>>(TizenViewMappers.ViewMapper)
			{
				[nameof(ItemsView.ItemsSource)] = MapItemsSource,
				[nameof(ItemsView.ItemTemplate)] = MapItemTemplate,
				[nameof(ItemsView.EmptyView)] = MapEmptyView,
				[nameof(ItemsView.EmptyViewTemplate)] = MapEmptyViewTemplate,
				[nameof(ItemsView.RemainingItemsThreshold)] = MapRemainingItemsThreshold,
				[nameof(ItemsView.HorizontalScrollBarVisibility)] = MapHorizontalScrollBarVisibility,
				[nameof(ItemsView.VerticalScrollBarVisibility)] = MapVerticalScrollBarVisibility,
				[nameof(ItemsView.ItemsUpdatingScrollMode)] = MapItemsUpdatingScrollMode,

				// Controls routes IsVisible through the items handler rather than leaving it to the
				// chained ViewMapper, because the platform view here is a scrolling container whose
				// visibility must be applied to the container itself. Upstream's Tizen backend maps it too;
				// this port had dropped it, so hiding a CollectionView silently did nothing.
				[nameof(ItemsView.IsVisible)] = MapIsVisible,
			};

		/// <summary>
		/// Command mapper for <see cref="ItemsView"/> commands.
		/// </summary>
		public static CommandMapper<TItemsView, TizenItemsViewHandler<TItemsView>> ItemsViewCommandMapper =
			new CommandMapper<TItemsView, TizenItemsViewHandler<TItemsView>>(TizenViewMappers.ViewCommandMapper)
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
		protected ItemAdaptor? Adaptor => _adaptorOwnership.Current;

		protected override void ConnectHandler(NView platformView)
		{
			base.ConnectHandler(platformView);
			var collectionView = (platformView as TizenItemsViewControl<TItemsView>)?.CollectionView;
			try
			{
				_remainingItemsThreshold = VirtualView.RemainingItemsThreshold;
				if (collectionView is not null)
					collectionView.Scrolled += OnCollectionViewScrolled;
				UpdateItemsSource();
				UpdateScrollBarVisibility();
			}
			catch
			{
				try
				{
					UnsubscribeFromCollectionChanges();
					if (collectionView is not null)
					{
						collectionView.Scrolled -= OnCollectionViewScrolled;
						SetAdaptorCore(collectionView, null, notifyInstalled: false);
					}
				}
				finally
				{
					base.DisconnectHandler(platformView);
				}
				throw;
			}
		}

		protected override void DisconnectHandler(NView platformView)
		{
			var collectionView = (platformView as TizenItemsViewControl<TItemsView>)?.CollectionView;
			ExceptionSafeCleanup.Run(
				UnsubscribeFromCollectionChanges,
				() =>
				{
					if (collectionView is not null)
						collectionView.Scrolled -= OnCollectionViewScrolled;
				},
				() =>
				{
					if (collectionView is not null)
						SetAdaptorCore(collectionView, null, notifyInstalled: false);
				},
				() => base.DisconnectHandler(platformView));
		}

		public override void SetVirtualView(IView view)
		{
			if (((IElementHandler)this).PlatformView is TizenItemsViewControl<TItemsView> platformView
				&& view is TItemsView itemsView)
			{
				platformView.Rebind(itemsView);
			}

			base.SetVirtualView(view);
		}

		protected virtual void UpdateItemsSource()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null)
				return;

			UnsubscribeFromCollectionChanges();

			var itemsSource = VirtualView?.ItemsSource;

			// CRITICAL: Subscribe to observable sources BEFORE checking if empty.
			// The upstream pattern (MauiCollectionView.cs:47-61) subscribes first, then calls
			// UpdateAdaptor. A source that starts empty must still be observed so the first add
			// triggers the transition from empty adaptor to real adaptor.
			if (itemsSource is INotifyCollectionChanged observable)
			{
				_observableCollection = observable;
				_observableCollection.CollectionChanged += OnCollectionChanged;
			}

			// Now decide which adaptor to use based on whether the source has items.
			if (itemsSource == null || !HasItems(itemsSource))
			{
				TransitionToEmptyAdaptor();
			}
			else
			{
				TransitionToRealAdaptor();
			}
		}

		/// <summary>
		/// Centralized transition to the real data adaptor. Handles backing field, native view,
		/// event subscriptions, and disposal of any replaced adaptor in one atomic operation.
		/// </summary>
		protected virtual void TransitionToRealAdaptor()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null)
				return;

			var newAdaptor = CreateAdaptor();
			SetAdaptor(collectionView, newAdaptor);
			_isShowingEmptyAdaptor = false;
		}

		/// <summary>
		/// Centralized transition to the empty view adaptor. Handles backing field, native view,
		/// event subscriptions, and disposal of any replaced adaptor in one atomic operation.
		/// </summary>
		protected virtual void TransitionToEmptyAdaptor()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null || VirtualView == null)
				return;

			if (VirtualView.EmptyView != null
				|| VirtualView.EmptyViewTemplate != null
				|| VirtualView is StructuredItemsView { Header: not null }
				|| VirtualView is StructuredItemsView { Footer: not null })
			{
				var emptyAdaptor = new TizenEmptyItemAdaptor(VirtualView);
				SetAdaptor(collectionView, emptyAdaptor);
			}
			else
			{
				// No empty view configured, just clear the adaptor
				SetAdaptor(collectionView, null);
			}
			_isShowingEmptyAdaptor = true;
		}

		/// <summary>
		/// Atomically sets the adaptor on the native view, updating the backing field, disposing the
		/// old adaptor, and managing selection event subscriptions.
		/// </summary>
		protected void SetAdaptor(NCollectionView collectionView, ItemAdaptor? newAdaptor) =>
			SetAdaptorCore(collectionView, newAdaptor, notifyInstalled: true);

		void SetAdaptorCore(
			NCollectionView collectionView,
			ItemAdaptor? newAdaptor,
			bool notifyInstalled)
		{
			_adaptorGeneration.Advance();
			_adaptorOwnership.Replace(
				newAdaptor,
				() => collectionView.Adaptor = null,
				adaptor =>
				{
					if (adaptor is ITizenItemTemplateAdaptor selectionAdaptor)
					{
						selectionAdaptor.SelectionChanged -= OnAdaptorSelectionChanged;
						selectionAdaptor.ItemsChanged -= OnAdaptorItemsChanged;
					}
				},
				adaptor => (adaptor as IDisposable)?.Dispose(),
				adaptor =>
				{
					if (adaptor is ITizenItemTemplateAdaptor selectionAdaptor)
					{
						selectionAdaptor.SelectionChanged += OnAdaptorSelectionChanged;
						selectionAdaptor.ItemsChanged += OnAdaptorItemsChanged;
					}
				},
				adaptor => collectionView.Adaptor = adaptor);

			if (notifyInstalled && ReferenceEquals(_adaptorOwnership.Current, newAdaptor))
				OnAdaptorInstalled();
		}

		protected virtual void OnAdaptorInstalled()
		{
		}

		protected virtual void OnAdaptorItemsChanged(object? sender, EventArgs e) => QueueItemsChanged();

		protected virtual void OnItemsChanged()
		{
		}

		void OnCollectionViewScrolled(object? sender, CollectionViewScrolledEventArgs e)
		{
			ItemsScrollCoordinator.Publish(
				Adaptor?.Count ?? 0,
				_remainingItemsThreshold,
				e.HorizontalDelta.ToScaledDP(),
				e.HorizontalOffset.ToScaledDP(),
				e.VerticalDelta.ToScaledDP(),
				e.VerticalOffset.ToScaledDP(),
				e.FirstVisibleItemIndex,
				e.CenterItemIndex,
				e.LastVisibleItemIndex,
				VirtualView.SendScrolled,
				VirtualView.SendRemainingItemsThresholdReached);
		}

		protected void UpdateScrollBarVisibility()
		{
			var collectionView = NativeCollectionView;
			if (collectionView?.LayoutManager is null)
				return;

			collectionView.ScrollView.HideScrollbar = collectionView.LayoutManager.IsHorizontal
				? VirtualView.HorizontalScrollBarVisibility == ScrollBarVisibility.Never
				: VirtualView.VerticalScrollBarVisibility == ScrollBarVisibility.Never;
		}

		protected virtual ItemAdaptor CreateAdaptor()
		{
			return new TizenItemTemplateAdaptor(VirtualView);
		}

		protected virtual void UpdateEmptyView()
		{
			var collectionView = NativeCollectionView;
			if (collectionView == null || VirtualView == null)
				return;

			bool hasItems = VirtualView.ItemsSource != null && HasItems(VirtualView.ItemsSource);
			if (!hasItems)
				TransitionToEmptyAdaptor();
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

			// The upstream pattern (MauiCollectionView.cs:207-220) checks empty->real and real->empty
			// transitions on every collection change, not just Reset.
			bool hasItems = VirtualView.ItemsSource != null && HasItems(VirtualView.ItemsSource);

			if (!hasItems)
			{
				// Transition to empty adaptor if not already showing one
				if (!_isShowingEmptyAdaptor)
				{
					TransitionToEmptyAdaptor();
				}
			}
			else if (_isShowingEmptyAdaptor)
			{
				// We have items now but are showing empty adaptor - transition to real
				TransitionToRealAdaptor();
			}
			// If Reset action and we have items and already showing real adaptor, recreate it
			// to pick up the new data. The ItemAdaptor internally tracks the IEnumerable but
			// a Reset may indicate a complete replacement.
			else if (e.Action == NotifyCollectionChangedAction.Reset)
			{
				TransitionToRealAdaptor();
			}

			QueueItemsChanged();
		}

		void QueueItemsChanged()
		{
			var generation = _adaptorGeneration.Capture();
			var virtualView = VirtualView;
			void Apply()
			{
				_adaptorGeneration.RunIfCurrent(
					generation,
					() =>
					{
						if (ReferenceEquals(virtualView, VirtualView) && Adaptor is not null)
							OnItemsChanged();
					});
			}

			if (virtualView?.Dispatcher is { } dispatcher)
				dispatcher.Dispatch(Apply);
			else
				Apply();
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
			try
			{
				return enumerator.MoveNext();
			}
			finally
			{
				(enumerator as IDisposable)?.Dispose();
			}
		}

		#region Mapper Methods

		/// <summary>
		/// Applies <see cref="ItemsView.IsVisible"/> to the platform collection view.
		/// </summary>
		/// <remarks>
		/// Mapped explicitly rather than inherited from the chained view mapper: Controls declares
		/// IsVisible on the items handler itself, and the platform view is the scrolling container,
		/// so the value has to be applied there.
		/// </remarks>
		public static void MapIsVisible(TizenItemsViewHandler<TItemsView> handler, TItemsView itemsView)
		{
			handler.PlatformView.UpdateVisibility(itemsView);
		}

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
		/// Updates the threshold consumed by the native Scrolled event bridge.
		/// </summary>
		/// <remarks>
		/// Tizen exposes visible indexes on its Scrolled event, so the handler computes the remaining
		/// count and raises MAUI's threshold event.
		/// </remarks>
		public static void MapRemainingItemsThreshold(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler._remainingItemsThreshold = view.RemainingItemsThreshold;
		}

		/// <summary>
		/// Maps horizontal scrollbar visibility when the active layout is horizontal.
		/// </summary>
		public static void MapHorizontalScrollBarVisibility(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateScrollBarVisibility();
		}

		/// <summary>
		/// Maps vertical scrollbar visibility when the active layout is vertical.
		/// </summary>
		public static void MapVerticalScrollBarVisibility(TizenItemsViewHandler<TItemsView> handler, TItemsView view)
		{
			handler.UpdateScrollBarVisibility();
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
				var index = handler.Adaptor is TizenGroupItemTemplateAdaptor grouped
					? grouped.GetAbsoluteIndex(scrollArgs.GroupIndex, scrollArgs.Index)
					: scrollArgs.Index;
				if (index >= 0)
					collectionView.ScrollTo(index, (TScrollToPosition)scrollArgs.ScrollToPosition, scrollArgs.IsAnimated);
			}
			else if (scrollArgs.Item != null && collectionView.Adaptor != null)
			{
				int index = handler.Adaptor is TizenGroupItemTemplateAdaptor grouped
					? grouped.GetAbsoluteIndex(scrollArgs.Group, scrollArgs.Item)
					: collectionView.Adaptor.GetItemIndex(scrollArgs.Item);
				if (index >= 0)
				{
					collectionView.ScrollTo(index, (TScrollToPosition)scrollArgs.ScrollToPosition, scrollArgs.IsAnimated);
				}
			}
		}

		#endregion
	}
}
