using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// Covers dialog placeholder coordination on the navigation stack.
/// </summary>
/// <remarks>
/// This logic used to live in a NUI-only type and could only be checked on a device. It now works
/// through <see cref="ITizenNavigationStack"/>, so placeholder balance - the failure mode that
/// leaves an app with a permanently wedged modal stack - is verified here.
/// </remarks>
public class TizenModalHostTests
{
	[Fact]
	public async Task APlaceholderIsPushedForTheDurationOfTheDialog()
	{
		var stack = new FakeNavigationStack();
		var host = new TizenModalHost(stack);
		var depthDuringDialog = -1;

		await host.RunModalAsync(() =>
		{
			depthDuringDialog = stack.Count;
			return Task.CompletedTask;
		});

		Assert.Equal(1, depthDuringDialog);
		Assert.Equal(0, stack.Count);
	}

	[Fact]
	public async Task ThePlaceholderIsNeverAnimated()
	{
		var stack = new FakeNavigationStack();
		var host = new TizenModalHost(stack);

		await host.RunModalAsync(() => Task.CompletedTask);

		Assert.False(Assert.Single(stack.PushAnimations));
		Assert.False(Assert.Single(stack.PopAnimations));
		Assert.Equal(1, Assert.Single(stack.Placeholders).DisposeCount);
	}

	[Fact]
	public async Task ThePageUnderneathKeepsRenderingWhileTheDialogIsOpen()
	{
		var stack = new FakeNavigationStack();
		var host = new TizenModalHost(stack);

		await host.RunModalAsync(() => Task.CompletedTask);

		// ShownBehindPage is set around the placeholder push and cleared again, because a dialog
		// floats above the page rather than replacing it.
		Assert.False(stack.ShownBehindPage);
		Assert.Contains("Push(False)", stack.Operations);
	}

	[Fact]
	public async Task ThePlaceholderIsPoppedEvenWhenTheDialogFaults()
	{
		var stack = new FakeNavigationStack();
		var host = new TizenModalHost(stack);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			host.RunModalAsync(() => throw new InvalidOperationException("dialog failed")));

