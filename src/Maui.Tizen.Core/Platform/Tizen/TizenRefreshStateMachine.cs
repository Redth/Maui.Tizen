// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// NUI-free so the state machine can be executed on the host lane. The behaviour it encodes is a
// timing race, which is exactly the kind of thing that cannot be verified by inspection.

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>What the caller must do with a refresh request.</summary>
	public enum TizenRefreshAction
	{
		/// <summary>Nothing to do; the native state already matches.</summary>
		None,

		/// <summary>Write the machine's current state to the platform view.</summary>
		Apply,

		/// <summary>
		/// Remembered, but not applied yet: the native control is mid-completion and would drop it.
		/// </summary>
		Defer,
	}

	/// <summary>
	/// Serialises <c>IsRefreshing</c> transitions around the native completion animation.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>Tizen.UIExtensions.NUI.RefreshLayout</c> runs a short completion animation when refreshing
	/// stops, and its <c>RequestRefresh</c> / <c>StartRefresh</c> / <c>CompleteRefresh</c> members
	/// are <b>private</b>, gated on a private <c>_refreshState</c> field. A request to start
	/// refreshing that arrives while that animation is running is therefore silently dropped.
	/// </para>
	/// <para>
	/// That is a real pattern, not a theoretical one: a refresh handler that sets
	/// <c>IsRefreshing = false</c> and immediately starts another refresh — pull to refresh twice
	/// quickly, or a command that re-triggers — loses the second one, and the spinner never
	/// reappears even though the virtual view believes it is refreshing.
	/// </para>
	/// <para>
	/// Private reflection is not an option here (see docs/architecture.md), so the transition is
	/// serialised on this side instead: a start requested during the completion window is held and
	/// replayed once the window closes.
	/// </para>
	/// </remarks>
	public sealed class TizenRefreshStateMachine
	{
		bool _isRefreshing;
		bool _completing;
		bool _pendingStart;

		/// <summary>The state the platform view should be in.</summary>
		public bool IsRefreshing => _isRefreshing;

		/// <summary>Whether the native completion animation is believed to be running.</summary>
		public bool IsCompleting => _completing;

		/// <summary>Whether a start is being held for replay.</summary>
		public bool HasPendingStart => _pendingStart;

		/// <summary>Records a requested state and reports what the caller must do.</summary>
		public TizenRefreshAction Request(bool isRefreshing)
		{
			if (isRefreshing)
			{
				if (_completing)
				{
					// The native control would ignore this. Hold it for CompletionElapsed.
					_pendingStart = true;
					return TizenRefreshAction.Defer;
				}

				if (_isRefreshing)
					return TizenRefreshAction.None;

				_isRefreshing = true;
				return TizenRefreshAction.Apply;
			}

			// A stop supersedes any held start; otherwise the replay would restart a refresh the
			// virtual view has already cancelled.
			_pendingStart = false;

			if (_completing)
				return TizenRefreshAction.None;

			if (!_isRefreshing)
				return TizenRefreshAction.None;

			_isRefreshing = false;
			_completing = true;
			return TizenRefreshAction.Apply;
		}

		/// <summary>Called when the native completion window has elapsed.</summary>
		/// <returns><see cref="TizenRefreshAction.Apply"/> when a held start must now be applied.</returns>
		public TizenRefreshAction CompletionElapsed()
		{
			_completing = false;

			if (!_pendingStart)
				return TizenRefreshAction.None;

			_pendingStart = false;
			_isRefreshing = true;
			return TizenRefreshAction.Apply;
		}

		/// <summary>
		/// Abandons all state without producing any action. Used at teardown.
		/// </summary>
		/// <remarks>
		/// Teardown must not produce an <see cref="TizenRefreshAction.Apply"/>: writing
		/// <c>IsRefreshing</c> starts the native completion animation, whose continuation would then
		/// run against an icon the handler is about to dispose.
		/// </remarks>
		public void Reset()
		{
			_isRefreshing = false;
			_completing = false;
			_pendingStart = false;
		}
	}
}
