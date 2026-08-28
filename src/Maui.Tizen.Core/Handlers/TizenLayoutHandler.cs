using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="ILayout"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Handlers.LayoutHandler</c> (Tizen) in dotnet/maui. The command
	/// mapper keys intentionally match <c>Microsoft.Maui.ILayoutHandler</c> member names because
	/// MAUI Controls raises child operations by key string.
	/// </remarks>
	public class TizenLayoutHandler : TizenViewHandler<ILayout, TizenLayoutViewGroup>, ILayoutHandler
	{
		/// <summary>
		/// The children in LOGICAL order - the same order as the layout's own collection.
		/// </summary>
		/// <remarks>
		/// Deliberately NOT the native z-order. Update, Insert and Remove all receive logical
		/// indices, and PlatformView.Children is sorted by ZIndex; those coincide only while every
		/// child sits at ZIndex 0. This list previously mirrored the native order, so
		/// <c>_children[index]</c> returned an unrelated child as soon as any ZIndex was set - and
		/// Update then disposed it.
		///
		/// Native positions are derived when needed, from the virtual view's z-ordering, rather
		/// than being conflated with this list.
		/// </remarks>
		readonly List<IView> _children = new();

		internal int LogicalChildCount => _children.Count;

		/// <summary>Property mapper for <see cref="ILayout"/> on Tizen.</summary>
		public static readonly IPropertyMapper<ILayout, ILayoutHandler> Mapper =
			new PropertyMapper<ILayout, ILayoutHandler>(TizenViewMappers.ViewMapper, LayoutHandler.Mapper)
			{
				[nameof(ILayout.Background)] = MapBackground,
				[nameof(ILayout.ClipsToBounds)] = MapClipsToBounds,
				[nameof(IView.InputTransparent)] = MapInputTransparent,
			};

		/// <summary>Command mapper for <see cref="ILayout"/> on Tizen.</summary>
		public static readonly CommandMapper<ILayout, ILayoutHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
				[nameof(ILayoutHandler.Add)] = MapAdd,
				[nameof(ILayoutHandler.Remove)] = MapRemove,
				[nameof(ILayoutHandler.Clear)] = MapClear,
				[nameof(ILayoutHandler.Insert)] = MapInsert,
				[nameof(ILayoutHandler.Update)] = MapUpdate,
				[nameof(ILayoutHandler.UpdateZIndex)] = MapUpdateZIndex,
			};

		/// <summary>Initializes a new instance of the <see cref="TizenLayoutHandler"/> class.</summary>
		public TizenLayoutHandler()
			: base(Mapper, CommandMapper)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="TizenLayoutHandler"/> class.</summary>
		/// <param name="mapper">An optional property mapper override.</param>
		/// <param name="commandMapper">An optional command mapper override.</param>
		public TizenLayoutHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		ILayout ILayoutHandler.VirtualView => VirtualView;

		object ILayoutHandler.PlatformView => PlatformView;

		/// <inheritdoc />
		protected override TizenLayoutViewGroup CreatePlatformView()
		{
			_ = VirtualView ?? throw new InvalidOperationException(
				$"{nameof(VirtualView)} must be set to create a {nameof(TizenLayoutViewGroup)}.");

			return new TizenLayoutViewGroup(VirtualView)
			{
				CrossPlatformMeasure = VirtualView.CrossPlatformMeasure,
				CrossPlatformArrange = VirtualView.CrossPlatformArrange,
			};
		}

		/// <inheritdoc />
		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);

			_ = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");

			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;

#if TIZEN
			PlatformView.Children.Clear();
