// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.SwipeItemMenuItemHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named SwipeItemMenuItemHandler, which still
// exists in Microsoft.Maui.Core.

using System.Threading.Tasks;
using Tizen.UIExtensions.NUI;
using TColor = Tizen.UIExtensions.Common.Color;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="ISwipeItemMenuItem"/>.</summary>
	public class TizenSwipeItemMenuItemHandler : ElementHandler<ISwipeItemMenuItem, Button>
	{
		public static IPropertyMapper<ISwipeItemMenuItem, TizenSwipeItemMenuItemHandler> Mapper =
			new PropertyMapper<ISwipeItemMenuItem, TizenSwipeItemMenuItemHandler>(ElementMapper)
			{
				[nameof(ISwipeItemMenuItem.Text)] = MapText,
				[nameof(ITextStyle.TextColor)] = MapTextColor,
				[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
				[nameof(ITextStyle.Font)] = MapFont,
				[nameof(ISwipeItemMenuItem.Background)] = MapBackground,
				[nameof(ISwipeItemMenuItem.Visibility)] = MapVisibility,
				[nameof(IImageSourcePart.Source)] = MapSource,

				// Controls-contributed key. Microsoft.Maui.Controls adds IconColor to the NEUTRAL
				// SwipeItemMenuItemHandler.Mapper, which this handler does not chain. The name is a
				// literal because ISwipeItemMenuItem has no IconColor property to take nameof from --
				// an earlier revision used nameof(ISwipeItemMenuItem.IconColor) and did not compile,
				// which is easily misread as "the key does not exist". It does; only the Core
				// interface member does not.
				[IconColorKey] = MapIconColor,
			};

		public static CommandMapper<ISwipeItemMenuItem, TizenSwipeItemMenuItemHandler> CommandMapper =
			new(ElementCommandMapper)
			{
			};

		/// <summary>Cancels a superseded or disconnected image load.</summary>
		readonly TizenImageSourceLoader _sourceLoader = new();

		public TizenSwipeItemMenuItemHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenSwipeItemMenuItemHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenSwipeItemMenuItemHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		/// <inheritdoc />
		protected override void DisconnectHandler(Button platformView)
		{
			// A pending load must not write to a view that is being released.
			_sourceLoader.Cancel();

			base.DisconnectHandler(platformView);
		}

		protected override Button CreatePlatformElement() =>
			new Button
			{
				BackgroundColor = global::Tizen.NUI.Color.Transparent,
				IconRelativeOrientation = global::Tizen.NUI.Components.Button.IconOrientation.Top,
				CornerRadius = 0,
			};

		public static void MapText(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view) =>
			handler.PlatformView?.UpdateText(view);

		public static void MapTextColor(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view) =>
			handler.PlatformView?.UpdateTextColor(view);

		/// <summary>
		/// Intentional no-op, carried over from dotnet/maui. The Tizen swipe menu button renders its
		/// label through a fixed style with no per-character tracking control.
		/// See docs/wave-b-mapper-parity.md.
		/// </summary>
		public static void MapCharacterSpacing(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
		}

		/// <summary>
		/// Intentional no-op, carried over from dotnet/maui. The swipe menu button does not expose the
		/// font family/size/slant of its embedded label. See docs/wave-b-mapper-parity.md.
		/// </summary>
		public static void MapFont(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
		}

		public static void MapBackground(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
			if (handler.PlatformView == null)
				return;

			handler.PlatformView.UpdateBackground(handler.VirtualView.Background);

			var textColor = handler.VirtualView.TextColor.ToTizenCommonColor();
			if (textColor != TColor.Default)
			{
				handler.PlatformView.TextColor = textColor;
			}
		}

		public static void MapVisibility(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
			if (view.Visibility.ToPlatformVisibility())
			{
				handler.PlatformView.Show();
			}
			else
			{
				handler.PlatformView.Hide();
			}

			var swipeView = handler.PlatformView.GetParentOfType<TizenSwipeViewGroup>();
			swipeView?.UpdateIsVisibleSwipeItem(view);
		}

		public static void MapSource(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view) =>
			_ = MapSourceAsync(handler, view);

		/// <summary>
		/// The <c>IconColor</c> mapper key, contributed by Microsoft.Maui.Controls rather than
		/// declared on <see cref="ISwipeItemMenuItem"/>.
		/// </summary>
		public const string IconColorKey = "IconColor";

		/// <summary>
		/// Intentional no-op. The Tizen swipe menu button renders its icon through a plain image view
		/// with no tint or colour-filter API, so the Controls-level <c>IconColor</c> cannot be applied
		/// natively. Upstream's Tizen backend likewise supplies no implementation. Mapped explicitly
		/// rather than left absent so the property is a documented gap instead of a silent one.
		/// See docs/wave-b-mapper-parity.md.
		/// </summary>
		public static void MapIconColor(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
		}


		public static Task MapSourceAsync(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
			if (handler.MauiContext is null)
			{
				return Task.CompletedTask;
			}

			var provider = handler.GetRequiredService<IImageSourceServiceProvider>();

			// The loader cancels any load still in flight, so a slow earlier source cannot
			// finish last and overwrite this one.
			return handler._sourceLoader.LoadAsync(
				view,
				provider,
				(platformImage, cancellationToken) =>
				{
					// The handler may have been disconnected while the source was resolving, in
					// which case there is no view left to write to.
					var platformView = handler.PlatformView.Icon;
					if (platformView is null || cancellationToken.IsCancellationRequested)
					{
						return Task.FromResult(TizenImageApplyResult.Cancelled);
					}

					return platformView.ApplyImageSourceAsync(
						platformImage,
						image =>
						{
							if (image is not null)
							{
								platformView.ResourceUrl = image.ResourceUrl;
							}
						},
						cancellationToken);
				},
				() =>
				{
					// Nothing resolved, so the previous image must come down rather than linger.
					var platformView = handler.PlatformView.Icon;
					if (platformView is not null)
					{
						platformView.ResourceUrl = null;
					}
				});
		}
	}
}