		// Leaving the placeholder behind would wedge the modal stack permanently.
		Assert.Equal(0, stack.Count);
	}

	[Fact]
	public async Task TheFailureIsNotSwallowed()
	{
		var stack = new FakeNavigationStack();
		var host = new TizenModalHost(stack);

		// The original NUI code swallowed everything, which was survivable only because it
		// published the dialog result from inside this scope. Here the caller is faulted instead
		// of being left pending forever.
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			host.RunModalAsync(() => throw new InvalidOperationException("dialog failed")));

		Assert.Equal("dialog failed", exception.Message);
	}

	[Fact]
	public async Task APlaceholderThatIsNoLongerOnTopIsRemovedByIdentity()
	{
		var stack = new FakeNavigationStack();
		var host = new TizenModalHost(stack);

		await host.RunModalAsync(async () =>
		{
			// Something else was pushed while the dialog was open, so the placeholder is buried.
			await stack.PushAsync(new object(), false);
		});

		Assert.DoesNotContain("Pop(False)", stack.Operations.Skip(1));
		Assert.Contains("Remove", stack.Operations);
		Assert.Equal(1, stack.Count);
		Assert.True(Assert.Single(stack.Placeholders).Disposed);
		Assert.Equal(1, Assert.Single(stack.Placeholders).DisposeCount);
	}

	[Fact]
	public async Task FailedIdentityRemovalKeepsThePlaceholderLiveForTheNextRetry()
	{
		var stack = new FakeNavigationStack();
		using var host = new TizenModalHost(stack);
		FakePlaceholder? first = null;

		await Assert.ThrowsAsync<AggregateException>(() => host.RunModalAsync(async () =>
		{
			first = Assert.IsType<FakePlaceholder>(stack.Top);
			await stack.PushAsync(new object(), false);
			stack.RemoveFailures.Add(first);
		}));

		Assert.NotNull(first);
		Assert.True(stack.Contains(first!));
		Assert.Equal(0, first!.DisposeCount);

		stack.RemoveFailures.Clear();
		await host.RunModalAsync(() => Task.CompletedTask);

		Assert.False(stack.Contains(first));
		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, stack.Count);
	}

	[Fact]
	public async Task NestedDialogsStayBalanced()
	{
		var stack = new FakeNavigationStack();
		var host = new TizenModalHost(stack);

		await host.RunModalAsync(async () => await host.RunModalAsync(() => Task.CompletedTask));

		Assert.Equal(0, stack.Count);
	}

	// The stack operations are awaited, not fire-and-forget. A discarded task swallows the fault
	// and lets the dialog open over a stack that never took the placeholder, unbalancing the pop.

	[Fact]
	public async Task ThePlaceholderIsOnTheStackBeforeTheDialogRuns()
	{
		var stack = new FakeNavigationStack { CompleteAsynchronously = true };
		var host = new TizenModalHost(stack);
		var depthDuringDialog = -1;

		await host.RunModalAsync(() =>
		{
			depthDuringDialog = stack.Count;
			return Task.CompletedTask;
		});

		// Without awaiting the push this observes 0: the dialog would open over a stack that has
		// not taken the placeholder yet.
		Assert.Equal(1, depthDuringDialog);
	}

	[Fact]
	public async Task AFailedPlaceholderPushSurfacesToTheCaller()
	{
		var stack = new FakeNavigationStack { PushFailure = new InvalidOperationException("stack push failed") };
		var host = new TizenModalHost(stack);
		var dialogRan = false;

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			host.RunModalAsync(() => { dialogRan = true; return Task.CompletedTask; }));

		Assert.Equal("stack push failed", exception.Message);

		// The dialog must not be presented over a stack that failed to take the placeholder.
		Assert.False(dialogRan);
	}

	[Fact]
	public async Task AFailedPlaceholderPushDoesNotLeaveShownBehindPageSet()
	{
		var stack = new FakeNavigationStack { PushFailure = new InvalidOperationException("stack push failed") };
		var host = new TizenModalHost(stack);

		await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunModalAsync(() => Task.CompletedTask));

		// Leaving this set would change how every later push renders.
		Assert.False(stack.ShownBehindPage);
	}

	[Fact]
	public async Task APlaceholderPushThatMutatesBeforeFaultingIsRolledBackAndDisposed()
	{
		var stack = new FakeNavigationStack
		{
			PushFailure = new InvalidOperationException("stack push failed"),
			MutateBeforePushFailure = true,
		};
		var host = new TizenModalHost(stack);

		await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunModalAsync(() => Task.CompletedTask));

		Assert.Equal(0, stack.Count);
		Assert.True(Assert.Single(stack.Placeholders).Disposed);
		Assert.Equal(1, Assert.Single(stack.Placeholders).DisposeCount);
	}

	[Fact]
	public async Task FailedPushRollbackKeepsTheLivePlaceholderForTheNextRetry()
	{
		var stack = new FakeNavigationStack
		{
			PushFailure = new InvalidOperationException("stack push failed"),
			MutateBeforePushFailure = true,
			PopFailure = new InvalidOperationException("stack pop failed"),
			FailEveryRemove = true,
		};
		using var host = new TizenModalHost(stack);

		await Assert.ThrowsAsync<AggregateException>(
			() => host.RunModalAsync(() => Task.CompletedTask));

		var first = Assert.Single(stack.Placeholders);
		Assert.True(stack.Contains(first));
		Assert.Equal(0, first.DisposeCount);

		stack.PushFailure = null;
		stack.PopFailure = null;
		stack.FailEveryRemove = false;
		await host.RunModalAsync(() => Task.CompletedTask);

		Assert.False(stack.Contains(first));
		Assert.Equal(1, first.DisposeCount);
	}

	[Fact]
	public async Task AFailedPlaceholderPopSurfacesToTheCaller()
	{
		var stack = new FakeNavigationStack { PopFailure = new InvalidOperationException("stack pop failed") };
		var host = new TizenModalHost(stack);

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			host.RunModalAsync(() => Task.CompletedTask));

		Assert.Equal("stack pop failed", exception.Message);
		Assert.Equal(0, stack.Count);
		Assert.True(Assert.Single(stack.Placeholders).Disposed);
		Assert.Equal(1, Assert.Single(stack.Placeholders).DisposeCount);
	}

	[Fact]
	public async Task APlaceholderPopThatMutatesBeforeFaultingStillDisposesThePlaceholder()
	{
		var stack = new FakeNavigationStack
		{
			PopFailure = new InvalidOperationException("stack pop failed"),
			MutateBeforePopFailure = true,
		};
		var host = new TizenModalHost(stack);

		await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunModalAsync(() => Task.CompletedTask));

		Assert.Equal(0, stack.Count);
		Assert.True(Assert.Single(stack.Placeholders).Disposed);
		Assert.Equal(1, Assert.Single(stack.Placeholders).DisposeCount);
	}

	[Fact]
	public async Task APlaceholderPopThatRemovesThenFaultsBeforeDisposalIsDisposedExactlyOnce()
	{
		var stack = new FakeNavigationStack
		{
			PopFailure = new InvalidOperationException("stack pop failed"),
			MutateBeforePopFailure = true,
			RemoveBeforePopFailureWithoutDisposal = true,
		};
		var host = new TizenModalHost(stack);

		await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunModalAsync(() => Task.CompletedTask));

		Assert.Equal(0, stack.Count);
		Assert.Equal(1, Assert.Single(stack.Placeholders).DisposeCount);
	}

	[Fact]
	public async Task PreMutationPlaceholderPopFailureThenRetryRemovalBeforeDisposalIsDisposedExactlyOnce()
	{
		var stack = new FakeNavigationStack
		{
			PopFailure = new InvalidOperationException("stack pop failed"),
			MutateBeforePopFailure = true,
			PopFailuresBeforeMutationRemaining = 1,
			RemoveBeforePopFailureWithoutDisposal = true,
		};
		var host = new TizenModalHost(stack);

		await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunModalAsync(() => Task.CompletedTask));

		Assert.Equal(0, stack.Count);
		Assert.Equal(1, Assert.Single(stack.Placeholders).DisposeCount);
	}

	[Fact]
	public async Task TheStackIsBalancedWhenOperationsCompleteAsynchronously()
	{
		var stack = new FakeNavigationStack { CompleteAsynchronously = true };
		var host = new TizenModalHost(stack);

		await host.RunModalAsync(() => Task.CompletedTask);
		await host.RunModalAsync(() => Task.CompletedTask);

		Assert.Equal(0, stack.Count);
		Assert.Equal(2, stack.PushAnimations.Count);
		Assert.Equal(2, stack.PopAnimations.Count);
	}

	// ShownBehindPage is stack-wide state owned by whatever is already presented. A dialog must
	// borrow it, not take it over.

	[Fact]
	public async Task ShownBehindPageIsRestoredRatherThanForcedFalse()
	{
		var stack = new FakeNavigationStack { ShownBehindPage = true };
		var host = new TizenModalHost(stack);

		await host.RunModalAsync(() => Task.CompletedTask);

		// Forcing false here silently reconfigures how every later push renders.
		Assert.True(stack.ShownBehindPage);
	}

	[Fact]
	public async Task ShownBehindPageIsRestoredEvenWhenThePlaceholderPushFails()
	{
		var stack = new FakeNavigationStack
		{
			ShownBehindPage = true,
			PushFailure = new InvalidOperationException("stack push failed"),
		};
		var host = new TizenModalHost(stack);

		await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunModalAsync(() => Task.CompletedTask));

		Assert.True(stack.ShownBehindPage);
	}

	[Fact]
	public async Task ShownBehindPageIsSetWhileThePlaceholderIsBeingPushed()
	{
		var stack = new FakeNavigationStack();
		var host = new TizenModalHost(stack);

		await host.RunModalAsync(() => Task.CompletedTask);

		// The page underneath must keep rendering while the placeholder goes on, which is the
		// whole reason the flag is touched at all.
		Assert.True(Assert.Single(stack.ShownBehindPageDuringPush));
		Assert.False(stack.ShownBehindPage);
	}

	[Fact]
	public async Task ANullOperationIsRejected()
	{
		var host = new TizenModalHost(new FakeNavigationStack());

		await Assert.ThrowsAsync<ArgumentNullException>(() => host.RunModalAsync(null!));
	}
}

