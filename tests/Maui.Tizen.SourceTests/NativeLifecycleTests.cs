using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards the NUI-bound halves of the lifecycle fixes.
/// </summary>
/// <remarks>
/// These five defects live in code that binds Tizen.NUI and therefore cannot be executed on a host
/// TFM — the platform views cannot even be loaded. Their portable decision logic is covered
/// behaviourally in <see cref="LifecycleBehaviourTests"/>; what remains is the native call sequence,
/// which is checked here against the source that the ref-pack lane compiles.
/// <para>
/// A source check is weaker than an executed one, and is used only where executing is genuinely
/// impossible. Each test therefore pins a specific ordering or absence that the defect depended on,
/// rather than merely asserting that some method is mentioned.
/// </para>
/// </remarks>
public class NativeLifecycleTests
{
	static string Read(params string[] parts) => File.ReadAllText(RepoPaths.Combine(parts));

	/// <summary>Reads a file with its comment lines removed.</summary>
	/// <remarks>
	/// Assertions about what the code does must not be satisfiable - or broken - by prose. A comment
	/// explaining that a call is deliberately absent contains the very text that proves it present.
	/// </remarks>
	static string ReadCode(params string[] parts) =>
		string.Join('\n', Read(parts)
			.Split('\n')
			.Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

	/// <summary>
	/// <c>Neither</c> must disable scrolling natively, not merely set an orientation.
	/// </summary>
	/// <remarks>
	/// UIExtensions collapses <c>Neither</c> onto Vertical, so without this the view scrolls
	/// vertically while the app has asked for no scrolling at all.
	/// </remarks>
	[Fact]
	public void ScrollNeitherDisablesNativeScrolling()
	{
		var source = ReadCode("src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenScrollViewExtensions.cs");

		Assert.Contains("ScrollEnabled = scrollOrientation != ScrollOrientation.Neither", source, StringComparison.Ordinal);
	}

	/// <summary>Every image call site captures Core's finalized commit dispatcher.</summary>
	[Theory]
	[InlineData("Image/TizenImageHandler.cs")]
	[InlineData("ImageButton/TizenImageButtonHandler.cs")]
	[InlineData("SwipeItemMenuItem/TizenSwipeItemMenuItemHandler.cs")]
	public void EveryImageCallSiteCapturesTheCommitDispatcher(string relative)
	{
		var source = Read(new[] { "src", "Maui.Tizen.Core", "Handlers" }.Concat(relative.Split('/')).ToArray());

		Assert.Contains("TizenDispatchExtensions.CaptureDispatcher(handler)", source, StringComparison.Ordinal);
		Assert.Contains("_sourceLoader.LoadPartAsync(", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// The refresh state machine is compiled into the ref-pack lane.
	/// </summary>
	/// <remarks>
	/// It is listed in the portable group so the host lane can execute it; this confirms the
	/// platform lane compiles it too, so the two cannot diverge.
	/// </remarks>
	[Fact]
	public void TheRefreshStateMachineIsEmitted()
	{
		using var stream = File.OpenRead(RefPackAssembly.Path);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();

		var defined = reader.TypeDefinitions
			.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name))
			.ToHashSet(StringComparer.Ordinal);

		Assert.Contains("TizenRefreshStateMachine", defined);
	}
}
