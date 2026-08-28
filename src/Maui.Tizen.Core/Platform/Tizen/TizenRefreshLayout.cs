// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/MauiRefreshLayout.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using Microsoft.Maui.Graphics;
using Tizen.UIExtensions.NUI;
using TColor = Tizen.UIExtensions.Common.Color;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;
using Color = Microsoft.Maui.Graphics.Color;

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenRefreshLayout : RefreshLayout
	{
		ITizenPlatformViewHandler? _contentHandler;

		public void UpdateContent(IView? content, IMauiContext? mauiContext)
		{
			Content = null;
			_contentHandler?.Dispose();
			_contentHandler = null;

			if (content != null && mauiContext != null)
			{
				var contentView = content.ToPlatformView(mauiContext);
				if (content.Handler is ITizenPlatformViewHandler thandler)
				{
					_contentHandler = thandler;
				}
				Content = contentView;
			}
		}

		/// <summary>Disposes the content handler this layout created.</summary>
		/// <remarks>
		/// The layout creates the child handler in <c>UpdateContent</c>, so it owns it. Without this
		/// the child handler outlives its parent and keeps the native content view alive.
		/// </remarks>
		public void DisposeContentHandler()
		{
			_contentHandler?.Dispose();
			_contentHandler = null;
		}

		/// <summary>Serialises IsRefreshing around the base class's private completion animation.</summary>
		public TizenRefreshStateMachine RefreshState { get; } = new();

		/// <summary>
		/// How long the native completion animation runs in Tizen.UIExtensions.NUI 0.9.2.
		/// </summary>
		/// <remarks>
		/// The base class exposes no completion event and its state members are private, so the
		/// window has to be waited out rather than observed. Deliberately a little longer than the
		/// animation itself: replaying too early is silently dropped, which is the bug being fixed.
		/// </remarks>
		public const int CompletionWindowMilliseconds = 150;

		/// <summary>Applies the machine's state to the platform view.</summary>
		public void ApplyRefreshState() => IsRefreshing = RefreshState.IsRefreshing;

		public void UpdateIsRefreshing(IRefreshView view) => UpdateIsRefreshing(view.IsRefreshing);

		/// <summary>Requests a refresh state, returning what the caller must schedule.</summary>
		public TizenRefreshAction UpdateIsRefreshing(bool isRefreshing)
		{
			var action = RefreshState.Request(isRefreshing);

			if (action == TizenRefreshAction.Apply)
				ApplyRefreshState();

			return action;
		}

		public void UpdateRefreshColor(IRefreshView view)
		{
			IconColor = view.RefreshColor.ToColor()?.ToTizenCommonColor() ?? TColor.Default;
		}

		public void UpdateBackground(IRefreshView view)
		{
			IconBackgroundColor = view.Background.ToColor()?.ToTizenCommonColor() ?? TColor.Default;
		}
	}
}
