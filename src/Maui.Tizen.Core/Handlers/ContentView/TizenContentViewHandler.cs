// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.ContentViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so that neutral handler no
// longer has a Tizen half to complete and a partial declaration would not bind. This is a
// standalone handler that owns its own mappers.
//
// It is deliberately NOT named ContentViewHandler: that type still exists in Microsoft.Maui.Core
// and re-declaring the name would be ambiguous for consumers referencing both assemblies.

using System;
using Microsoft.Maui.Platform;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Tizen handler for <see cref="IContentView"/>.</summary>
	public class TizenContentViewHandler : ViewHandler<IContentView, ContentViewGroup>
	{
		public static IPropertyMapper<IContentView, TizenContentViewHandler> Mapper =
			new PropertyMapper<IContentView, TizenContentViewHandler>(ViewMapper)
			{
				[nameof(IContentView.Background)] = MapBackground,
				[nameof(IContentView.Content)] = MapContent,
			};

		public static CommandMapper<IContentView, TizenContentViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
			};

		IPlatformViewHandler? _contentHandler;

		public TizenContentViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenContentViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenContentViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override ContentViewGroup CreatePlatformView()
		{
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} must be set to create a ContentViewGroup");

			return new ContentViewGroup(VirtualView)
			{
				CrossPlatformMeasure = VirtualView.CrossPlatformMeasure,
				CrossPlatformArrange = VirtualView.CrossPlatformArrange
			};
		}

		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by base class.");

			// Layout is owned by the MAUI cross-platform implementation; the native view only invokes it.
			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;
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
				PlatformView.SetNeedMeasureUpdate();
			}
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

		public static void MapBackground(TizenContentViewHandler handler, IContentView view)
		{
			handler.UpdateValue(nameof(IViewHandler.ContainerView));
			handler.ToPlatform()?.UpdateBackground(view);
		}

		public static void MapContent(TizenContentViewHandler handler, IContentView page)
		{
			handler.UpdateContent();
		}
	}
}
