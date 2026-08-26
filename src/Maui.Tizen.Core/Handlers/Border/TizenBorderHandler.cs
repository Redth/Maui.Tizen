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
			new PropertyMapper<IBorderView, TizenBorderHandler>(ViewMapper)
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
			new(ViewCommandMapper)
			{
			};

		ITizenPlatformViewHandler? _contentHandler;

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
			base.SetVirtualView(view);

			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");

			// Measurement/arrangement remain owned by the MAUI cross-platform implementation.
			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_contentHandler?.Dispose();
				_contentHandler = null;
			}

			base.Dispose(disposing);
		}

		void UpdateContent()
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by base class.");

			PlatformView.Children.Clear();
			_contentHandler?.Dispose();
			_contentHandler = null;

			if (VirtualView.PresentedContent is IView view)
			{
				PlatformView.Children.Add(view.ToPlatformView(MauiContext));
				if (view.Handler is ITizenPlatformViewHandler thandler)
				{
					_contentHandler = thandler;
				}
			}
		}

		public static void MapBackground(TizenBorderHandler handler, IBorderView border) =>
			handler.PlatformView?.UpdateBackground(border);

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
	}
}
