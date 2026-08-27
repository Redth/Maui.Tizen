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
// NUI defers decode, so the image view type is needed to await ResourceReady.
using TizenNativeImageView = Tizen.NUI.BaseComponents.ImageView;
using Point = Microsoft.Maui.Graphics.Point;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Platform helpers used by the Wave B views and handlers.
	/// </summary>
	public static class TizenWaveBViewExtensions
	{

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
	/// Applies a resolved image to a native Tizen view.
	/// </summary>
	/// <remarks>
	/// Cancellation, generation tracking and disposal live in <see cref="TizenImageSourceLoader"/>,
	/// which has no NUI dependency and is therefore executable in tests. This type holds only the
	/// part that genuinely requires NUI: waiting for the decode and reporting its outcome.
	/// </remarks>
	public static class TizenImageSourcePartExtensions
	{
		/// <summary>
		/// Hands <paramref name="platformImage"/> to <paramref name="setImage"/> and, for a native
		/// image view, waits until NUI reports the resource ready.
		/// </summary>
		/// <returns>
		/// What the platform actually did. Assigning a resource URL is NOT success: NUI resolves the
		/// URL synchronously and only later reports whether the bytes decoded, so reporting success
		/// on assignment would mark a broken or missing image as loaded.
		/// </returns>
		/// <remarks>
		/// Three departures from upstream, each fixing a hang or a crash rather than changing
		/// behaviour: the wait observes <paramref name="cancellationToken"/> so a superseded or
		/// disconnected load cannot wait forever on an event that will never arrive;
		/// <c>TrySetResult</c> replaces <c>SetResult</c>, which throws if NUI raises
		/// <c>ResourceReady</c> more than once; and nothing is written once the token is cancelled,
		/// so a stale load cannot overwrite a newer one.
		/// </remarks>
		public static async Task<TizenImageApplyResult> ApplyImageSourceAsync(
			this NView destinationContext,
			TizenImageSource? platformImage,
			Func<TizenImageSource?, bool> setImage,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(setImage);

			if (cancellationToken.IsCancellationRequested)
				return TizenImageApplyResult.Cancelled;

			if (platformImage is null)
				return TizenImageApplyResult.Failed;

			if (destinationContext is not TizenNativeImageView imageView)
			{
				// No decode notification is available, so the assignment is all we can report on.
				return setImage(platformImage) ? TizenImageApplyResult.Success : TizenImageApplyResult.Cancelled;
			}

			var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

			void OnResourceReady(object? sender, EventArgs args) =>
				completion.TrySetResult(imageView.LoadingStatus == TizenNativeImageView.LoadingStatusType.Ready);

			using var registration = cancellationToken.Register(static state =>
				((TaskCompletionSource<bool>)state!).TrySetResult(false), completion);

			bool ready;

			try
			{
				imageView.ResourceReady += OnResourceReady;

				// The write is refused when this load no longer owns the view. Returning here
				// rather than awaiting is essential: nothing was assigned, so no ResourceReady
				// would ever arrive and the await would never complete.
				if (!setImage(platformImage))
					return TizenImageApplyResult.Cancelled;

				ready = await completion.Task.ConfigureAwait(false);
			}
			finally
			{
				imageView.ResourceReady -= OnResourceReady;
			}

			if (cancellationToken.IsCancellationRequested)
				return TizenImageApplyResult.Cancelled;

			return ready ? TizenImageApplyResult.Success : TizenImageApplyResult.Failed;
		}
	}
}
