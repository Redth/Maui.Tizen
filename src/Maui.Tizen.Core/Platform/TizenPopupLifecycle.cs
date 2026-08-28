// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Owns one active popup and rejects completions from an earlier view lifetime.
	/// </summary>
	internal sealed class TizenPopupLifecycle<TPopup>
		where TPopup : class, IDisposable
	{
		readonly object _gate = new();
		readonly List<Session> _pendingUiCleanup = new();

		Session? _active;
		long _generation;

		public bool IsOpen
		{
			get
			{
				lock (_gate)
					return _active is not null;
			}
		}

		/// <summary>
		/// Opens and owns a popup for the supplied virtual/platform view pair.
		/// </summary>
		public async Task RunAsync<TVirtualView, TPlatformView, TResult>(
			TVirtualView virtualView,
			TPlatformView platformView,
			Func<TVirtualView?> getCurrentVirtualView,
			Func<TPlatformView?> getCurrentPlatformView,
			Func<TVirtualView, bool> isOpenRequested,
			Func<TPopup> createPopup,
			Func<TPopup, CancellationToken, Task<TResult>> openPopup,
			Action<TPopup> closePopup,
			Func<Action, Task> dispatchOnUiThread,
			Action<TVirtualView, TResult> apply,
			Action<TVirtualView> setClosed)
			where TVirtualView : class
			where TPlatformView : class
		{
			ArgumentNullException.ThrowIfNull(virtualView);
			ArgumentNullException.ThrowIfNull(platformView);
			ArgumentNullException.ThrowIfNull(getCurrentVirtualView);
			ArgumentNullException.ThrowIfNull(getCurrentPlatformView);
			ArgumentNullException.ThrowIfNull(isOpenRequested);
			ArgumentNullException.ThrowIfNull(createPopup);
			ArgumentNullException.ThrowIfNull(openPopup);
			ArgumentNullException.ThrowIfNull(closePopup);
			ArgumentNullException.ThrowIfNull(dispatchOnUiThread);
			ArgumentNullException.ThrowIfNull(apply);
			ArgumentNullException.ThrowIfNull(setClosed);

			DrainPendingCleanupOnUiThread();

			Session session;

			lock (_gate)
			{
				if (_active is not null ||
					!ReferenceEquals(getCurrentVirtualView(), virtualView) ||
					!ReferenceEquals(getCurrentPlatformView(), platformView) ||
					!isOpenRequested(virtualView))
				{
					return;
				}

				session = new Session(
					createPopup(),
					new CancellationTokenSource(),
					++_generation,
					dispatchOnUiThread,
					closePopup,
					() => setClosed(virtualView),
					SynchronizationContext.Current,
					Environment.CurrentManagedThreadId);
				_active = session;
			}

			var errors = new List<Exception>();
			var completed = false;
			TResult result = default!;

			try
			{
				result = await openPopup(session.Popup, session.Cancellation.Token);
				completed = true;
			}
			catch (OperationCanceledException)
			{
				// User dismissal and programmatic close both end the same active generation.
			}
			catch (Exception exception)
			{
				TizenCleanup.Add(errors, exception);
			}

			if (completed)
			{
				try
				{
					await session.DispatchOnUiThread(() =>
					{
						lock (_gate)
						{
							if (!IsCurrent(session) ||
								!ReferenceEquals(getCurrentVirtualView(), virtualView) ||
								!ReferenceEquals(getCurrentPlatformView(), platformView) ||
								!isOpenRequested(virtualView))
							{
								return;
							}

							apply(virtualView, result);
						}
					});
				}
				catch (Exception exception)
				{
					TizenCleanup.Add(errors, exception);
				}
			}

			foreach (var exception in await FinalizeAsync(session))
				TizenCleanup.Add(errors, exception);

			TizenCleanup.ThrowIfAny(errors);
		}

		/// <summary>
		/// Programmatically closes the active generation through its captured UI dispatcher.
		/// </summary>
		public async Task CancelAsync()
		{
			Session? session;

			lock (_gate)
				session = _active;

			if (session is null)
				return;

			var errors = await FinalizeAsync(session);
			TizenCleanup.ThrowIfAny(errors);
		}

		/// <summary>
		/// Closes from a handler's UI-thread disconnect path, even after an earlier dispatch failed.
		/// </summary>
		public void CancelOnUiThread()
		{
			var errors = new List<Exception>();
			DrainPendingCleanupOnUiThread(errors);

			Session? session;

			lock (_gate)
			{
				session = _active;

				if (session is not null)
					Detach(session);
			}

			if (session is not null)
			{
				Try(errors, session.Cancellation.Cancel);
				Try(errors, () => CleanupOnUiThread(session));
			}

			TizenCleanup.ThrowIfAny(errors);
		}

		async Task<IReadOnlyList<Exception>> FinalizeAsync(Session session)
		{
			var errors = new List<Exception>();

			lock (_gate)
			{
				if (!IsCurrent(session))
					return errors;

				Detach(session);
			}

			Try(errors, session.Cancellation.Cancel);

			try
			{
				await session.DispatchOnUiThread(() => CleanupOnUiThread(session));
			}
			catch (Exception exception)
			{
				TizenCleanup.Add(errors, exception);
			}

			if (Volatile.Read(ref session.CleanupStarted) == 0)
			{
				if (IsOnOpeningThread(session))
					Try(errors, () => CleanupOnUiThread(session));
				else
					QueuePendingCleanup(session);
			}

			return errors;
		}

		bool IsCurrent(Session session) =>
			ReferenceEquals(_active, session) &&
			_generation == session.Generation &&
			!session.Cancellation.IsCancellationRequested;

		void Detach(Session session)
		{
			_active = null;
			_generation++;
		}

		void CleanupOnUiThread(Session session)
		{
			if (Interlocked.Exchange(ref session.CleanupStarted, 1) != 0)
				return;

			TizenCleanup.Run(
				() => session.ClosePopup(session.Popup),
				session.Popup.Dispose,
				session.Cancellation.Dispose,
				session.SetClosed);
		}

		void QueuePendingCleanup(Session session)
		{
			lock (_gate)
				_pendingUiCleanup.Add(session);
		}

		void DrainPendingCleanupOnUiThread()
		{
			var errors = new List<Exception>();
			DrainPendingCleanupOnUiThread(errors);
			TizenCleanup.ThrowIfAny(errors);
		}

		void DrainPendingCleanupOnUiThread(ICollection<Exception> errors)
		{
			Session[] pending;

			lock (_gate)
			{
				pending = _pendingUiCleanup.ToArray();
				_pendingUiCleanup.Clear();
			}

			foreach (var session in pending)
				Try(errors, () => CleanupOnUiThread(session));
		}

		static bool IsOnOpeningThread(Session session) =>
			Environment.CurrentManagedThreadId == session.OpeningThreadId ||
			(session.OpeningContext is not null &&
				ReferenceEquals(SynchronizationContext.Current, session.OpeningContext));

		static void Try(ICollection<Exception> errors, Action action)
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

		sealed class Session
		{
			public Session(
				TPopup popup,
				CancellationTokenSource cancellation,
				long generation,
				Func<Action, Task> dispatchOnUiThread,
				Action<TPopup> closePopup,
				Action setClosed,
				SynchronizationContext? openingContext,
				int openingThreadId)
			{
				Popup = popup;
				Cancellation = cancellation;
				Generation = generation;
				DispatchOnUiThread = dispatchOnUiThread;
				ClosePopup = closePopup;
				SetClosed = setClosed;
				OpeningContext = openingContext;
				OpeningThreadId = openingThreadId;
			}

			public TPopup Popup { get; }

			public CancellationTokenSource Cancellation { get; }

			public long Generation { get; }

			public Func<Action, Task> DispatchOnUiThread { get; }

			public Action<TPopup> ClosePopup { get; }

			public Action SetClosed { get; }

			public SynchronizationContext? OpeningContext { get; }

			public int OpeningThreadId { get; }

			public int CleanupStarted;
		}
	}
}
