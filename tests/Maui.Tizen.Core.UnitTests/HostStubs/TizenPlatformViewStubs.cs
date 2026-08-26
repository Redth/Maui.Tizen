using System;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Host-buildable stand-in for <c>Tizen.NUI.BaseComponents.View</c>.
	/// </summary>
	/// <remarks>
	/// The <c>Tizen.NET</c> NuGet package only ships MSBuild props/targets - the real
	/// <c>Tizen.NUI</c> reference assemblies are delivered by the Samsung Tizen workload packs.
	/// These stand-ins let the whole handler surface (mappers, command mappers, hosting and DI
	/// registration) compile and be unit tested on a machine without that workload.
	/// <para>
	/// They intentionally have no behaviour. Every mapper body that touches NUI is guarded by
	/// <c>#if TIZEN</c>, so running against these types is a no-op rather than a crash.
	/// </para>
	/// </remarks>
	public class TizenPlatformView : IDisposable
	{
		bool _disposed;

		/// <summary>Gets a value indicating whether <see cref="Dispose()"/> has been called.</summary>
		public bool IsDisposed => _disposed;

		/// <inheritdoc />
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>Releases resources held by this stand-in.</summary>
		/// <param name="disposing">Whether managed resources should be released.</param>
		protected virtual void Dispose(bool disposing) => _disposed = true;
	}

	/// <summary>Host-buildable stand-in for <c>Tizen.UIExtensions.NUI.Label</c>.</summary>
	public class TizenLabelView : TizenPlatformView
	{
		/// <summary>Gets or sets the rendered text.</summary>
		public string Text { get; set; } = string.Empty;
	}

	/// <summary>Host-buildable stand-in for <c>Tizen.NUI.Window</c>.</summary>
	public class TizenPlatformWindow : TizenPlatformView
	{
	}

	/// <summary>Host-buildable stand-in for <c>Tizen.Applications.CoreApplication</c>.</summary>
	public class TizenPlatformApplication : TizenPlatformView
	{
	}
}
