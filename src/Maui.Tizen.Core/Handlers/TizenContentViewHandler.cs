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
	public class TizenContentViewHandler : TizenViewHandler<IContentView, TizenContentViewGroup>, IContentViewHandler
	{
		ITizenPlatformViewHandler? _contentHandler;

		internal bool HasOwnedContent => _contentHandler is not null;

		/// <summary>Property mapper for <see cref="IContentView"/> on Tizen.</summary>
		public static readonly IPropertyMapper<IContentView, IContentViewHandler> Mapper =
			new PropertyMapper<IContentView, IContentViewHandler>(TizenViewMappers.ViewMapper, ContentViewHandler.Mapper)
			{
				[nameof(IContentView.Background)] = MapBackground,
				[nameof(IContentView.Content)] = MapContent,
			};

		/// <summary>Command mapper for <see cref="IContentView"/> on Tizen.</summary>
		public static readonly CommandMapper<IContentView, IContentViewHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

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

		IContentView IContentViewHandler.VirtualView => VirtualView;

		object IContentViewHandler.PlatformView => PlatformView;

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
		public static void MapBackground(IContentViewHandler handler, IContentView view)
		{
#if TIZEN
			((TizenContentViewGroup?)handler.PlatformView)?.UpdateBackground(view);
#endif
		}

		/// <summary>Maps <see cref="IContentView.Content"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The content view.</param>
		public static void MapContent(IContentViewHandler handler, IContentView view)
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
			var contentHandler = disposing ? _contentHandler : null;

			TizenCleanup.Run(
				() =>
				{
					if (disposing)
						_contentHandler = null;
				},
				() => contentHandler?.Dispose(),
				() => base.Dispose(disposing));
		}
	}
}
