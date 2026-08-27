// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.SwipeItemMenuItemHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named SwipeItemMenuItemHandler, which still
// exists in Microsoft.Maui.Core.

using System.Threading.Tasks;
using Tizen.UIExtensions.NUI;
using TColor = Tizen.UIExtensions.Common.Color;
using NColor = global::Tizen.NUI.Color;
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
		/// Applies the label's character spacing.
		/// </summary>
		/// <remarks>
		/// Upstream marks this <c>[MissingMapper]</c> on Tizen, and an earlier revision of this
		/// handler recorded it as unsupported "fixed style with no per-character tracking control".
		/// That was wrong: the platform view is a <c>Tizen.UIExtensions.NUI.Button</c>, whose
		/// <c>TextLabel</c> exposes <c>CharacterSpacing</c> — the same route the core slice already
		/// uses for <c>IButton</c>. Upstream's omission was a gap, not a platform limitation.
		/// </remarks>
		public static void MapCharacterSpacing(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
			if (view is ITextStyle textStyle)
				handler?.PlatformView?.UpdateCharacterSpacing(textStyle);
		}

		/// <summary>
		/// Applies the label's font family, size and attributes.
		/// </summary>
		/// <remarks>
		/// Upstream marks this <c>[MissingMapper]</c> on Tizen, and an earlier revision recorded it
		/// as unsupported on the grounds that the button "does not expose the font family/size/slant
		/// of its embedded label". That was wrong: <c>Tizen.UIExtensions.NUI.Button</c> exposes
		/// <c>FontFamily</c>, <c>FontSize</c> and <c>FontAttributes</c>, and the core slice already
		/// drives them through <c>UpdateTizenFont</c> for <c>IButton</c>.
		/// </remarks>
		public static void MapFont(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
			if (view is ITextStyle textStyle)
				handler?.PlatformView?.UpdateTizenFont(textStyle, handler.GetService<IFontManager>());
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
		/// Tints the menu button's icon with the Controls-level <c>IconColor</c>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The value is not on <see cref="ISwipeItemMenuItem"/>. Microsoft.Maui.Controls' swipe item
		/// carries it through the separate Core interface
		/// <see cref="ISwipeItemMenuItemIconColor"/>, which is what neutral MAUI reads too, so this
		/// needs no dependency on Microsoft.Maui.Controls.
		/// </para>
		/// <para>
		/// Upstream's Tizen backend supplies no implementation, so an earlier revision of this
		/// handler recorded the key as an unsupported no-op. That was wrong:
		/// <c>Tizen.NUI.Components.Button.Icon</c> is an <c>ImageView</c>, and
		/// <c>ImageView.ImageColor</c> multiplies the image by a colour, which is exactly the tint
		/// this needs. Absence upstream was a gap, not a platform limitation.
		/// </para>
		/// <para>
		/// A null or unset colour resets the tint to white. <c>ImageColor</c> multiplies, so white
		/// is the identity — clearing it to transparent would erase the icon instead.
		/// </para>
		/// </remarks>
		public static void MapIconColor(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view)
		{
			if (handler?.PlatformView?.Icon is not { } icon)
				return;

			var color = (view as ISwipeItemMenuItemIconColor)?.IconColor;

			// Constructed directly rather than through a conversion helper: both
			// Tizen.UIExtensions.NUI and Core's TizenPlatformExtensions expose a ToTizen that
			// returns Tizen.UIExtensions.Common.Color, whereas ImageColor takes a Tizen.NUI.Color.
			// (Core's file even aliases NColor to the UIExtensions type, so the names are no guide.)
			icon.ImageColor = color is null
				? NColor.White
				: new NColor(color.Red, color.Green, color.Blue, color.Alpha);
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
				(platformImage, write, cancellationToken) =>
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
						// Routed through the loader's guard so the assignment cannot land on a view
						// this load no longer owns. Returns false when it is refused.
						image => image is not null
							&& write(() => platformView.ResourceUrl = image.ResourceUrl),
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
