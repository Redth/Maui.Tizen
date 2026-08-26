// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Helpers Wave B needs that the neutral Microsoft.Maui.Core assembly does not expose publicly, plus
// the small platform helpers whose upstream homes (ViewExtensions.cs, DPExtensions.cs) belong to the
// core slice rather than to this workstream.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;
using NView = Tizen.NUI.BaseComponents.View;
using Point = Microsoft.Maui.Graphics.Point;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Platform helpers used by the Wave B views and handlers.
	/// </summary>
	public static class TizenWaveBViewExtensions
	{
		/// <summary>Converts a MAUI <see cref="Visibility"/> to a native shown/hidden flag.</summary>
		/// <remarks>
		/// Written as an explicit switch to match upstream, so a future <c>Visibility</c> member
		/// defaults to visible here exactly as it does there.
		/// </remarks>
		public static bool ToPlatformVisibility(this Visibility visibility) =>
			visibility switch
			{
				Visibility.Hidden => false,
				Visibility.Collapsed => false,
				_ => true,
			};

		/// <summary>Applies <see cref="IView.Visibility"/> to the native view.</summary>
		public static void UpdateVisibility(this NView platformView, IView view)
		{
			if (view.Visibility.ToPlatformVisibility())
				platformView.Show();
			else
				platformView.Hide();
		}

		/// <summary>Walks up the native parent chain looking for <typeparamref name="T"/>.</summary>
		/// <typeparam name="T">The ancestor type to find.</typeparam>
		/// <remarks>
		/// Checks <paramref name="platformView"/> itself first, matching upstream's internal
		/// <c>ViewExtensions.GetParentOfType</c>. Starting at the parent instead would silently miss
		/// the case where the view already is the type being searched for.
		/// </remarks>
		public static T? GetParentOfType<T>(this NView platformView)
			where T : NView
		{
			if (platformView is T self)
				return self;

			var parent = platformView.GetParent() as NView;

			while (parent is not null)
			{
				if (parent is T found)
					return found;

				parent = parent.GetParent() as NView;
			}

			return null;
		}

		/// <summary>
		/// Applies <see cref="IView.FlowDirection"/> to a graphics view.
		/// </summary>
		/// <remarks>
		/// Mirrors the upstream Tizen behaviour: NUI has no RTL mirroring for a raw drawing surface,
		/// so the flag is recorded on the native view and the drawable is responsible for honouring
		/// it. See docs/wave-b-mapper-parity.md.
		/// </remarks>
		public static void UpdateFlowDirection(this NView platformView, IView view)
		{
			// UNSUPPORTED: TizenFX API15 exposes no per-view layout-direction switch on a raw drawing
			// surface, so there is nothing to push. The drawable receives FlowDirection through the
			// virtual view and is responsible for mirroring. Matches upstream Tizen behaviour.
			_ = platformView;
			_ = view;
		}

		/// <summary>Returns whether any of <paramref name="points"/> falls inside the rectangle.</summary>
		/// <remarks>Upstream used an internal <c>RectF.ContainsAny</c> helper.</remarks>
		public static bool ContainsAny(this RectF rect, PointF[] points) => points.Any(rect.Contains);

		/// <summary>Converts device-independent units to scaled pixels.</summary>
		public static float ToPixel(this double dp) => dp.ToScaledPixel();

		/// <summary>Converts scaled pixels to device-independent units.</summary>
		/// <remarks>
		/// NUI reports geometry as <see cref="float"/>; the core slice only offers int and double
		/// overloads, so this keeps the imported call sites compiling without widening casts.
		/// </remarks>
		public static float ToScaledDP(this float pixel) => (float)((double)pixel).ToScaledDP();

		/// <summary>Converts device-independent units to scaled pixels.</summary>
		
	}

	/// <summary>
	/// Loads an <see cref="IImageSourcePart"/> onto a native Tizen view.
	/// </summary>
	public static class TizenImageSourcePartExtensions
	{
		/// <summary>
		/// Resolves the part's source and hands the resulting resource URL to <paramref name="setImage"/>.
		/// </summary>
		/// <remarks>
		/// Ported from the upstream Tizen <c>ImageSourcePartExtensions</c>, retargeted onto the
		/// Tizen-typed image source contract the core slice owns.
		/// </remarks>
		public static async Task UpdateSourceAsync(
			this IImageSourcePart image,
			NView destinationContext,
			IImageSourceServiceProvider services,
			Action<TizenImageSource?> setImage,
			CancellationToken cancellationToken = default)
		{
			image.UpdateIsLoading(false);

			var imageSource = image.Source;
			if (imageSource is null)
				return;

			var events = image as IImageSourcePartEvents;

			events?.LoadingStarted();
			image.UpdateIsLoading(true);

			try
			{
				var result = await services.GetTizenImageAsync(imageSource, cancellationToken);
				var platformImage = result?.Value;

				// Re-check the source: it can change while the load is in flight.
				var applied = !cancellationToken.IsCancellationRequested
					&& platformImage is not null
					&& imageSource == image.Source;

				if (applied)
					setImage.Invoke(platformImage);

				events?.LoadingCompleted(applied);
			}
			catch (OperationCanceledException)
			{
				events?.LoadingCompleted(false);
			}
			catch (Exception ex)
			{
				events?.LoadingFailed(ex);
			}
			finally
			{
				// Only clear the flag if we are still working on the same image.
				if (imageSource == image.Source)
					image.UpdateIsLoading(false);
			}
		}
	}
}
