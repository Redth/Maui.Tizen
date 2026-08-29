using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Executable tests for toolbar ownership transfer.
/// </summary>
/// <remarks>
/// <para>
/// Core's <c>ITizenToolbarContainer.SetToolbar</c> transfers ownership and <b>disposes</b> the
/// toolbar it replaces. The failure modes that creates - unsubscribing from an already-disposed
/// instance, stacking duplicate subscriptions across remaps, double-teardown - are all runtime
/// behaviours that source analysis and a type-check lane cannot see.
/// </para>
/// <para>
/// <see cref="ToolbarOwnership{TToolbar}"/> is deliberately generic and NUI-free precisely so those
/// rules can be executed here on a plain host, using a stand-in toolbar. What remains device-gated
/// is the actual disposal and visual behaviour; what is pinned here is the bookkeeping that decides
/// whether a disposed instance is ever touched at all.
/// </para>
/// </remarks>
public class WaveCToolbarOwnershipTests
{
	/// <summary>Stand-in for the platform toolbar; records subscribe/unsubscribe traffic.</summary>
	sealed class FakeToolbar
	{
		public FakeToolbar(string name) => Name = name;

		public string Name { get; }

		public int Subscriptions { get; private set; }

		public bool Disposed { get; private set; }

		public void Subscribe()
		{
			ThrowIfDisposed();
			Subscriptions++;
		}

		public void Unsubscribe()
		{
			ThrowIfDisposed();
			Subscriptions--;
		}

		/// <summary>Models what <c>SetToolbar</c> does to the toolbar it replaces.</summary>
		public void Dispose() => Disposed = true;

		void ThrowIfDisposed()
		{
			if (Disposed)
			{
				throw new ObjectDisposedException(Name, "Touched a toolbar that SetToolbar already disposed.");
			}
		}
	}

	static ToolbarOwnership<FakeToolbar> NewTracker() =>
		new(t => t.Subscribe(), t => t.Unsubscribe());

	[Fact]
	public void TakingOwnershipSubscribesExactlyOnce()
	{
		var tracker = NewTracker();
		var toolbar = new FakeToolbar("a");

		tracker.Transfer(toolbar);

		Assert.Same(toolbar, tracker.Current);
		Assert.Equal(1, toolbar.Subscriptions);
		Assert.Equal(1, tracker.SubscriptionCount);
	}

	/// <summary>
	/// Re-setting the same instance must not stack a second subscription.
	/// </summary>
	/// <remarks>
	/// A duplicate subscription fires the icon handler twice per press. On a flyout toggle that
	/// cancels itself out, so the symptom is "the toolbar button does nothing" - which is a
	/// genuinely hard bug to trace back to a repeated toolbar remap.
	/// </remarks>
	[Fact]
	public void RepeatedTransfersOfTheSameToolbarDoNotStackSubscriptions()
	{
		var tracker = NewTracker();
		var toolbar = new FakeToolbar("a");

		tracker.Transfer(toolbar);
		tracker.Transfer(toolbar);
		tracker.Transfer(toolbar);

		Assert.Equal(1, toolbar.Subscriptions);
		Assert.Equal(1, tracker.SubscriptionCount);
	}

	/// <summary>
	/// The outgoing toolbar must be unsubscribed <em>before</em> the caller disposes it.
	/// </summary>
	[Fact]
	public void TransferUnsubscribesTheOutgoingToolbarBeforeItIsDisposed()
	{
		var tracker = NewTracker();
		var first = new FakeToolbar("first");
		var second = new FakeToolbar("second");

		tracker.Transfer(first);
		tracker.Transfer(second);

		// Unsubscribed while still alive, so the ownership transfer may now dispose it safely.
		Assert.Equal(0, first.Subscriptions);
		first.Dispose();

		Assert.Same(second, tracker.Current);
		Assert.Equal(1, second.Subscriptions);
	}

	/// <summary>
	/// Teardown after the previous toolbar was disposed must not touch it.
	/// </summary>
	/// <remarks>
	/// This is the exact hazard the cached-field implementation had: it held the replaced instance
	/// and unsubscribed from it during disposal, after <c>SetToolbar</c> had already disposed it.
	/// The fake throws <see cref="ObjectDisposedException"/> in that case, so this test fails loudly
	/// if the ordering regresses.
	/// </remarks>
	[Fact]
	public void ReleaseNeverTouchesAToolbarThatOwnershipTransferAlreadyDisposed()
	{
		var tracker = NewTracker();
		var replaced = new FakeToolbar("replaced");
		var current = new FakeToolbar("current");

		tracker.Transfer(replaced);
		tracker.Transfer(current);

		// SetToolbar disposes the instance it replaced.
		replaced.Dispose();

		// Teardown must only touch the instance actually owned.
		tracker.Release();

		Assert.Null(tracker.Current);
		Assert.Equal(0, current.Subscriptions);
	}

