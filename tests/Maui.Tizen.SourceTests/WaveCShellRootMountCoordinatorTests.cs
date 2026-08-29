using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCShellRootMountCoordinatorTests
{
	sealed class Root
	{
		public string? Current { get; set; }
		public int Updates { get; set; }
		public bool Disposed { get; set; }
	}

	[Fact]
	public void CurrentContentSetBeforeRootCreationIsAppliedAtCreation()
	{
		var coordinator = new ShellRootMountCoordinator<string, Root>();
		coordinator.SetCurrent("first", Update);

		var root = coordinator.GetOrCreate(() => new Root(), Update);

		Assert.Equal("first", root.Current);
		Assert.Equal(1, root.Updates);
	}

	[Fact]
	public void ExistingRootIsReusedAndUpdated()
	{
		var coordinator = new ShellRootMountCoordinator<string, Root>();
		var root = coordinator.GetOrCreate(() => new Root(), Update);

		coordinator.SetCurrent("second", Update);
		var reused = coordinator.GetOrCreate(() => throw new InvalidOperationException(), Update);

		Assert.Same(root, reused);
		Assert.Equal("second", root.Current);
	}

	[Fact]
	public void ClearDisposesRootAndDropsPendingContent()
	{
		var coordinator = new ShellRootMountCoordinator<string, Root>();
		var root = coordinator.GetOrCreate(() => new Root(), Update);
		coordinator.SetCurrent("current", Update);

		coordinator.Clear(value => value.Disposed = true);

		Assert.True(root.Disposed);
		Assert.Null(coordinator.Root);
	}

	static void Update(Root root, string current)
	{
		root.Current = current;
		root.Updates++;
	}
}
