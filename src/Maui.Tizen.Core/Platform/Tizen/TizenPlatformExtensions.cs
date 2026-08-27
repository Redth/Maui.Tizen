using System;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.UIExtensions.Common.Color;
using NPoint = Tizen.UIExtensions.Common.Point;
using NRect = Tizen.UIExtensions.Common.Rect;
using NSize = Tizen.UIExtensions.Common.Size;
using NLineBreakMode = Tizen.UIExtensions.Common.LineBreakMode;
using NStrikethrough = Tizen.NUI.Text.Strikethrough;
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

			platformView.UpdateBackground(view.Background, clearWhenNull: true);
		}

		/// <summary>
		/// Applies a view's background, optionally clearing the native colour when the paint is
		/// <see langword="null"/>.
		/// </summary>
		/// <remarks>
		/// Two different behaviours are needed and conflating them causes a visible bug either way:
		/// <list type="bullet">
		/// <item><description>
		/// A view whose <c>Background</c> transitions from a colour back to <see langword="null"/>
		/// must have the old colour cleared, or the stale colour stays on screen forever.
		/// </description></item>
		/// <item><description>
		/// A page is created with an opaque white default and then has the background mapper run
		/// over it; clearing on null there would repaint every page transparent at launch. That
		/// case passes <c>clearWhenNull: false</c>.
		/// </description></item>
		/// </list>
		/// </remarks>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		/// <param name="clearWhenNull">Whether a null background resets the native colour.</param>
		public static void UpdateBackground(this TizenNativeView platformView, IView view, bool clearWhenNull)
		{
			ArgumentNullException.ThrowIfNull(view);

			platformView.UpdateBackground(view.Background, clearWhenNull);
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
		public static void UpdateBackground(this TizenNativeView platformView, Paint? paint) =>
			platformView.UpdateBackground(paint, clearWhenNull: false);

		/// <summary>Applies a paint to the platform view's background.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="paint">The paint.</param>
		/// <param name="clearWhenNull">
		/// When <see langword="true"/>, a null paint resets the native colour to transparent rather
		/// than leaving the previous colour in place.
		/// </param>
		public static void UpdateBackground(this TizenNativeView platformView, Paint? paint, bool clearWhenNull)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			if (paint is null)
			{
				if (clearWhenNull)
					platformView.UpdateBackgroundColor(NColor.Transparent);

				return;
			}

			if (paint is SolidPaint solid && solid.Color is Color color)
			{
				platformView.UpdateBackgroundColor(color.ToTizen());
				return;
			}

			// Gradients collapse to their representative colour, which is the best a backend can do
			// without a container view to render into (gap G1).
			if (paint.ToColor() is Color fallback)
			{
				platformView.UpdateBackgroundColor(fallback.ToTizen());
				return;
			}

			// No representative colour. In practice this is an image brush: ImagePaint.ToColor()
			// returns null, verified against Microsoft.Maui.Graphics.
			//
			// Leaving the previous colour in place would be the worst outcome - setting an image
			// background would appear to do nothing while a stale colour stayed on screen. Clearing
			// is honest: the image is unrendered either way, and the view ends up in a defined
			// state rather than an arbitrary one.
			//
			// Rendering it properly needs the public consumption-only IImageSourcePaint contract,
			// which is ABSENT from Microsoft.Maui 11.0.0-preview.7 - verified by reflection - plus
			// a container view to draw into, whose setter is private protected. The concrete
			// ImageSourcePaint is present but intentionally internal and is expected to stay so, so
			// this waits on a new contract rather than on a type being opened up. Both are recorded
			// as gaps; the raw imported ViewExtensions.cs that uses them stays uncompiled.
			if (clearWhenNull)
				platformView.UpdateBackgroundColor(NColor.Transparent);
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
		// Core IView operations (ported from ViewExtensions / TransformationExtensions).
		//
		// These exist because MAUI's neutral ViewHandler.ViewMapper casts PlatformView to
		// System.Object on a non-platform TFM and calls the Standard no-op extensions. Chaining
		// that mapper therefore gives NO behaviour for any generic IView property.
		// ---------------------------------------------------------------------------------------

		/// <summary>Applies <see cref="IView.IsEnabled"/>.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateIsEnabled(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			platformView.IsEnabled = view.IsEnabled;
		}

		/// <summary>Applies <see cref="IView.Visibility"/>.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateVisibility(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			if (view.Visibility.ToPlatformVisibility())
			{
				platformView.Show();
				platformView.Layout?.RequestLayout();
			}
			else
			{
				platformView.Hide();
			}
		}

		/// <summary>Maps a MAUI <see cref="Visibility"/> to NUI's boolean visibility.</summary>
		/// <param name="visibility">The visibility.</param>
		/// <returns><see langword="true"/> when the view should be shown.</returns>
		public static bool ToPlatformVisibility(this Visibility visibility) => visibility switch
		{
			Visibility.Hidden => false,
			Visibility.Collapsed => false,
			_ => true,
		};

		/// <summary>Applies <see cref="IView.InputTransparent"/>.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateInputTransparent(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			platformView.Sensitive = !view.InputTransparent;

			// LayoutViewGroup additionally overrides HitTest, which is how MAUI makes a layout
			// itself transparent to input while its children stay hittable.
			if (platformView is TizenLayoutViewGroup layout)
				layout.InputTransparent = view.InputTransparent;
		}

		/// <summary>Applies <see cref="IView.Width"/> / <see cref="IView.Height"/>.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateSize(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			// Ignore the initial unset values; the first layout pass handles them, and the -1
			// default is resolved by GetDesiredSize.
			if (!IsExplicitSet(view.Width) || !IsExplicitSet(view.Height))
				return;

			platformView.Size = new global::Tizen.NUI.Size(
				view.Width.ToScaledPixel(),
				view.Height.ToScaledPixel());
		}

		/// <summary>Applies <see cref="IView.MinimumWidth"/>.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateMinimumWidth(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			// Clearing a minimum has to reset the native constraint, not be ignored. Returning
			// early on an unset value meant the previous minimum stayed applied forever, so a view
			// whose MinimumWidth was set once could never shrink below it again.
			var width = TizenPropertyResolvers.ResolveMinimum(view.MinimumWidth, static v => v.ToScaledPixel());

			platformView.MinimumSize = new Size2D(width, platformView.MinimumSize.Height);
		}

		/// <summary>Applies <see cref="IView.MinimumHeight"/>.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateMinimumHeight(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			var height = TizenPropertyResolvers.ResolveMinimum(view.MinimumHeight, static v => v.ToScaledPixel());

			platformView.MinimumSize = new Size2D(platformView.MinimumSize.Width, height);
		}

		/// <summary>
		/// Applies <see cref="IView.MaximumWidth"/>. Intentionally empty - NUI's MaximumSize does
		/// not behave correctly, and dotnet/maui leaves the same mapper empty for that reason.
		/// </summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateMaximumWidth(this TizenNativeView platformView, IView view)
		{
		}

		/// <summary>
		/// Applies <see cref="IView.MaximumHeight"/>. Intentionally empty - see
		/// <see cref="UpdateMaximumWidth"/>.
		/// </summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateMaximumHeight(this TizenNativeView platformView, IView view)
		{
		}

		/// <summary>Applies translation, scale and rotation together.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateTransformation(this TizenNativeView platformView, IView? view)
		{
			if (platformView is null || view is null)
				return;

			platformView.UpdateTranslation(view);
			platformView.UpdateScale(view);
			platformView.UpdateRotation(view);
		}

		/// <summary>Applies <see cref="IView.TranslationX"/> / <see cref="IView.TranslationY"/>.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateTranslation(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			var location = view.Frame.Location;
			location.X += view.TranslationX;
			location.Y += view.TranslationY;

			platformView.Position = new Position(
				location.X.ToScaledPixel(),
				location.Y.ToScaledPixel());
		}

		/// <summary>Applies <see cref="IView.Scale"/> and its axis components.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateScale(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			platformView.ScaleX = (float)(view.ScaleX * view.Scale);
			platformView.ScaleY = (float)(view.ScaleY * view.Scale);
		}

		/// <summary>Applies rotation and the anchor/pivot point.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateRotation(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			var zRotation = new Rotation(new Radian(DegreeToRadian(view.Rotation)), PositionAxis.Z);
			var xRotation = new Rotation(new Radian(DegreeToRadian(view.RotationX)), PositionAxis.X);
			var yRotation = new Rotation(new Radian(DegreeToRadian(view.RotationY)), PositionAxis.Y);

			platformView.Orientation = zRotation * xRotation * yRotation;
			platformView.PivotPoint = new Position((float)view.AnchorX, (float)view.AnchorY, 0);

			static float DegreeToRadian(double degree) => (float)(degree * Math.PI / 180);
		}

		/// <summary>
		/// Applies <see cref="IView.Clip"/>. Only bounds clipping is available without a container
		/// view; see docs/net11-status.md (G1).
		/// </summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateClip(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			platformView.ClippingMode = view.Clip is null
				? ClippingModeType.Disabled
				: ClippingModeType.ClipToBoundingBox;
		}

		/// <summary>
		/// Applies <see cref="IView.Shadow"/>. dotnet/maui renders shadows through a
		/// <c>WrapperView</c> container, which is unavailable here (G1), so this is a no-op for
		/// ordinary views; <see cref="Label"/> has a native shadow and is handled separately.
		/// </summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateShadow(this TizenNativeView platformView, IView view)
		{
			if (platformView is Label label)
				label.UpdateShadow(view);
		}

		/// <summary>
		/// Applies <see cref="IView.FlowDirection"/>. Not implemented, matching dotnet/maui's
		/// Tizen <c>FlowDirectionExtensions</c>.
		/// </summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateFlowDirection(this TizenNativeView platformView, IView view)
		{
		}

		/// <summary>
		/// Applies <see cref="IView.Semantics"/> to NUI's accessibility properties.
		/// </summary>
		/// <remarks>
		/// An improvement on dotnet/maui, whose Tizen <c>UpdateSemantics</c> is an empty stub even
		/// though TizenFX exposes these properties. MAUI Controls funnels its accessibility surface
		/// (<c>AutomationProperties.Name</c>/<c>HelpText</c>/<c>IsInAccessibleTree</c> and the
		/// heading level) through <see cref="IView.Semantics"/>, so leaving this empty silently
		/// discards every accessibility annotation an app sets.
		/// </remarks>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateSemantics(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			var semantics = view.Semantics;

			if (semantics is null)
			{
				platformView.AccessibilityName = string.Empty;
				platformView.AccessibilityDescription = string.Empty;
				return;
			}

			platformView.AccessibilityName = semantics.Description ?? string.Empty;
			platformView.AccessibilityDescription = semantics.Hint ?? string.Empty;

			// A heading is a navigation landmark, so it must be reachable by the screen reader.
			if (semantics.HeadingLevel != SemanticHeadingLevel.None)
				platformView.AccessibilityHighlightable = true;
		}

		/// <summary>Applies <see cref="IView.AutomationId"/>.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void UpdateAutomationId(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(view);

			if (!string.IsNullOrEmpty(view.AutomationId))
				platformView.Name = view.AutomationId;
		}

		/// <summary>Requests focus for the platform view and completes the request.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="request">The focus request.</param>
		public static void Focus(this TizenNativeView platformView, FocusRequest request)
		{
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(request);

			request.TrySetResult(FocusManager.Instance.SetCurrentFocusView(platformView));
		}

		/// <summary>Clears focus if the platform view currently holds it.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void Unfocus(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			if (FocusManager.Instance.GetCurrentFocusView() == platformView)
				FocusManager.Instance.ClearFocus();
		}

		/// <summary>Propagates an invalidate-measure request to the platform view.</summary>
		/// <param name="platformView">The platform view.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void InvalidateMeasure(this TizenNativeView platformView, IView view)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			// Walk up to the nearest group that caches a measurement and tell it the measurement is
			// stale. MarkChanged alone is not enough: TizenLayoutViewGroup and TizenContentViewGroup
			// keep a _needMeasureUpdate flag, and their measure pass reuses the cached intrinsic
			// size until that flag is set. Marking such a parent merely "changed" re-lays-out the
			// children at the size it had already computed, so a label whose text grew repaints
			// clipped and the containing stack never reflows.
			//
			// The walk starts at the view itself, because a group invalidating its own measure is
			// the common case, and continues through the parents, because leaf natives are not
			// groups at all - TizenLabelView is a NUI TextLabel with a null Layout, which makes
			// RequestLayout() on it a silent no-op.
			for (TizenNativeView? candidate = platformView; candidate is not null; candidate = candidate.GetParent() as TizenNativeView)
			{
				switch (candidate)
				{
					case TizenLayoutViewGroup layoutViewGroup:
						// SetNeedMeasureUpdate already marks changed and requests layout.
						layoutViewGroup.SetNeedMeasureUpdate();
						return;

					case TizenContentViewGroup contentViewGroup:
						contentViewGroup.SetNeedMeasureUpdate();
						return;
				}
			}

			// No measure-caching ancestor. Fall back to the nearest plain group, then to the view's
			// own layout.
			for (TizenNativeView? candidate = platformView; candidate is not null; candidate = candidate.GetParent() as TizenNativeView)
			{
				if (candidate is ViewGroup viewGroup)
				{
					viewGroup.MarkChanged();
					return;
				}
			}

			platformView.Layout?.RequestLayout();
		}

		static bool IsExplicitSet(double value) => !double.IsNaN(value) && value >= 0;

		// ---------------------------------------------------------------------------------------
		// Label (ported from LabelExtensions).
		// ---------------------------------------------------------------------------------------

		/// <summary>
		/// Applies MAUI Controls' accessibility annotations to the platform view.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Takes BOTH annotations, deliberately. They resolve onto the same two NUI flags, so
		/// applying them through separate calls means the later one overwrites the earlier - an
		/// element excluded with its children would silently become reachable again purely because
		/// IsInAccessibleTree happened to be mapped afterwards.
		/// </para>
		/// <para>
		/// The values live on Controls types, so the binding itself is Controls-side - see
		/// <c>TizenControlsMappings</c> in Maui.Tizen.Controls. This is the native half.
		/// </para>
		/// </remarks>
		/// <param name="platformView">The platform view.</param>
		/// <param name="isInAccessibleTree">AutomationProperties.IsInAccessibleTree, if set.</param>
		/// <param name="excludedWithChildren">AutomationProperties.ExcludedWithChildren, if set.</param>
		public static void UpdateAccessibility(
			this TizenNativeView platformView,
			bool? isInAccessibleTree,
			bool? excludedWithChildren)
		{
			ArgumentNullException.ThrowIfNull(platformView);

			var (hidden, highlightable) = TizenPropertyResolvers.ResolveAccessibility(
				isInAccessibleTree, excludedWithChildren);

			platformView.AccessibilityHidden = hidden;
			platformView.AccessibilityHighlightable = highlightable;
		}


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

			// TextDecorations is a [Flags] enum, so Underline|Strikethrough is a legal combination.
			// A switch on the whole value matched neither arm and fell through to None, dropping
			// BOTH decorations. The flag arithmetic lives in TizenPropertyResolvers so it can be
			// tested on the host; this is just the native assignment.
			var resolved = TizenPropertyResolvers.ResolveTextDecorations(label.TextDecorations);

			platformLabel.TextDecorations = (NTextDecorations)resolved;

			// Strikethrough must be applied directly, because the property above will not do it.
			// Tizen.UIExtensions' Label.TextDecorations setter reads ONLY the Underline flag: it
			// stores the value and applies an underline PropertyMap, and the Strikethrough bit is
			// dropped without a word.
			//
			// So computing the combined flags correctly - which is what the earlier fix to
			// ResolveTextDecorations did - still rendered no strikethrough, because the value
			// reached a sink that ignores it. TextLabel.SetStrikethrough is the API that works, and
			// it is available on API15.
			platformLabel.SetStrikethrough(new NStrikethrough
			{
				Enable = (resolved & TizenPropertyResolvers.StrikethroughDecoration) != 0,
			});
		}

		/// <summary>Applies a line break mode to the platform label.</summary>
		/// <remarks>
		/// <para>
		/// <c>LineBreakMode</c> is declared in Microsoft.Maui, so this backend can own the
		/// conversion - but the PROPERTY is <c>Microsoft.Maui.Controls.Label.LineBreakMode</c>.
		/// <see cref="ILabel"/> carries only TextDecorations and LineHeight, so the backend has no
		/// way to read it and the binding stays Controls-owned, exactly as with the accessibility
		/// annotations in gap G10.
		/// </para>
		/// <para>
		/// This is the native half, so whoever owns the Controls binding has something correct to
		/// call. Correct matters here: the two enums are not ordinal-compatible, and casting
		/// between them turns NoWrap into None and shifts everything after it.
		/// </para>
		/// </remarks>
		/// <param name="platformLabel">The platform label.</param>
		/// <param name="lineBreakMode">The cross-platform line break mode.</param>
		public static void UpdateLineBreakMode(this Label platformLabel, LineBreakMode lineBreakMode)
		{
			ArgumentNullException.ThrowIfNull(platformLabel);

			platformLabel.LineBreakMode =
				(NLineBreakMode)TizenPropertyResolvers.ResolveLineBreakMode(lineBreakMode);
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
