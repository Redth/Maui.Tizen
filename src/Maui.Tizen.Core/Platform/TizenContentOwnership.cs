// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal static class TizenContentOwnership
	{
		public static void Replace<TView, THandler>(
			ref TView? currentView,
			ref THandler? currentHandler,
			TView? replacementView,
			THandler? replacementHandler,
			Action<TView> detach,
			Action cancelCallbacks)
			where TView : class
			where THandler : class, IDisposable
		{
			var previousView = currentView;
			var previousHandler = currentHandler;

			// Relinquish ownership before calling external code. Re-entry observes an empty slot
			// and cannot dispose the same child twice.
			currentView = null;
			currentHandler = null;

			try
			{
				TizenCleanup.Run(
					cancelCallbacks,
					() =>
					{
						if (previousView is not null)
							detach(previousView);
					},
					() =>
					{
						if (previousHandler is not null)
							previousHandler.Dispose();
						else
							(previousView as IDisposable)?.Dispose();
					});
			}
			finally
			{
				currentView = replacementView;
				currentHandler = replacementHandler;
			}
		}

		public static void Clear<TView, THandler>(
			ref TView? currentView,
			ref THandler? currentHandler,
			Action<TView> detach,
			Action cancelCallbacks)
			where TView : class
			where THandler : class, IDisposable =>
			Replace(
				ref currentView,
				ref currentHandler,
				replacementView: null,
				replacementHandler: null,
				detach,
				cancelCallbacks);
	}

	internal sealed class TizenCallbackGeneration
	{
		long _generation;

		public long Current => Volatile.Read(ref _generation);

		public long Invalidate() => Interlocked.Increment(ref _generation);

		public bool IsCurrent<T>(long generation, T? expected, T? current)
			where T : class =>
			Current == generation && ReferenceEquals(expected, current);
	}
}
