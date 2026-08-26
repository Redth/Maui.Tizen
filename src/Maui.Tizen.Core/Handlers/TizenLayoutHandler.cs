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
	public class TizenLayoutHandler : TizenViewHandler<ILayout, TizenLayoutViewGroup>, ITizenLayoutHandler
	{
		readonly List<IView> _children = new();

		/// <summary>Property mapper for <see cref="ILayout"/> on Tizen.</summary>
		public static readonly IPropertyMapper<ILayout, ITizenLayoutHandler> Mapper =
			new PropertyMapper<ILayout, ITizenLayoutHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(ILayout.Background)] = MapBackground,
				[nameof(ILayout.ClipsToBounds)] = MapClipsToBounds,
				[nameof(IView.InputTransparent)] = MapInputTransparent,
			};

		/// <summary>Command mapper for <see cref="ILayout"/> on Tizen.</summary>
		public static readonly CommandMapper<ILayout, ITizenLayoutHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
				[nameof(ITizenLayoutHandler.Add)] = MapAdd,
				[nameof(ITizenLayoutHandler.Remove)] = MapRemove,
				[nameof(ITizenLayoutHandler.Clear)] = MapClear,
				[nameof(ITizenLayoutHandler.Insert)] = MapInsert,
				[nameof(ITizenLayoutHandler.Update)] = MapUpdate,
				[nameof(ITizenLayoutHandler.UpdateZIndex)] = MapUpdateZIndex,
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

		ILayout ITizenLayoutHandler.VirtualView => VirtualView;

		TizenLayoutViewGroup ITizenLayoutHandler.PlatformView => PlatformView;

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

			foreach (var child in VirtualView.OrderByZIndex())
			{
#if TIZEN
				PlatformView.Children.Add(child.ToPlatformView(MauiContext));
#else
				_ = child.ToPlatform(MauiContext);
#endif
				_children.Add(child);
			}
		}

		/// <inheritdoc />
		public void Add(IView child)
		{
			_ = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");

			var targetIndex = VirtualView.GetLayoutHandlerIndex(child);
#if TIZEN
			PlatformView.Children.Insert(targetIndex, child.ToPlatformView(MauiContext));
#else
			_ = child.ToPlatform(MauiContext);
#endif
			_children.Insert(Math.Clamp(targetIndex, 0, _children.Count), child);
			EnsureZIndexOrder(child);
			PlatformView.SetNeedMeasureUpdate();
		}

		/// <inheritdoc />
		public void Remove(IView child)
		{
			if (child.Handler is ITizenPlatformViewHandler childHandler)
			{
#if TIZEN
				if (child.Handler.ToPlatformView() is TizenNativeView childView)
					PlatformView.Children.Remove(childView);
#endif
				_children.Remove(child);
				childHandler.Dispose();
			}

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
			_ = index;
			Add(child);
		}

		/// <inheritdoc />
		public void Update(int index, IView child)
		{
			_ = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");

#if TIZEN
			if (index >= 0 && index < PlatformView.Children.Count)
			{
				var toBeRemoved = PlatformView.Children[index];
				PlatformView.Children.RemoveAt(index);
				toBeRemoved.Dispose();
			}
#endif

			if (index >= 0 && index < _children.Count)
			{
				var childToBeRemoved = _children[index];
				_children.RemoveAt(index);
				(childToBeRemoved.Handler as ITizenPlatformViewHandler)?.Dispose();
			}

			Add(child);
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
		public static void MapBackground(ITizenLayoutHandler handler, ILayout layout)
		{
#if TIZEN
			handler.PlatformView?.UpdateBackground(layout);
#endif
		}

		/// <summary>Maps <see cref="ILayout.ClipsToBounds"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="layout">The layout.</param>
		public static void MapClipsToBounds(ITizenLayoutHandler handler, ILayout layout)
		{
#if TIZEN
			handler.PlatformView.ClippingMode = layout.ClipsToBounds
				? global::Tizen.NUI.ClippingModeType.ClipToBoundingBox
				: global::Tizen.NUI.ClippingModeType.Disabled;
#endif
		}

		/// <summary>Maps <see cref="IView.InputTransparent"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="layout">The layout.</param>
		public static void MapInputTransparent(ITizenLayoutHandler handler, ILayout layout)
		{
			if (handler.PlatformView is TizenLayoutViewGroup viewGroup)
				viewGroup.InputTransparent = layout.InputTransparent;
		}

		static void MapAdd(ITizenLayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.Add(args.View);
		}

		static void MapRemove(ITizenLayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.Remove(args.View);
		}

		static void MapClear(ITizenLayoutHandler handler, ILayout layout, object? arg) =>
			handler.Clear();

		static void MapInsert(ITizenLayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.Insert(args.Index, args.View);
		}

		static void MapUpdate(ITizenLayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.Update(args.Index, args.View);
		}

		static void MapUpdateZIndex(ITizenLayoutHandler handler, ILayout layout, object? arg)
		{
			if (arg is LayoutHandlerUpdate args)
				handler.UpdateZIndex(args.View);
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
