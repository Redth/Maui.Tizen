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
		TizenNativeView? _contentView;
		long _contentGeneration;
		readonly TizenDisconnectingState _disconnecting = new();

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
			(((IElementHandler)this).PlatformView as TizenContentViewGroup)?.Rebind(view);
			base.SetVirtualView(view);

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
				() => ClearContent(platformView),
				() => base.DisconnectHandler(platformView));
		}

		protected override void Dispose(bool disposing)
		{
			if (!disposing)
			{
				base.Dispose(disposing);
				return;
			}

			var platformView = ((IElementHandler)this).PlatformView as TizenContentViewGroup;
			TizenCleanup.Run(
				_disconnecting.BeginDisconnect,
				() => ClearContent(platformView),
				() => base.Dispose(disposing));
		}

		/// <summary>Maps <see cref="IView.Background"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The content view.</param>
		public static void MapBackground(IContentViewHandler handler, IContentView view)
		{
#if TIZEN
			if (handler is TizenContentViewHandler contentViewHandler)
				Platform(contentViewHandler)?.UpdateBackground(view);
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
			if (_disconnecting.IsDisconnecting
				|| ((IElementHandler)this).PlatformView is not TizenContentViewGroup platformView)
				return;

			var context = MauiContext ?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} should have been set by base class.");
			var virtualView = VirtualView;
			var expectedContent = virtualView.PresentedContent;
			var operation = TizenContentOwnership.Reserve(ref _contentGeneration);
			TizenNativeView? replacementView = null;
			ITizenPlatformViewHandler? replacementHandler = null;

			if (expectedContent is IView view)
			{
				replacementView = view.ToPlatformView(context);
				replacementHandler = view.Handler as ITizenPlatformViewHandler;
			}

			TizenContentOwnership.Replace(
				operation,
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				replacementView,
				replacementHandler,
				oldView => platformView.Children.Remove(oldView),
				newView =>
				{
					platformView.Children.Add(newView);
					platformView.SetNeedMeasureUpdate();
				},
				static () => { },
				() =>
					ReferenceEquals(VirtualView, virtualView) &&
					ReferenceEquals(VirtualView.PresentedContent, expectedContent));
		}

		void ClearContent(TizenContentViewGroup? platformView)
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

		static TizenContentViewGroup? Platform(TizenContentViewHandler handler) =>
			!handler._disconnecting.IsDisconnecting &&
			TizenHandlerLifecycle.TryGetLivePlatformView(handler, out TizenContentViewGroup? platformView)
				? platformView
				: null;
	}
}