#endif
			_children.Clear();

			// The NATIVE collection is filled in z-order, because that is what determines paint
			// order. The LOGICAL list must not be: it is indexed by Update, Insert and Remove,
			// which all receive logical positions.
			//
			// Both were previously filled from OrderByZIndex, so _children was z-ordered here even
			// though every consumer treats it as logical - which is the same conflation that made
			// Update dispose the wrong child.
			foreach (var child in VirtualView.OrderByZIndex())
			{
#if TIZEN
				PlatformView.Children.Add(child.ToPlatformView(MauiContext));
#else
				_ = child.ToPlatform(MauiContext);
#endif
			}

			foreach (var child in VirtualView)
				_children.Add(child);
		}

		/// <inheritdoc />
		public void Add(IView child)
		{
			_ = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");

			// Native position comes from the z-ordering; the logical list simply appends.
			var targetIndex = VirtualView.GetLayoutHandlerIndex(child);
#if TIZEN
			PlatformView.Children.Insert(targetIndex, child.ToPlatformView(MauiContext));
#else
			_ = child.ToPlatform(MauiContext);
#endif
			_children.Add(child);
			EnsureZIndexOrder(child);
			PlatformView.SetNeedMeasureUpdate();
		}

		/// <summary>
		/// Removes a child from the native tree and the logical list, disposing its handler.
		/// </summary>
		/// <remarks>
		/// The native view is located through the child's OWN handler, never by position - which is
		/// what makes this correct regardless of z-order. Disposing the handler disposes the native
		/// view it owns, so the view is only unparented here and never disposed twice.
		/// </remarks>
		void RemoveChildCore(IView child)
		{
#if TIZEN
			if (child.Handler.ToPlatformView() is TizenNativeView childView)
				PlatformView.Children.Remove(childView);
#endif

			_children.Remove(child);
			(child.Handler as ITizenPlatformViewHandler)?.Dispose();
		}

		/// <inheritdoc />
		public void Remove(IView child)
		{
			if (child.Handler is ITizenPlatformViewHandler)
				RemoveChildCore(child);

#if TIZEN
			PlatformView.MarkChanged();
#endif
			PlatformView.SetNeedMeasureUpdate();
		}

		/// <inheritdoc />
		public void Clear()
		{
#if TIZEN
			var platformChildren = PlatformView.Children.ToList();
			PlatformView.Children.Clear();
			foreach (var platformChild in platformChildren)
				platformChild.Dispose();
#endif

			foreach (var child in _children)
				(child.Handler as ITizenPlatformViewHandler)?.Dispose();

			_children.Clear();
			PlatformView.SetNeedMeasureUpdate();
		}

		/// <inheritdoc />
		public void Insert(int index, IView child)
		{
			_ = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");

			var targetIndex = VirtualView.GetLayoutHandlerIndex(child);
#if TIZEN
			PlatformView.Children.Insert(targetIndex, child.ToPlatformView(MauiContext));
#else
			_ = child.ToPlatform(MauiContext);
#endif

			// Logical position for the logical list; the native insert above used the z-position.
			_children.Insert(Math.Clamp(index, 0, _children.Count), child);
			EnsureZIndexOrder(child);
			PlatformView.SetNeedMeasureUpdate();
		}

		/// <inheritdoc />
		public void Update(int index, IView child)
		{
			_ = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");

			// `index` is a LOGICAL position, and _children is now kept in logical order, so this
			// genuinely identifies the outgoing child. It previously indexed a z-ORDERED list,
			// which returned an unrelated child the moment any ZIndex was non-zero - and this
			// method then disposed it, leaving the child actually being replaced on screen with
			// its handler intact. Nothing threw.
			var outgoing = index >= 0 && index < _children.Count ? _children[index] : null;

			if (ReferenceEquals(outgoing, child))
				return;

			if (outgoing is not null)
			{
				// By identity, and the native view found through its own handler - never by
				// position, which is exactly what was wrong.
				RemoveChildCore(outgoing);
			}

			// Insert at the same LOGICAL position the outgoing child occupied; the native slot is
			// re-derived from the z-ordering.
			Insert(index, child);
		}

		/// <inheritdoc />
		public void UpdateZIndex(IView child)
		{
			_ = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");

			EnsureZIndexOrder(child);
		}

		void EnsureZIndexOrder(IView child)
		{
#if TIZEN
			if (PlatformView.Children.Count == 0)
				return;

			var platformChildView = child.ToPlatformView(MauiContext!);
			var currentIndex = PlatformView.Children.IndexOf(platformChildView);

			if (currentIndex == -1)
				return;

			var targetIndex = VirtualView.GetLayoutHandlerIndex(child);
			if (targetIndex > currentIndex)
			{
				platformChildView.RaiseToTop();
				for (var i = targetIndex + 1; i < PlatformView.Children.Count; i++)
					PlatformView.Children[i].RaiseToTop();
			}
			else
			{
				platformChildView.LowerToBottom();
				for (var i = targetIndex - 1; i >= 0; i--)
					PlatformView.Children[i].LowerToBottom();
			}
#else
			_ = child;
#endif
		}

		/// <summary>Maps <see cref="IView.Background"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="layout">The layout.</param>
		public static void MapBackground(ILayoutHandler handler, ILayout layout)
		{
#if TIZEN
			((TizenLayoutViewGroup?)handler.PlatformView)?.UpdateBackground(layout);
#endif
		}

		/// <summary>Maps <see cref="ILayout.ClipsToBounds"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="layout">The layout.</param>
		public static void MapClipsToBounds(ILayoutHandler handler, ILayout layout)
		{
#if TIZEN
			((TizenLayoutViewGroup)handler.PlatformView).ClippingMode = layout.ClipsToBounds
				? global::Tizen.NUI.ClippingModeType.ClipToBoundingBox
				: global::Tizen.NUI.ClippingModeType.Disabled;
#endif
		}

		/// <summary>Maps <see cref="IView.InputTransparent"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="layout">The layout.</param>
		public static void MapInputTransparent(ILayoutHandler handler, ILayout layout)
		{
			if (handler.PlatformView is TizenLayoutViewGroup viewGroup)
				viewGroup.InputTransparent = layout.InputTransparent;
		}

		static void MapAdd(ILayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.Add(args.View);
		}

		static void MapRemove(ILayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.Remove(args.View);
		}

		static void MapClear(ILayoutHandler handler, ILayout layout, object? arg) =>
			handler.Clear();

		static void MapInsert(ILayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.Insert(args.Index, args.View);
		}

		static void MapUpdate(ILayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.Update(args.Index, args.View);
		}

		static void MapUpdateZIndex(ILayoutHandler handler, ILayout layout, object? arg)
		{
			// The argument is the child IView itself, NOT a LayoutHandlerUpdate: both MAUI's
			// ViewHandler.MapZIndex and this backend's TizenViewMappers.MapZIndex forward the view
			// directly. Matching MAUI's own MapUpdateZIndex, which does `if (arg is IView view)`.
			if (arg is IView view)
				handler.UpdateZIndex(view);
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				foreach (var child in _children)
					(child.Handler as ITizenPlatformViewHandler)?.Dispose();

				_children.Clear();
			}

			base.Dispose(disposing);
		}
	}
}
