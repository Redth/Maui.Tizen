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

	static string SwipeGroup => ReadCode("src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenSwipeViewGroup.cs");

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

	/// <summary>
	/// The swipe content view must not be disposed directly when a handler owns it.
	/// </summary>
	/// <remarks>
	/// <c>UpdateContent</c> used to call <c>_contentView.Dispose()</c> and then dispose the handler
	/// that created that same view, double-disposing the native object.
	/// </remarks>
	[Fact]
	public void SwipeContentIsNotDoubleDisposed()
	{
		var source = SwipeGroup;

		// The direct dispose is now reachable only on the no-handler branch.
		Assert.DoesNotContain("_contentView?.Dispose();", source, StringComparison.Ordinal);
		Assert.DoesNotContain("_contentView.Dispose();", source, StringComparison.Ordinal);

		Assert.Contains("previousHandler.Dispose();", source, StringComparison.Ordinal);
		Assert.Contains("previousView?.Dispose();", source, StringComparison.Ordinal);
	}

	/// <summary>The old view is unparented before anything disposes it.</summary>
	[Fact]
	public void SwipeContentIsUnparentedBeforeDisposal()
	{
		var source = SwipeGroup;

		var unparent = source.IndexOf("previousView.Unparent();", StringComparison.Ordinal);
		var disposeHandler = source.IndexOf("previousHandler.Dispose();", StringComparison.Ordinal);

		Assert.True(unparent >= 0, "The previous content view is never unparented.");
		Assert.True(disposeHandler >= 0, "The previous content handler is never disposed.");
		Assert.True(unparent < disposeHandler, "The view must be unparented before its owner is disposed.");
	}

	/// <summary>
	/// The swipe animation is aborted before the content it animates is replaced or torn down.
	/// </summary>
	/// <remarks>
	/// The animation is committed under a fixed handle and outlives the content otherwise. Both its
	/// stepper and its finished callback touch the content view, so an animation left running across
	/// a replacement or a disconnect runs against a disposed native object.
	/// </remarks>
	[Theory]
	[InlineData("UpdateContent")]
	[InlineData("DisposeChildHandlers")]
	public void TheSwipeAnimationIsAbortedBefore(string method)
	{
		var source = SwipeGroup;

		var start = source.IndexOf($"public void {method}()", StringComparison.Ordinal);
		Assert.True(start >= 0, $"{method} not found.");

		var body = source[start..Math.Min(source.Length, start + 700)];

		Assert.Contains("AbortSwipeAnimation();", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// The animation callbacks capture the view rather than reading the mutable field.
	/// </summary>
	/// <remarks>
	/// Reading <c>_contentView</c> inside the stepper means a replacement mid-animation silently
	/// redirects the animation onto the new view.
	/// </remarks>
	[Fact]
	public void TheSwipeAnimationCapturesItsView()
	{
		var source = SwipeGroup;

		Assert.Contains("var animatedView = _contentView;", source, StringComparison.Ordinal);
		Assert.DoesNotContain("_contentView.PositionX = (float)(contentPosition.X + diffX", source, StringComparison.Ordinal);
		Assert.DoesNotContain("_contentView.PositionY = (float)(contentPosition.Y + diffY", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// Refresh teardown must not write <c>IsRefreshing</c>.
	/// </summary>
	/// <remarks>
	/// That write starts the base class's completion animation, an async void with no cancellation,
	/// whose continuation then touches the refresh icon the same teardown is about to dispose.
	/// </remarks>
	[Fact]
	public void RefreshTeardownDoesNotStartTheCompletionAnimation()
	{
		var source = ReadCode("src", "Maui.Tizen.Core", "Handlers", "RefreshView", "TizenRefreshViewHandler.cs");

		var start = source.IndexOf("protected override void DisconnectHandler", StringComparison.Ordinal);
		Assert.True(start >= 0);

		var body = source[start..source.IndexOf("base.DisconnectHandler", start, StringComparison.Ordinal)];

		Assert.DoesNotContain("IsRefreshing = false", body, StringComparison.Ordinal);
		Assert.Contains("RefreshState.Reset();", body, StringComparison.Ordinal);

		// The scheduled replay must be cancelled, or it fires against a disposed view.
		Assert.Contains("_completionCts?.Cancel();", body, StringComparison.Ordinal);
	}

	/// <summary>
	/// NUI signal cleanup is marshalled to the main loop and awaited.
	/// </summary>
	/// <remarks>
	/// The continuation that unsubscribes <c>ResourceReady</c> resumes on a pool thread, or on
	/// whichever thread cancelled. Posting the unsubscribe without awaiting it would let the
	/// caller's disposal of the platform view overtake it.
	/// </remarks>
	[Fact]
	public void ResourceReadyCleanupIsMarshalledAndAwaited()
	{
		var source = ReadCode("src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenWaveBInterop.cs");

		Assert.Contains("await UnsubscribeResourceReadyAsync(", source, StringComparison.Ordinal);
		Assert.Contains("await dispatcher.DispatchAsync(() => imageView.ResourceReady -= handler)", source, StringComparison.Ordinal);

		// The unguarded form must be gone from the apply path.
		Assert.DoesNotContain("\t\t\t\timageView.ResourceReady -= OnResourceReady;", source, StringComparison.Ordinal);
	}

	/// <summary>Every image apply call site supplies a dispatcher.</summary>
	/// <remarks>
	/// The parameter is optional so the extension stays usable without one, which means a call site
	/// that forgets it silently reverts to unmarshalled cleanup.
	/// </remarks>
	[Theory]
	[InlineData("Image/TizenImageHandler.cs")]
	[InlineData("ImageButton/TizenImageButtonHandler.cs")]
	[InlineData("SwipeItemMenuItem/TizenSwipeItemMenuItemHandler.cs")]
	public void EveryImageApplyCallSitePassesADispatcher(string relative)
	{
		var source = Read(new[] { "src", "Maui.Tizen.Core", "Handlers" }.Concat(relative.Split('/')).ToArray());

		Assert.Contains("ApplyImageSourceAsync(", source, StringComparison.Ordinal);
		Assert.Contains("GetService<Microsoft.Maui.Dispatching.IDispatcher>()", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// The indicator's visibility decision consults the virtual view's Visibility.
	/// </summary>
	[Fact]
	public void IndicatorVisibilityConsultsTheVirtualView()
	{
		var source = Read("src", "Maui.Tizen.Core", "Platform", "Tizen", "TizenPageControl.cs");

		Assert.Contains("IsIndicatorVisible(_indicatorView.Visibility", source, StringComparison.Ordinal);
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
