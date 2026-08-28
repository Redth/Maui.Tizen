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

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IImage"/>.</summary>
	public class TizenImageHandler : TizenViewHandler<IImage, Image>
	{
		public static IPropertyMapper<IImage, TizenImageHandler> Mapper =
			new PropertyMapper<IImage, TizenImageHandler>(ViewMapper)
			{
				[nameof(IImage.Background)] = MapBackground,
				[nameof(IImage.Aspect)] = MapAspect,
				[nameof(IImage.IsAnimationPlaying)] = MapIsAnimationPlaying,
				[nameof(IImage.Source)] = MapSource,
			};

		public static CommandMapper<IImage, TizenImageHandler> CommandMapper =
			new(ViewCommandMapper)
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

		protected override Image CreatePlatformView() => new Image();

		protected override void ConnectHandler(Image platformView)
		{
			var replacement = new TizenImageLoader<TizenImageSource>();

			TizenCleanup.Run(
				_sourceEvents.Invalidate,
				_sourceLoader.Dispose,
				() => _sourceLoader = replacement,
				() => base.ConnectHandler(platformView));
		}

		protected override void DisconnectHandler(Image platformView)
		{
			TizenCleanup.Run(
				_sourceEvents.Invalidate,
				_sourceLoader.Dispose,
				platformView.Clear,
				() => base.DisconnectHandler(platformView));
		}

		public static void MapBackground(TizenImageHandler handler, IImage image)
		{
			handler.PlatformView?.UpdateBackground(image);
		}

		public static void MapAspect(TizenImageHandler handler, IImage image) =>
			handler.PlatformView?.UpdateAspect(image);

		public static void MapIsAnimationPlaying(TizenImageHandler handler, IImage image) =>
			handler.PlatformView?.UpdateIsAnimationPlaying(image);

		public static void MapSource(TizenImageHandler handler, IImage image) =>
			_ = MapSourceAsync(handler, image);

		public static Task MapSourceAsync(TizenImageHandler handler, IImage image)
		{
			if (handler.MauiContext is null)
			{
				return Task.CompletedTask;
			}

			var provider = handler.GetRequiredService<IImageSourceServiceProvider>();
			var source = image.Source;
			var virtualView = handler.VirtualView;
			var target = handler.PlatformView;
			var commitOnUiThread = TizenDispatchExtensions.CaptureDispatcher(handler);

			return handler._sourceLoader.LoadPartAsync(
				image,
				handler._sourceEvents,
				(imageSource, token) => provider.GetTizenImageAsync(imageSource, token),
				commitOnUiThread,
				platformImage => target.ResourceUrl = platformImage?.ResourceUrl,
				() =>
					ReferenceEquals(handler.VirtualView, virtualView) &&
					ReferenceEquals(handler.PlatformView, target));
		}
	}
}
