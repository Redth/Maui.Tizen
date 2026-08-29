using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCOwnedReplacementCoordinatorTests
{
	sealed class Resource(string name)
	{
		public string Name { get; } = name;
	}

	[Fact]
	public void ReplacementUsesRequiredOwnershipOrder()
	{
		var coordinator = new OwnedReplacementCoordinator<Resource>();
		var calls = new List<string>();
		var first = new Resource("first");
		var second = new Resource("second");

		coordinator.Replace(first, () => calls.Add("detach"), _ => { }, _ => { }, r => calls.Add($"subscribe:{r.Name}"), _ => calls.Add("attach:first"));
		calls.Clear();

		coordinator.Replace(
			second,
			() => calls.Add("detach"),
			resource => calls.Add($"unsubscribe:{resource.Name}"),
			resource => calls.Add($"dispose:{resource.Name}"),
			resource => calls.Add($"subscribe:{resource.Name}"),
			_ => calls.Add("attach:second"));

		Assert.Equal(
			["detach", "unsubscribe:first", "dispose:first", "subscribe:second", "attach:second"],
			calls);
		Assert.Same(second, coordinator.Current);
	}

	[Fact]
	public void LaterCleanupStillRunsWhenAnEarlierStepThrows()
	{
		var coordinator = new OwnedReplacementCoordinator<Resource>();
		var first = new Resource("first");
		var second = new Resource("second");
		coordinator.Replace(first, () => { }, _ => { }, _ => { }, _ => { }, _ => { });
		var attached = false;

		Assert.Throws<AggregateException>(() => coordinator.Replace(
			second,
			() => throw new InvalidOperationException("detach"),
			_ => throw new InvalidOperationException("unsubscribe"),
			_ => throw new InvalidOperationException("dispose"),
			_ => { },
			_ => attached = true));

		Assert.True(attached);
		Assert.Same(second, coordinator.Current);
	}
}
