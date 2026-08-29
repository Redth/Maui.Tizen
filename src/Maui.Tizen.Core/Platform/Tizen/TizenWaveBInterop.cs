// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Helpers Wave B needs that the neutral Microsoft.Maui.Core assembly does not expose publicly, plus
// the small platform helpers whose upstream homes (ViewExtensions.cs, DPExtensions.cs) belong to the
// core slice rather than to this workstream.

using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using System;
using System.Threading;
using System.Threading.Tasks;
using Tizen.UIExtensions.Common;
using NView = Tizen.NUI.BaseComponents.View;
using NImageView = Tizen.NUI.BaseComponents.ImageView;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Platform helpers used by the Wave B views and handlers.
	/// </summary>
	public static class TizenWaveBViewExtensions
	{

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

		/// <summary>Converts scaled pixels to device-independent units.</summary>
		/// <remarks>
		/// NUI reports geometry as <see cref="float"/>; the core slice only offers int and double
		/// overloads, so this keeps the imported call sites compiling without widening casts.
		/// </remarks>
		public static float ToScaledDP(this float pixel) => (float)((double)pixel).ToScaledDP();
	}

	internal sealed class TizenTargetImageReadiness : ITizenImageReadinessTarget, IDisposable
	{
		readonly NImageView _target;

		public TizenTargetImageReadiness(NImageView target)
		{
			_target = target;
			_target.ResourceReady += OnResourceReady;
		}

		public event EventHandler? ResourceReady;

		public bool IsReady =>
			_target.LoadingStatus == NImageView.LoadingStatusType.Ready;

		public void Start(string url, bool immediate)
		{
			if (immediate)
				_target.LoadPolicy = global::Tizen.NUI.LoadPolicyType.Immediate;
			_target.ResourceUrl = url;
		}

		void OnResourceReady(object? sender, NImageView.ResourceReadyEventArgs args) =>
			ResourceReady?.Invoke(this, EventArgs.Empty);

		public void Dispose() => _target.ResourceReady -= OnResourceReady;
	}

	internal static class TizenTargetImageExtensions
	{
		public static async Task<bool> ApplyAndWaitForReadyAsync(
			this NImageView target,
			TizenImageSource? image,
			CancellationToken cancellationToken)
		{
			if (image?.ResourceUrl is not { Length: > 0 } url)
			{
				target.ResourceUrl = null;
				return true;
			}

			using var readiness = new TizenTargetImageReadiness(target);
			return await TizenImageReadinessCoordinator
				.WaitAsync(readiness, url, immediate: false, cancellationToken);
		}
	}
}
