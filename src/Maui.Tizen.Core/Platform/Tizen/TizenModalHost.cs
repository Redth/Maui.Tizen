// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Presents a modal popup on behalf of a handler.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>Picker</c>, <c>DatePicker</c> and <c>TimePicker</c> all open a dialog. Upstream this
	/// goes through the window's modal navigation stack, so the popup participates in back
	/// navigation and is torn down when the page it belongs to goes away.
	/// </para>
	/// <para>
	/// That stack is owned by the navigation/window workstream, not by Wave A. This interface is
	/// the seam between the two: Wave A opens popups through it, and the navigation wave
	/// registers an implementation that pushes onto the real modal stack. Until then
	/// <see cref="TizenDirectModalHost"/> keeps the pickers functional.
	/// </para>
	/// </remarks>
	public interface ITizenModalHost
	{
		/// <summary>
		/// Runs <paramref name="showPopup"/> as a modal interaction.
		/// </summary>
		/// <param name="showPopup">
		/// Opens the popup and completes when the user has accepted or dismissed it.
		/// </param>
		Task RunModalAsync(Func<Task> showPopup);
	}

	/// <summary>
	/// Opens popups directly onto the current NUI window.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The fallback used when no navigation-aware host is registered. NUI popups attach
	/// themselves to the current window, so this is sufficient to display and dismiss one.
	/// </para>
	/// <para>
	/// What it does not provide is modal-stack integration: a popup opened this way is not on
	/// the back stack, so a hardware back press dismisses the page underneath rather than the
	/// popup. That is the reason the seam exists.
	/// </para>
	/// </remarks>
	public sealed class TizenDirectModalHost : ITizenModalHost
	{
		/// <summary>The shared instance used when nothing is registered in DI.</summary>
		public static readonly ITizenModalHost Instance = new TizenDirectModalHost();

		public Task RunModalAsync(Func<Task> showPopup)
		{
			ArgumentNullException.ThrowIfNull(showPopup);
			return showPopup();
		}
	}

	/// <summary>
	/// Resolves the modal host for a handler.
	/// </summary>
	public static class TizenModalHostExtensions
	{
		/// <summary>
		/// Returns the registered <see cref="ITizenModalHost"/>, or the direct-window fallback.
		/// </summary>
		public static ITizenModalHost GetModalHost(this IElementHandler handler) =>
			handler.MauiContext?.Services?.GetService(typeof(ITizenModalHost)) as ITizenModalHost
			?? TizenDirectModalHost.Instance;
	}
}
