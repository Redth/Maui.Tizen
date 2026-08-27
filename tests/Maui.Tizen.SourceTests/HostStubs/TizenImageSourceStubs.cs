using System.IO;

namespace Microsoft.Maui.Platforms.Tizen;

/// <summary>
/// Host-buildable stand-in for the real <c>TizenImageSource</c>.
/// </summary>
/// <remarks>
/// <para>
/// The real type wraps <c>Tizen.NUI.ImageUrl</c> and <c>Tizen.NUI.EncodedImageBuffer</c>, so it can
/// only be compiled where TizenFX is available. <see cref="TizenImageSourceLoader"/> never inspects
/// an image — it only decides <em>whether</em> one should be applied — so a payload stand-in is
/// enough to execute every cancellation path.
/// </para>
/// <para>
/// Follows the same convention as <c>tests/Maui.Tizen.Core.UnitTests/HostStubs</c>: the stub carries
/// no behaviour, and the real type is compiled by the ref-pack lane instead.
/// </para>
/// </remarks>
public class TizenImageSource : IDisposable
{
	/// <summary>Gets or sets the NUI resource URL.</summary>
	public string? ResourceUrl { get; set; }

	/// <summary>Gets a value indicating whether <see cref="Dispose()"/> has been called.</summary>
	public bool IsDisposed { get; private set; }

	/// <summary>Stand-in for the real stream decode.</summary>
	public Task LoadSource(Stream stream) => Task.CompletedTask;

	/// <inheritdoc />
	public void Dispose() => IsDisposed = true;
}

/// <summary>Host-buildable stand-in for the real image source service result.</summary>
public sealed class TizenImageSourceServiceResult : IImageSourceServiceResult<TizenImageSource>
{
	readonly Action? _dispose;

	/// <summary>Initializes a new instance of the <see cref="TizenImageSourceServiceResult"/> class.</summary>
	public TizenImageSourceServiceResult(TizenImageSource value, Action? dispose = null)
	{
		Value = value;
		_dispose = dispose;
	}

	/// <inheritdoc />
	public TizenImageSource Value { get; }

	/// <inheritdoc />
	public bool IsResolutionDependent => false;

	/// <inheritdoc />
	public bool IsDisposed { get; private set; }

	/// <inheritdoc />
	public void Dispose()
	{
		if (IsDisposed)
			return;

		IsDisposed = true;
		_dispose?.Invoke();
	}
}
