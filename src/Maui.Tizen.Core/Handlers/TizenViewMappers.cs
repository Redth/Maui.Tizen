using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen-owned base property and command mappers for <see cref="IView"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every handler in this backend chains these instead of MAUI's
	/// <c>ViewHandler.ViewMapper</c> / <c>ViewCommandMapper</c>.
	/// </para>
	/// <para>
	/// This is not a stylistic preference. MAUI's neutral mappers are compiled for a non-platform
	/// target framework, where <c>PlatformView</c> is aliased to <see cref="object"/> and the
	/// <c>Microsoft.Maui.Platform</c> extensions they call are the <c>Standard</c> no-op bodies.
	/// Chaining them yields <b>no behaviour at all</b> for every generic <see cref="IView"/>
	/// property - size, visibility, enabled state, transforms, clip, background - while still
	/// reporting every key as "present". A key-presence test cannot detect that; only a behavioural
	/// one can, which is how the tests for these are written.
	/// </para>
	/// <para>
	/// Ported from the Tizen half of <c>Microsoft.Maui.Handlers.ViewHandler</c>. Mappers that
	/// dotnet/maui leaves deliberately empty on Tizen (maximum size, flow direction, semantics)
	/// are empty here too, and say so.
	/// </para>
	/// </remarks>
	public static class TizenViewMappers
	{
		/// <summary>Base property mapper for <see cref="IView"/> on Tizen.</summary>
		public static readonly IPropertyMapper<IView, IViewHandler> ViewMapper =
			new PropertyMapper<IView, IViewHandler>
			{
				[nameof(IView.AutomationId)] = MapAutomationId,
				[nameof(IView.Clip)] = MapClip,
				[nameof(IView.Shadow)] = MapShadow,
				[nameof(IView.Visibility)] = MapVisibility,
				[nameof(IView.Background)] = MapBackground,
				[nameof(IView.FlowDirection)] = MapFlowDirection,
				[nameof(IView.Width)] = MapWidth,
				[nameof(IView.Height)] = MapHeight,
				[nameof(IView.MinimumWidth)] = MapMinimumWidth,
				[nameof(IView.MinimumHeight)] = MapMinimumHeight,
				[nameof(IView.MaximumWidth)] = MapMaximumWidth,
				[nameof(IView.MaximumHeight)] = MapMaximumHeight,
				[nameof(IView.IsEnabled)] = MapIsEnabled,
				[nameof(IView.Opacity)] = MapOpacity,
				[nameof(IView.Semantics)] = MapSemantics,
				[nameof(IView.TranslationX)] = MapTranslationX,
				[nameof(IView.TranslationY)] = MapTranslationY,
				[nameof(IView.Scale)] = MapScale,
				[nameof(IView.ScaleX)] = MapScaleX,
				[nameof(IView.ScaleY)] = MapScaleY,
				[nameof(IView.Rotation)] = MapRotation,
				[nameof(IView.RotationX)] = MapRotationX,
				[nameof(IView.RotationY)] = MapRotationY,
				[nameof(IView.AnchorX)] = MapAnchorX,
				[nameof(IView.AnchorY)] = MapAnchorY,
				[nameof(IView.InputTransparent)] = MapInputTransparent,
				[nameof(IView.Frame)] = MapFrame,
				[nameof(IToolTipElement.ToolTip)] = MapToolTip,
			};

		/// <summary>Base command mapper for <see cref="IView"/> on Tizen.</summary>
		public static readonly CommandMapper<IView, IViewHandler> ViewCommandMapper =
			new()
			{
				[nameof(IView.InvalidateMeasure)] = MapInvalidateMeasure,
				[nameof(IView.Frame)] = MapFrameCommand,
				[nameof(IView.Focus)] = MapFocus,
				[nameof(IView.Unfocus)] = MapUnfocus,
			};

		static TizenNativeView? Platform(IViewHandler handler) =>
			(handler as IElementHandler)?.PlatformView as TizenNativeView;

		/// <summary>
		/// Records that a mapper key was applied.
		/// </summary>
		/// <remarks>
		/// On the real Tizen target this compiles away entirely. Off-platform it records onto the
		/// host stand-in so the unit tests can assert that a mapper actually <em>ran</em>, rather
		/// than only that its key resolves - the distinction that a no-op neutral mapper hides.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <param name="key">The mapper key.</param>
		static void Applied(IViewHandler handler, string key)
		{
#if !TIZEN
			Platform(handler)?.Record(key);
#endif
		}

		/// <summary>Maps <see cref="IView.AutomationId"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapAutomationId(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.AutomationId));
#if TIZEN
			Platform(handler)?.UpdateAutomationId(view);
#endif
		}

		/// <summary>Maps <see cref="IView.Clip"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapClip(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.Clip));
#if TIZEN
			Platform(handler)?.UpdateClip(view);
#endif
		}

		/// <summary>Maps <see cref="IView.Shadow"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapShadow(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.Shadow));
