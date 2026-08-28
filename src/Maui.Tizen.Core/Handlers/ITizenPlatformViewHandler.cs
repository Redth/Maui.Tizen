using System;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen-owned counterpart of the platform-internal <c>Microsoft.Maui.IPlatformViewHandler</c>
	/// contract that dotnet/maui defines inside its own Tizen build.
	/// </summary>
	/// <remarks>
	/// MAUI's <c>IPlatformViewHandler</c> only exists inside the <c>net*-tizen</c> build of
	/// <c>Microsoft.Maui.dll</c>. Re-declaring a type with that exact name in this package would
	/// produce CS0433 for anyone compiling against both assemblies, so this backend owns a
	/// distinctly named interface instead.
	/// </remarks>
	public interface ITizenPlatformViewHandler : IViewHandler, IDisposable
	{
		/// <summary>Gets the strongly typed platform view.</summary>
		new TizenNativeView? PlatformView { get; }

		/// <summary>Gets the strongly typed container view, when one is in use.</summary>
		new TizenNativeView? ContainerView { get; }
	}
}
