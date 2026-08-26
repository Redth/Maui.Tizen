// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.BorderHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named BorderHandler, which still
// exists in Microsoft.Maui.Core.

using System;
using Microsoft.Maui.Platform;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Tizen handler for <see cref="IBorderView"/>.</summary>
	/// <remarks>
	/// Stroke and shape are rendered by the container <see cref="WrapperView"/> rather than by the
	/// content view itself, so every stroke mapper re-runs <c>WrapperView.UpdateBorder</c>. Tizen has
	/// no per-property native stroke API to update incrementally.
	/// </remarks>
	public class TizenBorderHandler : ViewHandler<IBorderView, ContentViewGroup>
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

		IPlatformViewHandler? _contentHandler;

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

		public override bool NeedsContainer => true;

		protected override ContentViewGroup CreatePlatformView()
		{
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} must be set to create a ContentViewGroup");

			return new ContentViewGroup(VirtualView)
			{
				CrossPlatformMeasure = VirtualView.CrossPlatformMeasure,
				CrossPlatformArrange = VirtualView.CrossPlatformArrange
			};
		}

		protected override void SetupContainer()
		{
			base.SetupContainer();
			(ContainerView as WrapperView)?.UpdateBorder(VirtualView);
			(ContainerView as WrapperView)?.UpdateBackground(VirtualView.Background);
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
				PlatformView.Children.Add(view.ToPlatform(MauiContext));
				if (view.Handler is IPlatformViewHandler thandler)
				{
					_contentHandler = thandler;
				}
			}
		}

		void InvalidateBorder()
		{
			(ContainerView as WrapperView)?.UpdateBorder(VirtualView);
		}

		public static void MapBackground(TizenBorderHandler handler, IBorderView border)
		{
			handler.UpdateValue(nameof(IViewHandler.ContainerView));
			(handler.ContainerView as WrapperView)?.UpdateBackground(border.Background);
		}

		public static void MapContent(TizenBorderHandler handler, IBorderView border) => handler.UpdateContent();

		public static void MapStrokeShape(TizenBorderHandler handler, IBorderView border) => handler.InvalidateBorder();

		public static void MapStroke(TizenBorderHandler handler, IBorderView border) => handler.InvalidateBorder();

		public static void MapStrokeThickness(TizenBorderHandler handler, IBorderView border) => handler.InvalidateBorder();

		public static void MapStrokeLineCap(TizenBorderHandler handler, IBorderView border) => handler.InvalidateBorder();

		public static void MapStrokeLineJoin(TizenBorderHandler handler, IBorderView border) => handler.InvalidateBorder();

		public static void MapStrokeDashPattern(TizenBorderHandler handler, IBorderView border) => handler.InvalidateBorder();

		public static void MapStrokeDashOffset(TizenBorderHandler handler, IBorderView border) => handler.InvalidateBorder();

		public static void MapStrokeMiterLimit(TizenBorderHandler handler, IBorderView border) => handler.InvalidateBorder();
	}
}