/// <summary>
/// Covers the window-scoped holders that the Tizen window handler fills in.
/// </summary>
public class TizenScopedWindowServiceTests
{
	[Fact]
	public void AnUnattachedNavigationStackFailsLoudlyRatherThanSilently()
	{
		var stack = new TizenScopedNavigationStack();

		Assert.False(stack.IsAttached);

		// A modal that reports success without appearing is worse than a clear failure.
		var exception = Assert.Throws<InvalidOperationException>(() => stack.CreatePlaceholder());
		Assert.Contains("Controls scoped initializer", exception.Message);
	}

	[Fact]
	public void AnUnattachedNavigationStackStillReportsEmptyState()
	{
		var stack = new TizenScopedNavigationStack();

		// Queries must not throw, so that code which merely inspects the stack works before the
		// window is realized.
		Assert.Equal(0, stack.Count);
		Assert.Null(stack.Top);
		Assert.False(stack.ShownBehindPage);
	}

	[Fact]
	public async Task AttachingForwardsToTheNativeStack()
	{
		var target = new FakeNavigationStack();
		var stack = new TizenScopedNavigationStack();
		stack.Attach(target);

		await stack.PushAsync(new object(), true);

		Assert.True(stack.IsAttached);
		Assert.Equal(1, target.Count);
		Assert.Equal(1, stack.Count);
	}

