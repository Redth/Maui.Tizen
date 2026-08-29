// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal static class TizenContentOwnership
	{
		public static bool Replace<TView, THandler>(
			ref TView? currentView,
			ref THandler? currentHandler,
			ref long generation,
			TView? replacementView,
			THandler? replacementHandler,
			Action<TView> detach,
			Action<TView> attach,
			Action cancelCallbacks)
			where TView : class
			where THandler : class, IDisposable
		{
			if (ReferenceEquals(currentView, replacementView)
				&& ReferenceEquals(currentHandler, replacementHandler))
				return false;

			var operation = Interlocked.Increment(ref generation);
			var previousView = currentView;
			var previousHandler = currentHandler;
			var errors = new List<Exception>();

			// Relinquish ownership before calling external code. Re-entry observes an empty slot
			// and cannot dispose the same child twice.
			currentView = null;
			currentHandler = null;

			void Try(Action action)
			{
				try
				{
					action();
				}
				catch (Exception exception)
				{
					TizenCleanup.Add(errors, exception);
				}
			}

			Try(cancelCallbacks);
			if (previousView is not null)
				Try(() => detach(previousView));

			if (previousHandler is not null)
				Try(previousHandler.Dispose);
			else if (previousView is IDisposable disposableView)
				Try(disposableView.Dispose);

			var installed = Volatile.Read(ref generation) == operation;
			if (installed)
			{
				currentView = replacementView;
				currentHandler = replacementHandler;

				if (replacementView is not null)
					Try(() => attach(replacementView));
			}
			else if (!ReferenceEquals(currentView, replacementView)
				|| !ReferenceEquals(currentHandler, replacementHandler))
			{
				if (replacementHandler is not null)
					Try(replacementHandler.Dispose);
				else if (replacementView is IDisposable disposableReplacement)
					Try(disposableReplacement.Dispose);
			}

			TizenCleanup.ThrowIfAny(errors);
			return installed;
		}

		public static bool Clear<TView, THandler>(
			ref TView? currentView,
			ref THandler? currentHandler,
			ref long generation,
			Action<TView> detach,
			Action cancelCallbacks)
			where TView : class
			where THandler : class, IDisposable =>
			Replace(
				ref currentView,
				ref currentHandler,
				ref generation,
				replacementView: null,
				replacementHandler: null,
				detach,
				static _ => { },
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