#if TIZEN
			Platform(handler)?.UpdateShadow(view);
#endif
		}

		/// <summary>Maps <see cref="IView.Visibility"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapVisibility(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.Visibility));
#if TIZEN
			Platform(handler)?.UpdateVisibility(view);
#endif
		}

		/// <summary>Maps <see cref="IView.Background"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapBackground(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.Background));
#if TIZEN
			Platform(handler)?.UpdateBackground(view);
#endif
		}

		/// <summary>Maps <see cref="IView.FlowDirection"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapFlowDirection(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.FlowDirection));
#if TIZEN
			Platform(handler)?.UpdateFlowDirection(view);
#endif
		}

		/// <summary>Maps <see cref="IView.Width"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapWidth(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.Width));
#if TIZEN
			Platform(handler)?.UpdateSize(view);
#endif
		}

		/// <summary>Maps <see cref="IView.Height"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapHeight(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.Height));
#if TIZEN
			Platform(handler)?.UpdateSize(view);
#endif
		}

		/// <summary>Maps <see cref="IView.MinimumWidth"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapMinimumWidth(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.MinimumWidth));
#if TIZEN
			Platform(handler)?.UpdateMinimumWidth(view);
#endif
		}

		/// <summary>Maps <see cref="IView.MinimumHeight"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapMinimumHeight(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.MinimumHeight));
#if TIZEN
			Platform(handler)?.UpdateMinimumHeight(view);
#endif
		}

		/// <summary>
		/// Maps <see cref="IView.MaximumWidth"/>. Empty on purpose - NUI's MaximumSize does not
		/// behave correctly, and dotnet/maui leaves the same mapper empty.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapMaximumWidth(IViewHandler handler, IView view)
		{
		}

		/// <summary>
		/// Maps <see cref="IView.MaximumHeight"/>. Empty on purpose - see
		/// <see cref="MapMaximumWidth"/>.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapMaximumHeight(IViewHandler handler, IView view)
		{
		}

		/// <summary>Maps <see cref="IView.IsEnabled"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapIsEnabled(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.IsEnabled));
#if TIZEN
			Platform(handler)?.UpdateIsEnabled(view);
#endif
		}

		/// <summary>Maps <see cref="IView.Opacity"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapOpacity(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.Opacity));
#if TIZEN
			Platform(handler)?.UpdateOpacity(view);
#endif
		}

		/// <summary>
		/// Maps <see cref="IView.Semantics"/>. Empty on purpose, matching dotnet/maui's Tizen
		/// implementation.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapSemantics(IViewHandler handler, IView view)
		{
		}

		/// <summary>Maps <see cref="IView.TranslationX"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapTranslationX(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.TranslationY"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapTranslationY(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.Scale"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapScale(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.ScaleX"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapScaleX(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.ScaleY"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapScaleY(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.Rotation"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapRotation(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.RotationX"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapRotationX(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.RotationY"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapRotationY(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.AnchorX"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapAnchorX(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.AnchorY"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapAnchorY(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>Maps <see cref="IView.InputTransparent"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapInputTransparent(IViewHandler handler, IView view)
		{
			Applied(handler, nameof(IView.InputTransparent));
#if TIZEN
			Platform(handler)?.UpdateInputTransparent(view);
#endif
		}

		/// <summary>Maps <see cref="IView.Frame"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapFrame(IViewHandler handler, IView view) => UpdateTransformation(handler, view);

		/// <summary>
		/// Maps <see cref="IToolTipElement.ToolTip"/>. Empty on purpose - NUI has no tooltip
		/// primitive, and dotnet/maui's Tizen <c>UpdateToolTip</c> is likewise empty. Present so
		/// the key resolves rather than silently falling through.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="view">The view.</param>
		public static void MapToolTip(IViewHandler handler, IView view)
		{
		}

		static void MapFrameCommand(IViewHandler handler, IView view, object? args) =>
			UpdateTransformation(handler, view);

		static void MapInvalidateMeasure(IViewHandler handler, IView view, object? args)
		{
			Applied(handler, nameof(IView.InvalidateMeasure));
#if TIZEN
			Platform(handler)?.InvalidateMeasure(view);
#endif
		}

		static void MapFocus(IViewHandler handler, IView view, object? args)
		{
			Applied(handler, nameof(IView.Focus));
#if TIZEN
			if (args is FocusRequest request)
				Platform(handler)?.Focus(request);
#endif
		}

		static void MapUnfocus(IViewHandler handler, IView view, object? args)
		{
			Applied(handler, nameof(IView.Unfocus));
#if TIZEN
			Platform(handler)?.Unfocus(view);
#endif
		}

		static void UpdateTransformation(IViewHandler handler, IView view)
		{
			Applied(handler, "Transformation");
#if TIZEN
			Platform(handler)?.UpdateTransformation(view);
#endif
		}
	}
}