	[Fact]
	public void TheBackButtonHandlerSetBeforeAttachmentIsReplayed()
	{
		var scoped = new TizenScopedWindowBackButton();
		Func<bool> handler = static () => true;

		// The modal platform installs its handler on PageAttached, which can run before the window
		// handler attaches the native window.
		using var registration = scoped.RegisterBackButtonPressedHandler(handler);

		var target = new FakeWindowBackButton();
		scoped.Attach(target);

		Assert.Same(handler, target.Handler);
	}

	[Fact]
	public void TheBackButtonForwardsAfterAttachment()
	{
		var scoped = new TizenScopedWindowBackButton();
		var target = new FakeWindowBackButton();
		scoped.Attach(target);

		using var registration = scoped.RegisterBackButtonPressedHandler(static () => true);

		Assert.NotNull(target.Handler);
		Assert.True(scoped.IsAttached);
	}

	[Fact]
	public void DisposingTheScopedRegistrationRestoresTheTargetRoute()
	{
		var scoped = new TizenScopedWindowBackButton();
		var target = new FakeWindowBackButton { FallbackHandler = static () => true };
		scoped.Attach(target);
		var registration = scoped.RegisterBackButtonPressedHandler(static () => false);

		Assert.True(target.Invoke());

		registration.Dispose();

		Assert.Null(target.Handler);
		Assert.True(target.Invoke());
	}

	[Fact]
	public void AMissingBackButtonTargetIsNotFatal()
	{
		var scoped = new TizenScopedWindowBackButton();

		// Unlike the navigation stack, the app still runs; back presses just fall through.
		var exception = Record.Exception(() =>
		{
			using var registration = scoped.RegisterBackButtonPressedHandler(static () => true);
		});

		Assert.Null(exception);
	}
}

