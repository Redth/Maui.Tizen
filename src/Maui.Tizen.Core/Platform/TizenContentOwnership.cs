// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal static class TizenContentOwnership
	{
		public static long Reserve(ref long generation) => Interlocked.Increment(ref generation);

		public static bool Replace<TView, THandler>(
			long operation,
			ref TView? currentView,
			ref THandler? currentHandler,
			ref long generation,
			TView? replacementView,
			THandler? replacementHandler,
			Action<TView> detach,
			Action<TView> attach,
			Action cancelCallbacks,
			Func<bool> isExpected)
			where TView : class
			where THandler : class, IDisposable
		{
			ArgumentNullException.ThrowIfNull(isExpected);

			if (Volatile.Read(ref generation) != operation || !isExpected())
			{
				DisposePreparedReplacement(currentView, currentHandler, replacementView, replacementHandler);
				return false;
			}

			if (ReferenceEquals(currentView, replacementView)
				&& ReferenceEquals(currentHandler, replacementHandler))
				return false;

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

			var installed = Volatile.Read(ref generation) == operation && isExpected();
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

		static void DisposePreparedReplacement<TView, THandler>(
			TView? currentView,
			THandler? currentHandler,
			TView? replacementView,
			THandler? replacementHandler)
			where TView : class
			where THandler : class, IDisposable
		{
			if (ReferenceEquals(currentView, replacementView)
				&& ReferenceEquals(currentHandler, replacementHandler))
				return;

			if (replacementHandler is not null)
				replacementHandler.Dispose();
			else
				(replacementView as IDisposable)?.Dispose();
		}

		public static bool Clear<TView, THandler>(
			long operation,
			ref TView? currentView,
			ref THandler? currentHandler,
			ref long generation,
			Action<TView> detach,
			Action cancelCallbacks,
			Func<bool> isExpected)
			where TView : class
			where THandler : class, IDisposable =>
			Replace(
				operation,
				ref currentView,
				ref currentHandler,
				ref generation,
				replacementView: null,
				replacementHandler: null,
				detach,
				static _ => { },
				cancelCallbacks,
				isExpected);
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

	internal sealed class TizenDisconnectingState
	{
		int _disconnecting;

		public bool IsDisconnecting => Volatile.Read(ref _disconnecting) != 0;

		public void Connected() => Volatile.Write(ref _disconnecting, 0);

		public void BeginDisconnect() => Interlocked.Exchange(ref _disconnecting, 1);
	}
}
