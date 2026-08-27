using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen
{
	// ---------------------------------------------------------------------------------------
	// PROVISIONAL CONTRACTS - delete this file when dotnet/maui#37853 ships.
	//
	// dotnet/maui#37853 ("Add public modal navigation extensibility seam for external platform
	// backends") adds IModalNavigationPlatform, IModalNavigationPlatformFactory and
	// IModalNavigationHost to Microsoft.Maui.Controls.Platform. It is still OPEN, so those types
	// are not in the 11.0.0-preview.7 package this repository builds against and cannot be
	// implemented yet.
	//
	// The member shapes below are copied verbatim from that PR so the Tizen implementation is
	// written against the final contract today. Adopting the real interfaces is then a namespace
	// change on TizenModalNavigationPlatform / TizenModalNavigationPlatformFactory plus deleting
	// this file - no logic moves.
	//
	// They are declared in Microsoft.Maui.Platforms.Tizen, NOT in Microsoft.Maui.Controls.Platform.
	// Re-declaring a MAUI type name in a MAUI namespace would collide (CS0433) for any consumer
	// that also references MAUI's own build once the PR lands. See docs/architecture.md.
	//
	// ProvisionalModalNavigationContractTests asserts that these shapes still match the PR and
	// fails once the real types appear in Microsoft.Maui.Controls, so this file cannot rot
	// silently or outlive its purpose.
	// ---------------------------------------------------------------------------------------

	/// <summary>
	/// Provisional stand-in for <c>Microsoft.Maui.Controls.Platform.IModalNavigationPlatform</c>
	/// from dotnet/maui#37853. Presents and dismisses modal pages for a single
	/// <see cref="Window"/>.
	/// </summary>
	/// <remarks>
	/// The framework creates one instance per window and disposes it when the window is destroyed
	/// or when the window's handler changes, so implementations must tolerate multiple
	/// create/dispose cycles and must not be shared between windows. All members are invoked on
	/// the UI thread.
	/// </remarks>
	public interface IModalNavigationPlatform : IDisposable
	{
		/// <summary>
		/// Gets a value indicating whether the backend can present or dismiss a modal right now.
		/// </summary>
		/// <remarks>
		/// While this returns <see langword="false"/> the framework still records pushes and pops
		/// on the cross-platform modal stack, but does not call <see cref="PushModalAsync"/> or
		/// <see cref="PopModalAsync"/>. A backend that defers readiness must call
		/// <see cref="IModalNavigationHost.RequestSync"/> when it becomes ready, because the
		/// framework does not poll.
		/// </remarks>
		bool IsReady { get; }

		/// <summary>
		/// Presents <paramref name="modal"/> on top of
		/// <see cref="IModalNavigationHost.CurrentPlatformPage"/>.
		/// </summary>
		/// <param name="modal">
		/// The page to present. It has already been added to
		/// <see cref="IModalNavigationHost.PlatformModalStack"/> when this method is called.
		/// </param>
		/// <param name="animated"><see langword="true"/> to animate the transition.</param>
		/// <returns>A task that completes once the modal is on screen and safe to dismiss.</returns>
		Task PushModalAsync(Page modal, bool animated);

		/// <summary>
		/// Dismisses <paramref name="modal"/>, revealing
		/// <see cref="IModalNavigationHost.CurrentPlatformPage"/>.
		/// </summary>
		/// <param name="modal">
		/// The page to dismiss. It has already been removed from
		/// <see cref="IModalNavigationHost.PlatformModalStack"/> when this method is called.
		/// </param>
		/// <param name="animated"><see langword="true"/> to animate the transition.</param>
		/// <returns>A task that completes once the modal is off screen.</returns>
		Task PopModalAsync(Page modal, bool animated);

		/// <summary>
		/// Called when the window's page gets a handler, including when the page is replaced.
		/// </summary>
		/// <remarks>May be called multiple times for the same window.</remarks>
		void PageAttached();
	}

	/// <summary>
	/// Provisional stand-in for
	/// <c>Microsoft.Maui.Controls.Platform.IModalNavigationPlatformFactory</c> from
	/// dotnet/maui#37853.
	/// </summary>
	public interface IModalNavigationPlatformFactory
	{
		/// <summary>
		/// Creates the <see cref="IModalNavigationPlatform"/> for a window.
		/// </summary>
		/// <param name="host">The per-window host exposing the framework's modal navigation state.</param>
		/// <returns>
		/// A new instance owned and disposed by the framework, or <see langword="null"/> to let the
		/// window keep the built-in platform implementation.
		/// </returns>
		IModalNavigationPlatform? CreateModalNavigationPlatform(IModalNavigationHost host);
	}

	/// <summary>
	/// Provisional stand-in for <c>Microsoft.Maui.Controls.Platform.IModalNavigationHost</c> from
	/// dotnet/maui#37853. Exposes the cross-platform modal navigation state that an
	/// <see cref="IModalNavigationPlatform"/> needs.
	/// </summary>
	/// <remarks>
	/// The framework implements this. It owns the cross-platform modal stack, the page lifecycle
	/// events, the window modal events and the reconciliation loop; a platform implementation is
	/// only responsible for the visual presentation of a single push or pop.
	/// </remarks>
	public interface IModalNavigationHost
	{
		/// <summary>Gets the window that owns this modal navigation host.</summary>
		Window Window { get; }

		/// <summary>Gets the <see cref="IMauiContext"/> scoped to <see cref="Window"/>.</summary>
		IMauiContext MauiContext { get; }

		/// <summary>
		/// Gets the modal pages the platform has actually presented, in push order.
		/// </summary>
		IReadOnlyList<Page> PlatformModalStack { get; }

		/// <summary>
		/// Gets the page the user is expected to be looking at once the cross-platform modal stack
		/// is fully applied.
		/// </summary>
		Page? CurrentPage { get; }

		/// <summary>
		/// Gets the page currently hosting content on the platform: the topmost presented modal, or
		/// the window's page when none has been presented.
		/// </summary>
		Page CurrentPlatformPage { get; }

		/// <summary>
		/// Gets a value indicating whether the framework considers the window ready to present modals.
		/// </summary>
		bool IsWindowReady { get; }

		/// <summary>
		/// Gets a value indicating whether several modals are being presented as a single batch.
		/// </summary>
		bool IsBatchPushing { get; }

		/// <summary>
		/// Asks the framework to re-run the reconciliation loop between the requested modal stack
		/// and <see cref="PlatformModalStack"/>.
		/// </summary>
		void RequestSync();
	}
}
