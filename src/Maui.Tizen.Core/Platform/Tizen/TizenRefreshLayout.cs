// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.NUI.Binding;
using Tizen.NUI.Components;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using Tizen.UIExtensions.NUI.GraphicsView;
using NRect = Tizen.UIExtensions.Common.Rect;
using NView = Tizen.NUI.BaseComponents.View;
using TColor = Tizen.UIExtensions.Common.Color;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// NUI refresh container with an observable, cancellable pull/reset lifecycle.
	/// </summary>
	/// <remarks>
	/// UIExtensions 0.9.2 keeps its gesture state private and does not reset a cancelled pull.
	/// This wrapper owns the pan detector and animation state so teardown can wait for a causal
	/// terminal transition instead of inferring safety from elapsed frames.
	/// </remarks>
	public class TizenRefreshLayout : ViewGroup
	{
		enum NativeRefreshState
		{
			Idle,
			Pulling,
			Refresh,
			Resetting,
		}

		const float ThresholdDistanceInDp = 70;
		const int MaximumNativeCompletionFrames = 120;
		const int AnimationDuration = 100;

		readonly NView _overlayArea;
		readonly RefreshIcon _refreshIcon;
		readonly PanGestureDetector _panGestureDetector;
		readonly TizenRefreshNativeActivity _nativeActivity = new();
		readonly TizenRefreshTeardownObserver _teardownObserver = new();
		ITizenPlatformViewHandler? _contentHandler;
		TizenNativeView? _contentView;
		TizenNativeView? _nativeContent;
		ScrollableBase? _scrollContent;
		Task _nativeTransition = Task.CompletedTask;
		NativeRefreshState _nativeState;
		long _contentGeneration;
		int _transitionGeneration;
		int _nativeResourcesDisposed;
		float _iconDistance;
		bool _disconnected;

		public TizenRefreshLayout()
		{
			_overlayArea = new NView
			{
				WidthSpecification = LayoutParamPolicies.MatchParent,
				HeightSpecification = LayoutParamPolicies.MatchParent,
			};
			Add(_overlayArea);

			_refreshIcon = new RefreshIcon { Opacity = 0 };
			_overlayArea.Add(_refreshIcon);

			_panGestureDetector = new PanGestureDetector();
			_panGestureDetector.Attach(this);
			_panGestureDetector.Detected += OnPanDetected;
			LayoutUpdated += OnLayout;
		}

		internal event EventHandler? NativePullTerminated;

		/// <summary>Occurs when a pull crosses the refresh threshold.</summary>
		public event EventHandler? Refreshing;

		/// <summary>Gets or sets whether the native refresh indicator is active.</summary>
		public bool IsRefreshing
		{
			get => _nativeState == NativeRefreshState.Refresh;
			set
			{
				if (value)
					RequestRefresh();
				else
					CompleteRefresh();
			}
		}

		/// <summary>Gets or sets the refresh icon foreground color.</summary>
		public TColor IconColor
		{
			get => _refreshIcon.Color;
			set => _refreshIcon.Color = value;
		}

		/// <summary>Gets or sets the refresh icon background color.</summary>
		public TColor IconBackgroundColor
		{
			get => _refreshIcon.BackgroundColor;
			set => _refreshIcon.BackgroundColor = value;
		}

		/// <summary>Gets or sets the native content hosted by the refresh container.</summary>
		public TizenNativeView? Content
		{
			get => _nativeContent;
			set
			{
				if (ReferenceEquals(_nativeContent, value))
					return;

				if (_nativeContent is { } previous)
					Children.Remove(previous);

				_nativeContent = value;
				_scrollContent = null;

				if (value is null)
					return;

				if (!Children.Contains(value))
					Children.Add(value);

				value.LowerBelow(_overlayArea);
				_scrollContent = FindScrollContent(value);
			}
		}

		float ThresholdDistance => ThresholdDistanceInDp * (float)DeviceInfo.ScalingFactor;

		float IconDistance
		{
			get => _iconDistance;
			set
			{
				_iconDistance = value;
				_refreshIcon.PositionY = value;
				_refreshIcon.PullDistance = Math.Min(value / ThresholdDistance, 1);
			}
		}

		public void UpdateContent(IView? content, IMauiContext? mauiContext) =>
			UpdateContent(content, mauiContext, static () => true);

		internal void UpdateContent(IView? content, IMauiContext? mauiContext, Func<bool> isExpected)
		{
			if (_disconnected)
				return;

			var operation = TizenContentOwnership.Reserve(ref _contentGeneration);
			TizenNativeView? replacementView = null;
			ITizenPlatformViewHandler? replacementHandler = null;

			if (content != null && mauiContext != null)
			{
				replacementView = content.ToPlatformView(mauiContext);
				if (content.Handler is ITizenPlatformViewHandler thandler)
					replacementHandler = thandler;
			}

			TizenContentOwnership.Replace(
				operation,
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				replacementView,
				replacementHandler,
				view =>
				{
					if (ReferenceEquals(Content, view))
						Content = null;
					view.Unparent();
				},
				newView => Content = newView,
				static () => { },
				isExpected);
		}

		/// <summary>Disposes the content handler this layout created.</summary>
		public void DisposeContentHandler()
		{
			var operation = TizenContentOwnership.Reserve(ref _contentGeneration);
			TizenContentOwnership.Clear(
				operation,
				ref _contentView,
				ref _contentHandler,
				ref _contentGeneration,
				view =>
				{
					if (ReferenceEquals(Content, view))
						Content = null;
					view.Unparent();
				},
				static () => { },
				static () => true);
		}

		/// <summary>Serialises IsRefreshing around the native completion animation.</summary>
		public TizenRefreshStateMachine RefreshState { get; } = new();

		internal void ApplyRefreshState(bool isRefreshing)
		{
			if (_disconnected)
				return;

			if (isRefreshing)
				_nativeActivity.ObserveRefreshStarted();

			IsRefreshing = isRefreshing;
		}

		internal void MarkDisconnected() => _disconnected = true;

		internal void BeginTeardownObservation() =>
			_teardownObserver.Begin(_nativeState == NativeRefreshState.Pulling);

		internal bool HasPendingNativeActivity =>
			_nativeActivity.HasPendingActivity ||
			_nativeState != NativeRefreshState.Idle ||
			!_nativeTransition.IsCompleted;

		internal void ObserveNativeRefreshStarted() => _nativeActivity.ObserveRefreshStarted();

		internal bool DeferDisableUntilNativePullTerminates() => _nativeActivity.DeferDisable();

		internal void CancelDeferredNativeDisable() => _nativeActivity.CancelDeferredDisable();

		internal Task<bool> WaitForNativeIdleAsync(
			Func<Action, Task> dispatch,
			Func<CancellationToken, Task> nextFrame,
			CancellationToken cancellationToken) =>
			TizenRefreshNativeIdlePoller.WaitAsync(
				() => HasPendingNativeActivity,
				dispatch,
				nextFrame,
				MaximumNativeCompletionFrames,
				cancellationToken);

		internal bool TryDisposeNativeResources()
		{
			if (HasPendingNativeActivity)
				return false;

			if (Interlocked.Exchange(ref _nativeResourcesDisposed, 1) != 0)
				return true;

			_teardownObserver.Complete();
			TizenCleanup.Run(
				() => LayoutUpdated -= OnLayout,
				() => _panGestureDetector.Detected -= OnPanDetected,
				() => _panGestureDetector.Detach(this),
				_panGestureDetector.Dispose,
				base.Dispose);
			return true;
		}

		protected override void OnEnabled(bool enabled)
		{
			base.OnEnabled(enabled);
			if (enabled)
				return;

			if (_nativeState == NativeRefreshState.Pulling)
				CancelPull();
			else
				CompleteRefresh();
		}

		protected override void OnChildAdded(Element child)
		{
			base.OnChildAdded(child);
			if (child is NView view && view != _overlayArea && Content is null)
				Content = view;
		}

		protected override void OnChildRemoved(Element child)
		{
			base.OnChildRemoved(child);
			if (ReferenceEquals(child, Content))
			{
				_nativeContent = null;
				_scrollContent = null;
			}
		}

		void OnPanDetected(object source, PanGestureDetector.DetectedEventArgs e)
		{
			e.Handled = false;

			if (_nativeState == NativeRefreshState.Pulling)
			{
				if (e.PanGesture.State == Gesture.StateType.Finished)
				{
					if (!_teardownObserver.CanProcessTerminal)
						return;

					FinishPull();
					_teardownObserver.TerminalProcessed();
					return;
				}

				if (e.PanGesture.State == Gesture.StateType.Cancelled)
				{
					if (!_teardownObserver.CanProcessTerminal)
						return;

					CancelPull();
					_teardownObserver.TerminalProcessed();
					return;
				}
			}

			if (!IsEnabled || !_teardownObserver.CanStartOrContinue)
				return;

			switch (e.PanGesture.State)
			{
				case Gesture.StateType.Started when _nativeState == NativeRefreshState.Idle && IsTopEdge():
					BeginPull();
					break;
				case Gesture.StateType.Continuing when _nativeState == NativeRefreshState.Pulling:
					MovePull((float)e.PanGesture.Displacement.Y);
					break;
			}
		}

		void BeginPull()
		{
			_nativeState = NativeRefreshState.Pulling;
			_nativeActivity.BeginPull();
			_refreshIcon.IsPulling = true;
			IconDistance = 0;
			TrackTransition(_refreshIcon.AnimationTo(nameof(_refreshIcon.Opacity), 1, AnimationDuration));
		}

		void MovePull(float displacementY)
		{
			var maximumDistance = ThresholdDistance * 1.5f;
			IconDistance = Math.Max(0, Math.Min(maximumDistance, IconDistance + displacementY));
		}

		void FinishPull()
		{
			var applyDisable = _nativeActivity.ReleasePull();
			if (IconDistance >= ThresholdDistance)
			{
				StartRefresh();
				return;
			}

			BeginPullReset();
			if (applyDisable)
				NativePullTerminated?.Invoke(this, EventArgs.Empty);
		}

		void CancelPull()
		{
			var applyDisable = _nativeActivity.ReleasePull();
			BeginPullReset();
			if (applyDisable)
				NativePullTerminated?.Invoke(this, EventArgs.Empty);
		}

		void RequestRefresh()
		{
			switch (_nativeState)
			{
				case NativeRefreshState.Idle:
					IconDistance = ThresholdDistance;
					TrackTransition(Task.WhenAll(
						_refreshIcon.AnimationTo(nameof(_refreshIcon.Opacity), 1, AnimationDuration),
						_refreshIcon.AnimationTo(nameof(_refreshIcon.PositionY), ThresholdDistance, AnimationDuration)));
					StartRefresh(trackPosition: false);
					break;
				case NativeRefreshState.Pulling:
					StartRefresh();
					break;
			}
		}

		void StartRefresh(bool trackPosition = true)
		{
			_transitionGeneration++;
			_nativeState = NativeRefreshState.Refresh;
			_nativeActivity.ObserveRefreshStarted();
			_refreshIcon.IsRunning = true;
			_refreshIcon.IsPulling = false;
			if (trackPosition)
				TrackTransition(_refreshIcon.AnimationTo(
					nameof(_refreshIcon.PositionY),
					ThresholdDistance,
					AnimationDuration));

			Refreshing?.Invoke(this, EventArgs.Empty);
			if (_teardownObserver.ShouldForceCompletion())
				CompleteRefresh();
		}

		void CompleteRefresh()
		{
			if (_nativeState != NativeRefreshState.Refresh)
				return;

			_nativeState = NativeRefreshState.Resetting;
			_nativeActivity.BeginReset();
			_refreshIcon.PullDistance = 0;
			_refreshIcon.IsRunning = false;
			var preceding = _nativeTransition;
			TrackTransition(CompleteRefreshAsync(preceding, ++_transitionGeneration));
		}

		void BeginPullReset()
		{
			_nativeState = NativeRefreshState.Resetting;
			_nativeActivity.BeginReset();
			_refreshIcon.IsPulling = false;
			var preceding = _nativeTransition;
			TrackTransition(ResetPullAsync(preceding, ++_transitionGeneration));
		}

		async Task CompleteRefreshAsync(Task preceding, int generation)
		{
			try
			{
				await preceding;
				await _refreshIcon.AnimationTo(nameof(_refreshIcon.Opacity), 0, AnimationDuration);
				await _refreshIcon.AnimationTo(nameof(_refreshIcon.PositionY), 0, AnimationDuration);
			}
			finally
			{
				CompleteNativeReset(generation);
			}
		}

		async Task ResetPullAsync(Task preceding, int generation)
		{
			try
			{
				await preceding;
				await _refreshIcon.AnimationTo(nameof(_refreshIcon.PositionY), 0, AnimationDuration);
				await _refreshIcon.AnimationTo(nameof(_refreshIcon.Opacity), 0, AnimationDuration);
			}
			finally
			{
				CompleteNativeReset(generation);
			}
		}

		void CompleteNativeReset(int generation)
		{
			if (generation != _transitionGeneration)
				return;

			_nativeState = NativeRefreshState.Idle;
			_nativeActivity.CompleteReset();
			IconDistance = 0;
		}

		void TrackTransition(Task transition)
		{
			_nativeTransition = _nativeTransition.IsCompletedSuccessfully
				? transition
				: Task.WhenAll(_nativeTransition, transition);
			_nativeTransition.FireAndForget();
		}

		bool IsTopEdge() =>
			_scrollContent is not null &&
			_scrollContent.ContentContainer.PositionY == 0;

		static ScrollableBase? FindScrollContent(NView view)
		{
			if (view is ScrollableBase scrollable)
				return scrollable;

			var queue = new Queue<NView>(view.Children);
			while (queue.TryDequeue(out var child))
			{
				if (child is ScrollableBase nested)
					return nested;

				foreach (var descendant in child.Children)
					queue.Enqueue(descendant);
			}

			return null;
		}

		void OnLayout(object? sender, LayoutEventArgs e)
		{
			var bounds = new NRect(0, 0, SizeWidth, SizeHeight);
			_overlayArea.UpdateBounds(bounds);
			_nativeContent?.UpdateBounds(bounds);
			var measured = _refreshIcon.Measure(bounds.Width, bounds.Height);
			_refreshIcon.UpdateBounds(new NRect(
				bounds.Width / 2f - measured.Width / 2f,
				_iconDistance,
				measured.Width,
				measured.Height));
		}

		public void UpdateRefreshColor(IRefreshView view)
		{
			if (!_disconnected)
				IconColor = view.RefreshColor.ToColor()?.ToTizenCommonColor() ?? TColor.Default;
		}

		public void UpdateBackground(IRefreshView view)
		{
			if (!_disconnected)
				IconBackgroundColor = view.Background.ToColor()?.ToTizenCommonColor() ?? TColor.Default;
		}
	}
}
