// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.SwipeItemMenuItemHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named SwipeItemMenuItemHandler, which still
// exists in Microsoft.Maui.Core.

using System.Threading.Tasks;
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.NUI;
using TColor = Tizen.UIExtensions.Common.Color;

namespace Microsoft.Maui.Handlers
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
				[nameof(ISwipeItemMenuItem.IconColor)] = MapIconColor,
			};

		public static CommandMapper<ISwipeItemMenuItem, TizenSwipeItemMenuItemHandler> CommandMapper =
			new(ElementCommandMapper)
			{
			};

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

			var textColor = handler.VirtualView.GetTextColor()?.ToPlatform() ?? TColor.Default;
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

			var swipeView = handler.PlatformView.GetParentOfType<MauiSwipeView>();
			swipeView?.UpdateIsVisibleSwipeItem(view);
		}

		public static void MapSource(TizenSwipeItemMenuItemHandler handler, ISwipeItemMenuItem view) =>
			_ = MapSourceAsync(handler, view);

		/// <summary>
		/// Intentional no-op. The Tizen swipe menu button renders its icon through a plain image view
		/// with no tint/colour-filter API, so <see cref="ISwipeItemMenuItem.IconColor"/> cannot be
		/// applied natively. This matches the upstream Tizen backend, which left the mapper
		/// unimplemented. See docs/wave-b-mapper-parity.md.
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

			return view.UpdateSourceAsync(
				handler.PlatformView,
				provider,
				platformImage =>
				{
					if (platformImage is not null)
					{
						handler.PlatformView.Icon.ResourceUrl = platformImage.ResourceUrl;
					}
				});
		}
	}
}
