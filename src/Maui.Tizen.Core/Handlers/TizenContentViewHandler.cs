using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="IContentView"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Handlers.ContentViewHandler</c> (Tizen) in dotnet/maui.
	/// </remarks>
	public class TizenContentViewHandler : TizenViewHandler<IContentView, TizenContentViewGroup>, ITizenContentViewHandler
	{
		ITizenPlatformViewHandler? _contentHandler;

		/// <summary>Property mapper for <see cref="IContentView"/> on Tizen.</summary>
		public static readonly IPropertyMapper<IContentView, ITizenContentViewHandler> Mapper =
			new PropertyMapper<IContentView, ITizenContentViewHandler>(ViewHandler.ViewMapper)
			{
				[nameof(IContentView.Background)] = MapBackground,
				[nameof(IContentView.Content)] = MapContent,
			};

		/// <summary>Command mapper for <see cref="IContentView"/> on Tizen.</summary>
		public static readonly CommandMapper<IContentView, ITizenContentViewHandler> CommandMapper =
			new(ViewHandler.ViewCommandMapper);

		/// <summary>Initializes a new instance of the <see cref="TizenContentViewHandler"/> class.</summary>
		public TizenContentViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="TizenContentViewHandler"/> class.</summary>
		/// <param name="mapper">An optional property mapper override.</param>
		/// <param name="commandMapper">An optional command mapper override.</param>
		public TizenContentViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IContentView ITizenContentViewHandler.VirtualView => VirtualView;

		TizenContentViewGroup ITizenContentViewHandler.PlatformView => PlatformView;

		/// <inheritdoc />
		protected override TizenContentViewGroup CreatePlatformView()
		{
			_ = VirtualView ?? throw new InvalidOperationException(
				$"{nameof(VirtualView)} must be set to create a {nameof(TizenContentViewGroup)}.");

			return new TizenContentViewGroup(VirtualView)
			{
				CrossPlatformMeasure = VirtualView.CrossPlatformMeasure,
				CrossPlatformArrange = VirtualView.CrossPlatformArrange,
			};
		}

		/// <inheritdoc />
		public override void SetVirtualView(IView view)
		{
			base.SetVirtualView(view);

			PlatformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			PlatformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;
		}

		/// <summary>Maps <see cref="IView.Background"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The content view.</param>
		public static void MapBackground(ITizenContentViewHandler handler, IContentView view)
		{
#if TIZEN
			handler.PlatformView?.UpdateBackground(view);
#endif
		}

		/// <summary>Maps <see cref="IContentView.Content"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The content view.</param>
		public static void MapContent(ITizenContentViewHandler handler, IContentView view)
		{
			if (handler is TizenContentViewHandler contentViewHandler)
				contentViewHandler.UpdateContent();
		}

		void UpdateContent()
		{
			_ = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");

#if TIZEN
			PlatformView.Children.Clear();
#endif
			_contentHandler?.Dispose();
			_contentHandler = null;

			if (VirtualView.PresentedContent is not IView view)
				return;

#if TIZEN
			PlatformView.Children.Add(view.ToPlatformView(MauiContext));
			PlatformView.SetNeedMeasureUpdate();
#else
			_ = view.ToPlatform(MauiContext);
#endif

			if (view.Handler is ITizenPlatformViewHandler tizenHandler)
				_contentHandler = tizenHandler;
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_contentHandler?.Dispose();

			base.Dispose(disposing);
		}
	}
}
