using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.UIExtensions.Common.Color;
using NPoint = Tizen.UIExtensions.Common.Point;
using NRect = Tizen.UIExtensions.Common.Rect;
using NSize = Tizen.UIExtensions.Common.Size;
using NTextDecorations = Tizen.UIExtensions.Common.TextDecorations;
using Color = Microsoft.Maui.Graphics.Color;
using NFontAttributes = Tizen.UIExtensions.Common.FontAttributes;
using NTextAlignment = Tizen.UIExtensions.Common.TextAlignment;
using Rect = Microsoft.Maui.Graphics.Rect;
using Size = Microsoft.Maui.Graphics.Size;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Native NUI helpers used by this backend's handlers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.Platform.DPExtensions</c>, <c>ViewExtensions</c>,
	/// <c>LabelExtensions</c>, <c>ColorExtensions</c>, <c>WindowExtensions</c> and
	/// <c>ElementExtensions</c> (Tizen) in dotnet/maui.
	/// </para>
	/// <para>
	/// Those types are <c>public</c> in MAUI, but only inside the <c>net*-tizen</c> build of
	/// <c>Microsoft.Maui.dll</c>. Taking a dependency on them would defeat the point of extracting
	/// the Tizen backend, and would break the moment MAUI drops its Tizen target framework. This
	/// backend therefore owns the behaviour.
	/// </para>
	/// </remarks>
	public static class TizenPlatformExtensions
	{
		// ---------------------------------------------------------------------------------------
		// Density conversions (ported from DPExtensions).
		// ---------------------------------------------------------------------------------------

		/// <summary>Converts a pixel rectangle to device-independent units.</summary>
		/// <param name="rect">The pixel rectangle.</param>
		/// <returns>The rectangle in device-independent units.</returns>
		public static Rect ToDP(this NRect rect) =>
			new(rect.X.ToScaledDP(), rect.Y.ToScaledDP(), rect.Width.ToScaledDP(), rect.Height.ToScaledDP());

		/// <summary>Converts a NUI pixel rectangle to device-independent units.</summary>
		/// <param name="rect">The NUI rectangle.</param>
		/// <returns>The rectangle in device-independent units.</returns>
		public static Rect ToDP(this Rectangle rect) =>
			new(((double)rect.X).ToScaledDP(),
				((double)rect.Y).ToScaledDP(),
				((double)rect.Width).ToScaledDP(),
				((double)rect.Height).ToScaledDP());

		/// <summary>Converts a device-independent rectangle to pixels.</summary>
		/// <param name="rect">The rectangle.</param>
		/// <returns>The rectangle in pixels.</returns>
		public static NRect ToPixel(this Rect rect) =>
			new(rect.X.ToScaledPixel(), rect.Y.ToScaledPixel(), rect.Width.ToScaledPixel(), rect.Height.ToScaledPixel());

		/// <summary>Converts a pixel size to device-independent units.</summary>
		/// <param name="size">The pixel size.</param>
		/// <returns>The size in device-independent units.</returns>
		public static Size ToDP(this NSize size) =>
			new(size.Width.ToScaledDP(), size.Height.ToScaledDP());

		/// <summary>Converts a device-independent size to pixels.</summary>
		/// <param name="size">The size.</param>
		/// <returns>The size in pixels.</returns>
		public static NSize ToPixel(this Size size) =>
			new(size.Width.ToScaledPixel(), size.Height.ToScaledPixel());

		/// <summary>Converts a device-independent point to pixels.</summary>
		/// <param name="point">The point.</param>
		/// <returns>The point in pixels.</returns>
		public static NPoint ToPixel(this Point point) =>
			new(point.X.ToScaledPixel(), point.Y.ToScaledPixel());

		/// <summary>Converts a device-independent value to a scaled font point size.</summary>
		/// <param name="dp">The device-independent value.</param>
		/// <returns>The value in points.</returns>
		public static double ToScaledPoint(this double dp) =>
			dp.ToScaledPixel() * 72 / (TizenDisplayDensity.Current * TizenDisplayDensity.BaselineDpi);

		// ---------------------------------------------------------------------------------------
		// Colors (ported from ColorExtensions).
		// ---------------------------------------------------------------------------------------

		/// <summary>Converts a MAUI color to its Tizen counterpart.</summary>
		/// <param name="color">The color.</param>
		/// <returns>The Tizen color.</returns>
		public static NColor ToTizen(this Color color) =>
			new(color.Red, color.Green, color.Blue, color.Alpha);

		// ---------------------------------------------------------------------------------------
		// Geometry (ported from ViewExtensions).
		// ---------------------------------------------------------------------------------------

		/// <summary>Gets the platform view's bounds, in pixels.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <returns>The bounds in pixels.</returns>
		public static NRect GetBounds(this TizenNativeView platformView)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			return new NRect(
				platformView.Position.X,
				platformView.Position.Y,
				platformView.Size.Width,
				platformView.Size.Height);
		}

		/// <summary>Sets the platform view's bounds, in pixels.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="bounds">The bounds in pixels.</param>
		public static void UpdateBounds(this TizenNativeView platformView, NRect bounds)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			platformView.Position = new Position((float)bounds.X, (float)bounds.Y);
			platformView.Size = new global::Tizen.NUI.Size((float)bounds.Width, (float)bounds.Height);
		}

		// ---------------------------------------------------------------------------------------
		// Backgrounds (ported from ViewExtensions).
		// ---------------------------------------------------------------------------------------

		/// <summary>Applies a view's background paint to the platform view.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateBackground(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(view);

			platformView.UpdateBackground(view.Background);
		}

		/// <summary>Applies a paint to the platform view's background.</summary>
		/// <remarks>
		/// <para>
		/// A <see langword="null"/> paint is a no-op, matching dotnet/maui's
		/// <c>ViewExtensions.UpdateBackground</c>, which returns early rather than clearing. This
		/// matters: <see cref="Handlers.TizenPageHandler"/> gives a page an opaque white background
		/// at creation and then runs the background mapper, so clearing on null would repaint every
		/// page transparent at launch.
		/// </para>
		/// <para>
		/// Only solid colours are honoured. dotnet/maui renders gradient and image brushes through a
		/// <c>WrapperView</c> container, which this backend cannot construct -
		/// <c>ViewHandler.ContainerView</c> has a <c>private protected</c> setter. See
		/// docs/net11-status.md ("Required public MAUI API gaps").
		/// </para>
		/// </remarks>
		/// <param name="platformView">The platform view.</param>
		/// <param name="paint">The paint. <see langword="null"/> leaves the background untouched.</param>
		public static void UpdateBackground(this TizenNativeView platformView, Paint? paint)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			if (paint is null)
				return;

			if (paint is SolidPaint solid && solid.Color is Color color)
			{
				platformView.UpdateBackgroundColor(color.ToTizen());
				return;
			}

			if (paint.ToColor() is Color fallback)
				platformView.UpdateBackgroundColor(fallback.ToTizen());
		}

		/// <summary>Sets the platform view's background color.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="color">The color.</param>
		public static void UpdateBackgroundColor(this TizenNativeView platformView, NColor color)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			platformView.BackgroundColor = new global::Tizen.NUI.Color(
				(float)color.R,
				(float)color.G,
				(float)color.B,
				(float)color.A);
		}

		/// <summary>Applies the view's opacity to the platform view.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateOpacity(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			platformView.Opacity = (float)view.Opacity;
		}

		// ---------------------------------------------------------------------------------------
		// Label (ported from LabelExtensions).
		// ---------------------------------------------------------------------------------------

		/// <summary>Applies <see cref="ILabel.Text"/>.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="label">The cross-platform label.</param>
		public static void UpdateLabelText(this Label platformLabel, ILabel label)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(label);

			platformLabel.Text = label.Text ?? string.Empty;
		}

		/// <summary>Applies <see cref="ITextStyle.TextColor"/>.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="label">The cross-platform label.</param>
		public static void UpdateTextColor(this Label platformLabel, ILabel label)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(label);

			platformLabel.TextColor = label.TextColor is null ? NColor.Black : label.TextColor.ToTizen();
		}

		/// <summary>Applies <see cref="ITextStyle.Font"/>.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="label">The cross-platform label.</param>
		/// <param name="fontManager">The font manager.</param>
		public static void UpdateFont(this Label platformLabel, ILabel label, IFontManager fontManager)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(label);
			ArgumentNullException.ThrowIfNull(fontManager);

			platformLabel.FontSize = label.Font.Size > 0 ? label.Font.Size.ToScaledPoint() : 14d.ToScaledPoint();
			platformLabel.FontAttributes = label.Font.GetTizenFontAttributes();
			// dotnet/maui calls its Tizen-only IFontManager.GetFontFamily extension here; that
			// extension is not part of the cross-platform IFontManager surface, so the family name
			// is used directly. See docs/net11-status.md.
			_ = fontManager;
			platformLabel.FontFamily = label.Font.Family ?? string.Empty;
		}

		/// <summary>Applies <see cref="ITextAlignment.HorizontalTextAlignment"/>.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="label">The cross-platform label.</param>
		public static void UpdateHorizontalTextAlignment(this Label platformLabel, ILabel label)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(label);

			platformLabel.HorizontalTextAlignment = label.HorizontalTextAlignment switch
			{
				TextAlignment.Start => NTextAlignment.Start,
				TextAlignment.Center => NTextAlignment.Center,
				TextAlignment.End => NTextAlignment.End,
				_ => NTextAlignment.Auto,
			};
		}

		/// <summary>Applies <see cref="ITextAlignment.VerticalTextAlignment"/>.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="label">The cross-platform label.</param>
		public static void UpdateVerticalTextAlignment(this Label platformLabel, ILabel label)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(label);

			platformLabel.VerticalTextAlignment = label.VerticalTextAlignment switch
			{
				TextAlignment.Start => NTextAlignment.Start,
				TextAlignment.Center => NTextAlignment.Center,
				TextAlignment.End => NTextAlignment.End,
				_ => NTextAlignment.Auto,
			};
		}

		/// <summary>Applies <see cref="ILabel.TextDecorations"/>.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="label">The cross-platform label.</param>
		public static void UpdateTextDecorations(this Label platformLabel, ILabel label)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(label);

			platformLabel.TextDecorations = label.TextDecorations switch
			{
				TextDecorations.Strikethrough => NTextDecorations.Strikethrough,
				TextDecorations.Underline => NTextDecorations.Underline,
				_ => NTextDecorations.None,
			};
		}

		/// <summary>Applies <see cref="ITextStyle.CharacterSpacing"/>.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="label">The cross-platform label.</param>
		public static void UpdateCharacterSpacing(this Label platformLabel, ILabel label)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(label);

			platformLabel.CharacterSpacing = label.CharacterSpacing.ToScaledPixel();
		}

		/// <summary>Applies <see cref="ILabel.LineHeight"/>.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="label">The cross-platform label.</param>
		public static void UpdateLineHeight(this Label platformLabel, ILabel label)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(label);

			platformLabel.RelativeLineHeight = (float)label.LineHeight;
		}

		/// <summary>Applies <see cref="IView.Shadow"/> to a label.</summary>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateShadow(this Label platformLabel, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);
			ArgumentNullException.ThrowIfNull(view);

			var map = new PropertyMap();

			if (view.Shadow is IShadow shadow)
			{
				var offsetX = shadow.Offset.X.ToScaledPixel();
				var offsetY = shadow.Offset.Y.ToScaledPixel();
				var radius = ((double)shadow.Radius).ToScaledPixel();
				var color = (shadow.Paint.ToColor() ?? Colors.Black).MultiplyAlpha(shadow.Opacity);

				map.Add("offset", new PropertyValue(new Vector2(offsetX, offsetY)));
				map.Add("color", new PropertyValue(new global::Tizen.NUI.Color(
					color.Red, color.Green, color.Blue, color.Alpha)));
				map.Add("blurRadius", new PropertyValue(radius));
			}

			platformLabel.Shadow = map;
		}

		/// <summary>Maps a MAUI <see cref="Font"/> to Tizen font attributes.</summary>
		/// <param name="font">The font.</param>
		/// <returns>The Tizen font attributes.</returns>
		public static NFontAttributes GetTizenFontAttributes(this Font font)
		{
			var attributes = font.Weight == FontWeight.Bold
				? NFontAttributes.Bold
				: NFontAttributes.None;

			if (font.Slant != FontSlant.Default)
			{
				attributes = attributes == NFontAttributes.None
					? NFontAttributes.Italic
					: attributes | NFontAttributes.Italic;
			}

			return attributes;
		}

		// ---------------------------------------------------------------------------------------
		// Window (ported from WindowExtensions).
		// ---------------------------------------------------------------------------------------

		/// <summary>Reports the platform frame for <see cref="IWindow.X"/>.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="window">The cross-platform window.</param>
		public static void UpdateX(this TizenNativeWindow platformWindow, IWindow window) =>
			platformWindow.UpdateUnsupportedCoordinate(window);

		/// <summary>Reports the platform frame for <see cref="IWindow.Y"/>.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="window">The cross-platform window.</param>
		public static void UpdateY(this TizenNativeWindow platformWindow, IWindow window) =>
			platformWindow.UpdateUnsupportedCoordinate(window);

		/// <summary>Reports the platform frame for <see cref="IWindow.Width"/>.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="window">The cross-platform window.</param>
		public static void UpdateWidth(this TizenNativeWindow platformWindow, IWindow window) =>
			platformWindow.UpdateUnsupportedCoordinate(window);

		/// <summary>Reports the platform frame for <see cref="IWindow.Height"/>.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="window">The cross-platform window.</param>
		public static void UpdateHeight(this TizenNativeWindow platformWindow, IWindow window) =>
			platformWindow.UpdateUnsupportedCoordinate(window);

		/// <summary>
		/// Pushes the real device geometry into the cross-platform window.
		/// </summary>
		/// <remarks>
		/// Tizen windows are owned by the window manager: an application cannot move or resize its
		/// own window, so X/Y/Width/Height flow *out* of the platform rather than in. dotnet/maui
		/// names the equivalent helper <c>UpdateUnsupportedCoordinate</c> for the same reason.
		/// Calling <see cref="IWindow.FrameChanged"/> here is what tells the cross-platform window
		/// how big it actually is; without it <c>IWindow.Width</c>/<c>Height</c> would stay at
		/// their initial values forever, because these mappers only run at handler init.
		/// </remarks>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="window">The cross-platform window.</param>
		public static void UpdateUnsupportedCoordinate(this TizenNativeWindow platformWindow, IWindow window)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(window);

			window.FrameChanged(platformWindow.WindowPositionSize.ToDP());
		}

		// ---------------------------------------------------------------------------------------
		// Handler helpers (ported from ElementExtensions).
		// ---------------------------------------------------------------------------------------

		/// <summary>Gets the platform view for a handler.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/>.</returns>
		public static TizenNativeView? ToPlatformView(this IElementHandler? handler) =>
			handler?.PlatformView as TizenNativeView;

		/// <summary>Gets, creating if needed, the platform view for an element.</summary>
		/// <param name="element">The element.</param>
		/// <param name="context">The MAUI context.</param>
		/// <returns>The platform view.</returns>
		public static TizenNativeView ToPlatformView(this IElement element, IMauiContext context)
		{
			ArgumentNullException.ThrowIfNull(element);
			ArgumentNullException.ThrowIfNull(context);

			return Microsoft.Maui.Platform.ElementExtensions.ToPlatform(element, context) as TizenNativeView
				?? throw new InvalidOperationException(
					$"The handler for {element.GetType().FullName} did not produce a "
					+ $"{typeof(TizenNativeView).FullName}.");
		}
	}
}
