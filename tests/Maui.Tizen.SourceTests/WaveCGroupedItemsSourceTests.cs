using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Platform;

namespace Maui.Tizen.SourceTests;

public class WaveCGroupedItemsSourceTests
{
	[Fact]
	public void InnerAddRaisesFlattenedAddAfterHeader()
	{
		var group = new ObservableCollection<object> { "a" };
		var groups = new ObservableCollection<object> { group };
		using var source = Create(groups);
		NotifyCollectionChangedEventArgs? observed = null;
		source.CollectionChanged += (_, args) => observed = args;

		group.Insert(1, "b");

		Assert.NotNull(observed);
		Assert.Equal(NotifyCollectionChangedAction.Add, observed.Action);
		Assert.Equal(2, observed.NewStartingIndex);
		Assert.Equal("b", observed.NewItems![0]);
	}

	[Fact]
	public void OuterMultiRowMoveRaisesReset()
	{
		var first = new ObservableCollection<object> { "a" };
		var second = new ObservableCollection<object> { "b", "c" };
		var groups = new ObservableCollection<object> { first, second };
		using var source = Create(groups);
		NotifyCollectionChangedEventArgs? observed = null;
		source.CollectionChanged += (_, args) => observed = args;

		groups.Move(1, 0);

		Assert.NotNull(observed);
		Assert.Equal(NotifyCollectionChangedAction.Reset, observed.Action);
	}

	[Fact]
	public void OuterAddAndRemoveUseFlattenedGroupRanges()
	{
		var groups = new ObservableCollection<object>
		{
			new ObservableCollection<object> { "a" },
		};
		using var source = Create(groups, footer: true);
		var changes = new List<NotifyCollectionChangedEventArgs>();
		source.CollectionChanged += (_, args) => changes.Add(args);

		var added = new ObservableCollection<object> { "b", "c" };
		groups.Add(added);
		groups.RemoveAt(0);

		Assert.Equal(NotifyCollectionChangedAction.Add, changes[0].Action);
		Assert.Equal(3, changes[0].NewStartingIndex);
		Assert.Equal(4, changes[0].NewItems!.Count);
		Assert.Equal(NotifyCollectionChangedAction.Remove, changes[1].Action);
		Assert.Equal(0, changes[1].OldStartingIndex);
		Assert.Equal(3, changes[1].OldItems!.Count);
	}

	[Fact]
	public void OuterRemovalReportsTheActualPreviouslyExposedHeader()
	{
		var first = new ObservableCollection<object> { "a" };
		var groups = new ObservableCollection<object> { first };
		using var source = Create(groups);
		var exposedHeader = source[0];
		NotifyCollectionChangedEventArgs? observed = null;
		source.CollectionChanged += (_, args) => observed = args;

		groups.Remove(first);

		Assert.NotNull(observed);
		Assert.Same(exposedHeader, observed.OldItems![0]);
	}

	[Fact]
	public void InnerRemoveReplaceMoveAndResetAreTranslated()
	{
		var group = new ObservableCollection<object> { "a", "b", "c" };
		var groups = new ObservableCollection<object> { group };
		using var source = Create(groups);
		var actions = new List<NotifyCollectionChangedAction>();
		source.CollectionChanged += (_, args) => actions.Add(args.Action);

		group.RemoveAt(0);
		group[0] = "replacement";
		group.Move(1, 0);
		group.Clear();

		Assert.Equal(
			[
				NotifyCollectionChangedAction.Remove,
				NotifyCollectionChangedAction.Replace,
				NotifyCollectionChangedAction.Move,
				NotifyCollectionChangedAction.Reset,
			],
			actions);
	}

	[Fact]
	public void GroupCoordinatesIncludeHeaderAndFooterRows()
	{
		var groups = new ObservableCollection<object>
		{
			new ObservableCollection<object> { "a", "b" },
			new ObservableCollection<object> { "c" },
		};
		using var source = Create(groups, footer: true);

		Assert.Equal(2, source.GetAbsoluteIndex(0, 1));
		Assert.Equal(5, source.GetAbsoluteIndex(1, 0));
		Assert.Equal(5, source.GetAbsoluteIndex(groups[1], "c"));
		Assert.True(source.IsGroupHeader(0));
		Assert.True(source.IsGroupFooter(3));
	}

	[Fact]
	public void GroupHeadersAreOmittedWithoutAHeaderTemplate()
	{
		var groups = new ObservableCollection<object>
		{
			new ObservableCollection<object> { "a", "b" },
			new ObservableCollection<object> { "c" },
		};
		using var source = Create(groups, header: false, footer: true);

		Assert.Equal(1, source.GetAbsoluteIndex(0, 1));
		Assert.Equal(3, source.GetAbsoluteIndex(1, 0));
		Assert.False(source.IsGroupHeader(0));
		Assert.True(source.IsGroupFooter(2));
	}

	[Fact]
	public void ImplementsTheObservableNonGenericListBoundaryRequiredByItemAdaptor()
	{
		var groups = new ObservableCollection<object>
		{
			new ObservableCollection<object> { "a" },
		};
		using var source = Create(groups);

		var list = Assert.IsAssignableFrom<System.Collections.IList>(source);
		Assert.IsAssignableFrom<INotifyCollectionChanged>(list);
		Assert.Equal(2, list.Count);
		Assert.Throws<NotSupportedException>(() => list.Add("not-a-group"));
		var adaptor = File.ReadAllText(WaveCSource.Files.Single(
			path => Path.GetFileName(path) == "TizenGroupItemTemplateAdaptor.cs"));
		Assert.Contains(": base(groupItemSource)", adaptor, StringComparison.Ordinal);
	}

	[Fact]
	public void DisposeStopsOuterAndInnerNotifications()
	{
		var group = new ObservableCollection<object> { "a" };
		var groups = new ObservableCollection<object> { group };
		var source = Create(groups);
		var notifications = 0;
		source.CollectionChanged += (_, _) => notifications++;

		source.Dispose();
		group.Add("b");
		groups.Add(new ObservableCollection<object>());

		Assert.Equal(0, notifications);
	}

	static TizenGroupItemSource Create(
		ObservableCollection<object> groups,
		bool header = true,
		bool footer = false) =>
		new(new CollectionView
		{
			IsGrouped = true,
			ItemsSource = groups,
			GroupHeaderTemplate = header ? new DataTemplate(() => new Label()) : null,
			GroupFooterTemplate = footer ? new DataTemplate(() => new Label()) : null,
		});
}