/// <summary>
/// Guards the provisional copies of the dotnet/maui#37853 contracts.
/// </summary>
/// <remarks>
/// These contracts are copied into this repository because the upstream PR is still open. That is
/// only safe if the copies cannot drift from the PR and cannot outlive it, which is what these
/// tests enforce.
/// </remarks>
public class ProvisionalModalNavigationContractTests
{
	[Fact]
	public void TheProvisionalContractsAreNotDeclaredInAMauiNamespace()
	{
		// Re-declaring a MAUI type name in a MAUI namespace would collide (CS0433) for consumers
		// that also reference MAUI's own build once the PR lands.
		foreach (var type in new[] { typeof(IModalNavigationPlatform), typeof(IModalNavigationPlatformFactory), typeof(IModalNavigationHost) })
		{
			Assert.Equal("Microsoft.Maui.Platforms.Tizen", type.Namespace);
		}
	}

	[Fact]
	public void PinnedMauiPackageDoesNotContainTheProvisionalTypes()
	{
		var mauiControls = typeof(Page).Assembly;

		var landed = new[]
		{
			"Microsoft.Maui.Controls.Platform.IModalNavigationPlatform",
			"Microsoft.Maui.Controls.Platform.IModalNavigationPlatformFactory",
			"Microsoft.Maui.Controls.Platform.IModalNavigationHost",
		}.Where(name => mauiControls.GetType(name) is not null).ToArray();

		Assert.True(
			landed.Length == 0,
			"The pinned MAUI package now contains the dotnet/maui#37853 contracts: " + string.Join(", ", landed) + ". "
				+ "Delete Core/Platform/Modal/ProvisionalModalNavigationContracts.cs, point "
				+ "TizenModalNavigationPlatform and TizenModalNavigationPlatformFactory at "
				+ "Microsoft.Maui.Controls.Platform, and delete this test.");
	}

	[Theory]
	[InlineData(typeof(IModalNavigationPlatform), "IsReady", "PushModalAsync", "PopModalAsync", "PageAttached")]
	[InlineData(typeof(IModalNavigationPlatformFactory), "CreateModalNavigationPlatform")]
	[InlineData(typeof(IModalNavigationHost), "Window", "MauiContext", "PlatformModalStack", "CurrentPage", "CurrentPlatformPage", "IsWindowReady", "IsBatchPushing", "IsBatchPopping", "RequestSync")]
	public void TheProvisionalShapeMatchesTheUpstreamPullRequest(Type contract, params string[] expectedMembers)
	{
		var actual = contract
			.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Where(m => m is not MethodInfo method || !method.IsSpecialName)
			.Select(m => m.Name)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(expectedMembers.OrderBy(n => n, StringComparer.Ordinal).ToArray(), actual);
	}

	[Fact]
	public void TheModalPlatformIsDisposable()
	{
		// The framework disposes the platform on window destroy and on handler change.
		Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(IModalNavigationPlatform)));
	}

	[Fact]
	public void TheFactoryMayDeclineToSupplyAPlatform()
	{
		var method = typeof(IModalNavigationPlatformFactory).GetMethod(nameof(IModalNavigationPlatformFactory.CreateModalNavigationPlatform))!;

		Assert.Equal(typeof(IModalNavigationPlatform), method.ReturnType);
		Assert.Equal(typeof(IModalNavigationHost), Assert.Single(method.GetParameters()).ParameterType);

		// Returning null means "keep the built-in platform", so the return type must be nullable.
		var nullability = new NullabilityInfoContext().Create(method.ReturnParameter);
		Assert.Equal(NullabilityState.Nullable, nullability.ReadState);
	}

	[Fact]
	public void TheTizenImplementationsSatisfyTheProvisionalContracts()
	{
		Assert.True(typeof(IModalNavigationPlatform).IsAssignableFrom(typeof(TizenModalNavigationPlatform)));
		Assert.True(typeof(IModalNavigationPlatformFactory).IsAssignableFrom(typeof(TizenModalNavigationPlatformFactory)));
	}
}
