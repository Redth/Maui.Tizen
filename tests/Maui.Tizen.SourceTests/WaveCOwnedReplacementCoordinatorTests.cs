using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCOwnedReplacementCoordinatorTests
{
	[Fact]
	public void QueuedWorkFromAReplacedAdaptorIsIgnored()
	{
		var guard = new GenerationGuard();
		var old = guard.Capture();
		guard.Advance();
		var ran = false;

		Assert.False(guard.RunIfCurrent(old, () => ran = true));
		Assert.False(ran);
	}

	[Fact]
	public void RealizedItemOwnershipKeepsMovesAndReleasesRemovedItems()
	{
		var ownership = new RealizedItemOwnership<object, string>();
		var first = new object();
		var second = new object();
		var released = new List<string>();
		ownership.Track(first, "first-handler");
		ownership.Track(second, "second-handler");

		ownership.ReleaseRemoved(new[] { second, first }, (_, handler) => released.Add(handler));
		Assert.Empty(released);

		ownership.ReleaseRemoved(new[] { second }, (_, handler) => released.Add(handler));
		Assert.Equal(["first-handler"], released);

		ownership.ReleaseAll((_, handler) => released.Add(handler));
		Assert.Equal(["first-handler", "second-handler"], released);
	}

	[Fact]
	public void RealizedItemOwnershipReleasesEveryHandlerWhenOneThrows()
	{
		var ownership = new RealizedItemOwnership<object, string>();
		var first = new object();
		var second = new object();
		var released = new List<string>();
		ownership.Track(first, "first-handler");
		ownership.Track(second, "second-handler");

		Assert.Throws<AggregateException>(() => ownership.ReleaseAll((_, handler) =>
		{
			released.Add(handler);
			if (handler == "first-handler")
				throw new InvalidOperationException("expected");
		}));

		Assert.Equal(2, released.Count);
	}

	sealed class EqualReference(string name)
	{
		public string Name { get; } = name;
		public override bool Equals(object? obj) => obj is EqualReference;
		public override int GetHashCode() => 1;
	}

	[Fact]
	public void RealizedRowsUseHolderAndAbsoluteIndexInsteadOfItemEquality()
	{
		var rows = new RealizedRowIndexMap<EqualReference, EqualReference>();
		var firstHolder = new EqualReference("first-holder");
		var secondHolder = new EqualReference("second-holder");
		var firstView = new EqualReference("first-view");
		var secondView = new EqualReference("second-view");

		rows.Bind(firstHolder, 2, firstView);
		rows.Bind(secondHolder, 3, secondView);

		Assert.Same(firstView, rows.GetView(2));
		Assert.Same(secondView, rows.GetView(3));
		Assert.True(rows.TryGetIndex(firstView, out var firstIndex));
		Assert.Equal(2, firstIndex);

		rows.Unbind(firstHolder);

		Assert.Null(rows.GetView(2));
		Assert.Same(secondView, rows.GetView(3));
	}

	[Fact]
	public void RealizedRowsShiftAcrossInsertionsAndRemovals()
	{
		var rows = new RealizedRowIndexMap<object, object>();
		var removedHolder = new object();
		var shiftedHolder = new object();
		var removedView = new object();
		var shiftedView = new object();
		rows.Bind(removedHolder, 2, removedView);
		rows.Bind(shiftedHolder, 4, shiftedView);

		rows.Apply(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
			System.Collections.Specialized.NotifyCollectionChangedAction.Add,
			new[] { new object(), new object() },
			1));
		Assert.Same(removedView, rows.GetView(4));
		Assert.Same(shiftedView, rows.GetView(6));

		rows.Apply(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
			System.Collections.Specialized.NotifyCollectionChangedAction.Remove,
			new[] { new object(), new object() },
			3));
		Assert.False(rows.TryGetIndex(removedView, out _));
		Assert.Same(shiftedView, rows.GetView(4));
		Assert.True(rows.Unbind(removedHolder));
		Assert.True(rows.TryGetIndex(shiftedView, out var shiftedIndex));
		Assert.Equal(4, shiftedIndex);
	}

	[Theory]
	[InlineData(System.Collections.Specialized.NotifyCollectionChangedAction.Add)]
	[InlineData(System.Collections.Specialized.NotifyCollectionChangedAction.Remove)]
	[InlineData(System.Collections.Specialized.NotifyCollectionChangedAction.Replace)]
	public void UnknownIndexesInvalidateAllRealizedIndexLookups(
		System.Collections.Specialized.NotifyCollectionChangedAction action)
	{
		var rows = new RealizedRowIndexMap<object, object>();
		var holder = new object();
		var view = new object();
		rows.Bind(holder, 2, view);
		var item = new object();
		var change = action switch
		{
			System.Collections.Specialized.NotifyCollectionChangedAction.Add =>
				new System.Collections.Specialized.NotifyCollectionChangedEventArgs(action, item, -1),
			System.Collections.Specialized.NotifyCollectionChangedAction.Remove =>
				new System.Collections.Specialized.NotifyCollectionChangedEventArgs(action, item, -1),
			_ => new System.Collections.Specialized.NotifyCollectionChangedEventArgs(action, item, new object(), -1),
		};

		rows.Apply(change);

		Assert.Null(rows.GetView(2));
		Assert.False(rows.TryGetIndex(view, out _));
		Assert.True(rows.Unbind(holder));
	}

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
