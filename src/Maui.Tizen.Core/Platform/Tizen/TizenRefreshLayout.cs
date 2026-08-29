// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/MauiRefreshLayout.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using System;
using Microsoft.Maui.Graphics;
using Tizen.UIExtensions.NUI;
using TColor = Tizen.UIExtensions.Common.Color;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;
using Color = Microsoft.Maui.Graphics.Color;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenRefreshLayout : RefreshLayout
	{
		ITizenPlatformViewHandler? _contentHandler;
		TizenNativeView? _contentView;
		long _contentGeneration;
		bool _disconnected;
		const int MaximumNativeCompletionFrames = 120;

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
		/// <remarks>
		/// The layout creates the child handler in <c>UpdateContent</c>, so it owns it. Without this
		/// the child handler outlives its parent and keeps the native content view alive.
		/// </remarks>
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

		/// <summary>Serialises IsRefreshing around the base class's private completion animation.</summary>
		public TizenRefreshStateMachine RefreshState { get; } = new();

		/// <summary>Applies a coordinator-approved state to the native layout.</summary>
		internal void ApplyRefreshState(bool isRefreshing)
		{
			if (_disconnected)
				return;

			IsRefreshing = isRefreshing;
		}

		/// <summary>Prevents any late coordinator callback from touching this layout.</summary>
		internal void MarkDisconnected() => _disconnected = true;

		internal Task<bool> WaitForNativeIdleAsync(
			Func<Action, Task> dispatch,
			Func<CancellationToken, Task> nextFrame,
			CancellationToken cancellationToken) =>
			TizenRefreshNativeIdlePoller.WaitAsync(
				() => IsRefreshing,
				dispatch,
				nextFrame,
				MaximumNativeCompletionFrames,
				cancellationToken);

		public void UpdateRefreshColor(IRefreshView view)
		{
			if (_disconnected)
				return;

			IconColor = view.RefreshColor.ToColor()?.ToTizenCommonColor() ?? TColor.Default;
		}

		public void UpdateBackground(IRefreshView view)
		{
			if (_disconnected)
				return;

			IconBackgroundColor = view.Background.ToColor()?.ToTizenCommonColor() ?? TColor.Default;
		}
	}
}
