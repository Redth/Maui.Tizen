using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Handler contract for <see cref="IApplication"/> on Tizen.</summary>
	/// <remarks>
	/// <para>
	/// This is the ONLY backend-owned handler interface that remains. MAUI Core ships no
	/// <c>IApplicationHandler</c>, so there is nothing to implement instead - verified by
	/// reflection over Microsoft.Maui.dll 11.0.0-preview.7.26426.4.
	/// </para>
	/// <para>
	/// Every other <c>ITizen*Handler</c> was removed. They existed on the false premise that MAUI's
	/// handler interfaces bound <c>PlatformView</c> to a per-TFM alias and so could not be
	/// implemented externally. On the neutral package they declare <c>object PlatformView</c> and
	/// are perfectly implementable; the CS9333 that prompted the workaround came from returning the
	/// concrete platform type from the explicit implementation instead of <c>object</c>. The
	/// handlers now implement <c>ILabelHandler</c>, <c>IContentViewHandler</c>,
	/// <c>IPageHandler</c>, <c>ILayoutHandler</c> and <c>IWindowHandler</c> directly, which is what
	/// lets MAUI Controls' <c>RemapForControls</c> compose with them.
	/// </para>
	/// </remarks>
	public interface ITizenApplicationHandler : IElementHandler
	{
		/// <summary>Gets the cross-platform application.</summary>
		new IApplication VirtualView { get; }

		/// <summary>Gets the platform application.</summary>
		new TizenNativeApplication PlatformView { get; }
	}
}
