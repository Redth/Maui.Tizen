// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.BorderHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named BorderHandler, which still
// exists in Microsoft.Maui.Core.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IBorderView"/>.</summary>
	/// <remarks>
	/// UNSUPPORTED: stroke and shape have no rendering path in this backend today. Upstream drew
	/// them on the container <c>TizenWrapperView</c>, but <see cref="TizenViewHandler{TVirtualView,
	/// TPlatformView}"/> pins <c>NeedsContainer</c> to <see langword="false"/> because MAUI exposes
	/// no settable container hook to an out-of-repo backend. Background still renders directly on
	/// the platform view. See docs/net11-status.md ("Required public MAUI API gaps") and
	/// docs/wave-b-mapper-parity.md.
	/// </remarks>
	public class TizenBorderHandler : TizenViewHandler<IBorderView, TizenContentViewGroup>
	{
		public static IPropertyMapper<IBorderView, TizenBorderHandler> Mapper =
			new PropertyMapper<IBorderView, TizenBorderHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IBorderView.Background)] = MapBackground,
				[nameof(IBorderView.Content)] = MapContent,
				[nameof(IBorderView.Shape)] = MapStrokeShape,
				[nameof(IBorderView.Stroke)] = MapStroke,
				[nameof(IBorderView.StrokeThickness)] = MapStrokeThickness,
				[nameof(IBorderView.StrokeLineCap)] = MapStrokeLineCap,
				[nameof(IBorderView.StrokeLineJoin)] = MapStrokeLineJoin,
				[nameof(IBorderView.StrokeDashPattern)] = MapStrokeDashPattern,
				[nameof(IBorderView.StrokeDashOffset)] = MapStrokeDashOffset,
				[nameof(IBorderView.StrokeMiterLimit)] = MapStrokeMiterLimit,
			};

		public static CommandMapper<IBorderView, TizenBorderHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
			};

		ITizenPlatformViewHandler? _contentHandler;
		TizenNativeView? _contentView;
		long _contentGeneration;
		readonly TizenDisconnectingState _disconnecting = new();

		public TizenBorderHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenBorderHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenBorderHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}


		protected override TizenContentViewGroup CreatePlatformView()
		{
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} must be set to create a TizenContentViewGroup");

			return new TizenContentViewGroup(VirtualView)
			{
				CrossPlatformMeasure = VirtualView.CrossPlatformMeasure,
				CrossPlatformArrange = VirtualView.CrossPlatformArrange
			};
		}


		public override void SetVirtualView(IView view)
		{
			(((IElementHandler)this).PlatformView as TizenContentViewGroup)?.Rebind(view);
			base.SetVirtualView(view);

			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");

			// Measurement/arrangement remain owned by the MAUI cross-platform implementation.
			PlatformView.Rebind(VirtualView);
			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;
		}

		protected override void ConnectHandler(TizenContentViewGroup platformView)
		{
			_disconnecting.Connected();
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(TizenContentViewGroup platformView)
		{
			TizenCleanup.Run(
				_disconnecting.BeginDisconnect,
				() => ClearOwnedContent(platformView),
				() => base.DisconnectHandler(platformView));
		}

		void ClearOwnedContent(TizenContentViewGroup? platformView)
		{
			var operation = TizenContentOwnership.Reserve(ref _contentGeneration);
			TizenContentOwnership.Clear(
				operation,
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				view => platformView?.Children.Remove(view),
				static () => { },
				static () => true);
		}

		void UpdateContent()
		{
			if (_disconnecting.IsDisconnecting
				|| ((IElementHandler)this).PlatformView is not TizenContentViewGroup)
				return;

			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			var virtualView = VirtualView;
			var expectedContent = virtualView.PresentedContent;
			var operation = TizenContentOwnership.Reserve(ref _contentGeneration);
			TizenNativeView? replacementView = null;
			ITizenPlatformViewHandler? replacementHandler = null;

			if (expectedContent is IView view)
			{
				replacementView = view.ToPlatformView(MauiContext);
				if (view.Handler is ITizenPlatformViewHandler thandler)
					replacementHandler = thandler;
			}

			TizenContentOwnership.Replace(
				operation,
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				replacementView,
				replacementHandler,
				oldView => PlatformView.Children.Remove(oldView),
				newView => PlatformView.Children.Add(newView),
				static () => { },
				() =>
					ReferenceEquals(VirtualView, virtualView) &&
					ReferenceEquals(VirtualView.PresentedContent, expectedContent));
		}

		public static void MapBackground(TizenBorderHandler handler, IBorderView border)
		{
			if (Platform(handler) is not null)
				TizenViewMappers.MapBackground(handler, border);
		}

		public static void MapContent(TizenBorderHandler handler, IBorderView border) => handler.UpdateContent();

		/// <summary>
		/// UNSUPPORTED, not merely unimplemented. Upstream drew <see cref="IBorderStroke.Shape"/>
		/// on the container TizenWrapperView, and this backend cannot create a container: MAUI exposes no
		/// settable container hook to an out-of-repo assembly, so TizenViewHandler pins
		/// NeedsContainer to false. Border strokes therefore do not render. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </summary>
		public static void MapStrokeShape(TizenBorderHandler handler, IBorderView border)
		{
		}

		/// <summary>
		/// UNSUPPORTED, not merely unimplemented. Upstream drew <see cref="IBorderStroke.Stroke"/>
		/// on the container TizenWrapperView, and this backend cannot create a container: MAUI exposes no
		/// settable container hook to an out-of-repo assembly, so TizenViewHandler pins
		/// NeedsContainer to false. Border strokes therefore do not render. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </summary>
		public static void MapStroke(TizenBorderHandler handler, IBorderView border)
		{
		}

		/// <summary>
		/// UNSUPPORTED, not merely unimplemented. Upstream drew <see cref="IBorderStroke.StrokeThickness"/>
		/// on the container TizenWrapperView, and this backend cannot create a container: MAUI exposes no
		/// settable container hook to an out-of-repo assembly, so TizenViewHandler pins
		/// NeedsContainer to false. Border strokes therefore do not render. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </summary>
		public static void MapStrokeThickness(TizenBorderHandler handler, IBorderView border)
		{
		}

		/// <summary>
		/// UNSUPPORTED, not merely unimplemented. Upstream drew <see cref="IBorderStroke.StrokeLineCap"/>
		/// on the container TizenWrapperView, and this backend cannot create a container: MAUI exposes no
		/// settable container hook to an out-of-repo assembly, so TizenViewHandler pins
		/// NeedsContainer to false. Border strokes therefore do not render. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </summary>
		public static void MapStrokeLineCap(TizenBorderHandler handler, IBorderView border)
		{
		}

		/// <summary>
		/// UNSUPPORTED, not merely unimplemented. Upstream drew <see cref="IBorderStroke.StrokeLineJoin"/>
		/// on the container TizenWrapperView, and this backend cannot create a container: MAUI exposes no
		/// settable container hook to an out-of-repo assembly, so TizenViewHandler pins
		/// NeedsContainer to false. Border strokes therefore do not render. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </summary>
		public static void MapStrokeLineJoin(TizenBorderHandler handler, IBorderView border)
		{
		}

		/// <summary>
		/// UNSUPPORTED, not merely unimplemented. Upstream drew <see cref="IBorderStroke.StrokeDashPattern"/>
		/// on the container TizenWrapperView, and this backend cannot create a container: MAUI exposes no
		/// settable container hook to an out-of-repo assembly, so TizenViewHandler pins
		/// NeedsContainer to false. Border strokes therefore do not render. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </summary>
		public static void MapStrokeDashPattern(TizenBorderHandler handler, IBorderView border)
		{
		}

		/// <summary>
		/// UNSUPPORTED, not merely unimplemented. Upstream drew <see cref="IBorderStroke.StrokeDashOffset"/>
		/// on the container TizenWrapperView, and this backend cannot create a container: MAUI exposes no
		/// settable container hook to an out-of-repo assembly, so TizenViewHandler pins
		/// NeedsContainer to false. Border strokes therefore do not render. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </summary>
		public static void MapStrokeDashOffset(TizenBorderHandler handler, IBorderView border)
		{
		}

		/// <summary>
		/// UNSUPPORTED, not merely unimplemented. Upstream drew <see cref="IBorderStroke.StrokeMiterLimit"/>
		/// on the container TizenWrapperView, and this backend cannot create a container: MAUI exposes no
		/// settable container hook to an out-of-repo assembly, so TizenViewHandler pins
		/// NeedsContainer to false. Border strokes therefore do not render. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </summary>
		public static void MapStrokeMiterLimit(TizenBorderHandler handler, IBorderView border)
		{
		}

		static TizenContentViewGroup? Platform(TizenBorderHandler handler) =>
			!handler._disconnecting.IsDisconnecting &&
			TizenHandlerLifecycle.TryGetLivePlatformView(handler, out TizenContentViewGroup? platformView)
				? platformView
				: null;
	}
}
