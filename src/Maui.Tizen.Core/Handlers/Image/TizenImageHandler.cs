// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.ImageHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named ImageHandler, which still
// exists in Microsoft.Maui.Core.

using System.Threading.Tasks;
using Tizen.UIExtensions.NUI;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;
using TizenImageView = Tizen.UIExtensions.NUI.Image;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IImage"/>.</summary>
	public class TizenImageHandler : TizenViewHandler<IImage, TizenImageView>
	{
		public static IPropertyMapper<IImage, TizenImageHandler> Mapper =
			new PropertyMapper<IImage, TizenImageHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IImage.Background)] = MapBackground,
				[nameof(IImage.Aspect)] = MapAspect,
				[nameof(IImage.IsAnimationPlaying)] = MapIsAnimationPlaying,
				[nameof(IImage.Source)] = MapSource,
			};

		public static CommandMapper<IImage, TizenImageHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
			};

		TizenImageLoader<TizenImageSource> _sourceLoader = new();
		readonly TizenImageLoadEvents _sourceEvents = new();

		public TizenImageHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenImageHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenImageHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenImageView CreatePlatformView() => new TizenImageView();

		public override void SetVirtualView(IView view)
		{
			if (!ReferenceEquals(((IElementHandler)this).VirtualView, view))
			{
				var replacement = new TizenImageLoader<TizenImageSource>();
				TizenCleanup.Run(
					_sourceEvents.Invalidate,
					_sourceLoader.Dispose,
					() => _sourceLoader = replacement);
			}

			base.SetVirtualView(view);
		}

		protected override void ConnectHandler(TizenImageView platformView)
		{
			var replacement = new TizenImageLoader<TizenImageSource>();

			TizenCleanup.Run(
				_sourceEvents.Invalidate,
				_sourceLoader.Dispose,
				() => _sourceLoader = replacement,
				() => base.ConnectHandler(platformView));
		}

		protected override void DisconnectHandler(TizenImageView platformView)
		{
			TizenCleanup.Run(
				_sourceEvents.Invalidate,
				_sourceLoader.Dispose,
				platformView.Clear,
				() => base.DisconnectHandler(platformView));
		}

		public static void MapBackground(TizenImageHandler handler, IImage image)
		{
			if (Platform(handler) is not null)
				TizenViewMappers.MapBackground(handler, image);
		}

		public static void MapAspect(TizenImageHandler handler, IImage image) =>
			Platform(handler)?.UpdateAspect(image);

		public static void MapIsAnimationPlaying(TizenImageHandler handler, IImage image) =>
			Platform(handler)?.UpdateIsAnimationPlaying(image);

		public static void MapSource(TizenImageHandler handler, IImage image)
		{
#if TIZEN
			if (Platform(handler) is not null)
				MapSourceAsync(handler, image).FireAndForget(handler);
#endif
		}

		public static Task MapSourceAsync(TizenImageHandler handler, IImage image)
		{
			if (handler.MauiContext is null)
			{
				return Task.CompletedTask;
			}

			var target = Platform(handler);
			if (target is null)
				return Task.CompletedTask;

			var provider = handler.GetRequiredService<IImageSourceServiceProvider>();
			var source = image.Source;
			var virtualView = handler.VirtualView;
			var commitOnUiThread = TizenDispatchExtensions.CaptureDispatcher(handler);

			return handler._sourceLoader.LoadPartAsync(
				image,
				handler._sourceEvents,
				(imageSource, token) => provider.GetTizenImageAsync(imageSource, token),
				commitOnUiThread,
				(platformImage, token) => target.ApplyAndWaitForReadyAsync(platformImage, token),
				() =>
					ReferenceEquals(handler.VirtualView, virtualView) &&
					ReferenceEquals(Platform(handler), target));
		}

		static TizenImageView? Platform(TizenImageHandler handler) =>
			TizenHandlerLifecycle.TryGetLivePlatformView(handler, out TizenImageView? platformView)
				? platformView
				: null;
	}
}