	[Fact]
	public void ReleaseIsIdempotent()
	{
		var tracker = NewTracker();
		var toolbar = new FakeToolbar("a");

		tracker.Transfer(toolbar);

		tracker.Release();
		tracker.Release();
		tracker.Release();

		Assert.Null(tracker.Current);
		Assert.Equal(0, tracker.SubscriptionCount);
		Assert.Equal(0, toolbar.Subscriptions);
	}

	/// <summary>
	/// Releasing, then having the owned instance disposed, then releasing again must be safe.
	/// </summary>
	[Fact]
	public void ReleaseAfterTheOwnedToolbarWasDisposedIsSafe()
	{
		var tracker = NewTracker();
		var toolbar = new FakeToolbar("a");

		tracker.Transfer(toolbar);
		tracker.Release();

		toolbar.Dispose();

		// No owner, so nothing is touched - no ObjectDisposedException.
		tracker.Release();

		Assert.Null(tracker.Current);
	}

	[Fact]
	public void TransferringNullReleasesWithoutTakingNewOwnership()
	{
		var tracker = NewTracker();
		var toolbar = new FakeToolbar("a");

		tracker.Transfer(toolbar);
		tracker.Transfer(null);

		Assert.Null(tracker.Current);
		Assert.Equal(0, toolbar.Subscriptions);
		Assert.Equal(0, tracker.SubscriptionCount);
	}

	[Fact]
	public void SubscriptionCountIsNeverMoreThanOneAcrossAChurnOfTransfers()
	{
		var tracker = NewTracker();
		var toolbars = Enumerable.Range(0, 25).Select(i => new FakeToolbar($"t{i}")).ToList();

		foreach (var toolbar in toolbars)
		{
			tracker.Transfer(toolbar);
			Assert.Equal(1, tracker.SubscriptionCount);
		}

		tracker.Release();

		Assert.All(toolbars, t => Assert.Equal(0, t.Subscriptions));
	}

	[Fact]
	public void ShellUnsubscribesBeforeTheContainerDisposesAndSubscribesAfterTransfer()
	{
		var source = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Controls", "Navigation", "Platform", "Shell", "TizenShellView.cs"));
		var methodStart = source.IndexOf("public void UpdateToolbar()", StringComparison.Ordinal);
		var methodEnd = source.IndexOf("public void DetachToolbar()", methodStart, StringComparison.Ordinal);
		var body = source[methodStart..methodEnd];

		var release = body.LastIndexOf("_toolbarOwnership.Release();", StringComparison.Ordinal);
		var transferOwnership = body.LastIndexOf("_mainContentView.SetToolbar(platformToolbar);", StringComparison.Ordinal);
		var subscribe = body.IndexOf("_toolbarOwnership.Transfer(platformToolbar);", StringComparison.Ordinal);

		Assert.True(release >= 0 && transferOwnership > release && subscribe > transferOwnership);
	}

	[Fact]
	public void ShellToolbarContainerDisposesTheToolbarItReplaces()
	{
		var source = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Controls", "Navigation", "Platform", "Shell", "TizenNavigationContentView.cs"));

		Assert.Contains("ITizenToolbarContainer", source, StringComparison.Ordinal);
		Assert.Contains("outgoing.Dispose();", source, StringComparison.Ordinal);
	}

	[Fact]
	public void ToolbarImagesUseTheSharedTypedLoaderAndDisposeOnReplacement()
	{
		var handler = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Controls", "Navigation", "Handlers", "Toolbar", "TizenToolbarHandler.cs"));
		var extensions = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Controls", "Navigation", "Platform", "Toolbar", "TizenToolbarExtensions.cs"));

		Assert.Contains("TizenImageLoader<TizenImageSource>", handler, StringComparison.Ordinal);
		Assert.Contains("GetTizenImageAsync", handler, StringComparison.Ordinal);
		Assert.Contains("DisposeActionIconLoaders();", handler, StringComparison.Ordinal);
		Assert.DoesNotContain(".LoadImage(", extensions, StringComparison.Ordinal);
	}
}
