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

		/// <summary>Cancels a superseded or disconnected image load.</summary>
		readonly TizenImageSourceLoader _sourceLoader = new();

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


		protected override void DisconnectHandler(Image platformView)
		{
			// A pending load must not write to a view that is being released.
			_sourceLoader.Cancel();

			base.DisconnectHandler(platformView);
			platformView.Clear();
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

			// The loader cancels any load still in flight, so a slow earlier source cannot
			// finish last and overwrite this one.
			return handler._sourceLoader.LoadAsync(
				image,
				provider,
				(platformImage, write, cancellationToken) =>
				{
					// The handler may have been disconnected while the source was resolving, in
					// which case there is no view left to write to.
					var platformView = handler.PlatformView;
					if (platformView is null || cancellationToken.IsCancellationRequested)
					{
						return Task.FromResult(TizenImageApplyResult.Cancelled);
					}

					return platformView.ApplyImageSourceAsync(
						platformImage,
						// Routed through the loader's guard so the assignment cannot land on a view
						// this load no longer owns. Returns false when it is refused.
						image => image is not null
							&& write(() => platformView.ResourceUrl = image.ResourceUrl),
						// NUI signal cleanup must be marshalled to the main loop; see
						// ApplyImageSourceAsync.
						handler.GetService<Microsoft.Maui.Dispatching.IDispatcher>(),
						cancellationToken);
				},
				() =>
				{
					// Nothing resolved, so the previous image must come down rather than linger.
					var platformView = handler.PlatformView;
					if (platformView is not null)
					{
						platformView.ResourceUrl = null;
					}
				});
		}
	}
}
