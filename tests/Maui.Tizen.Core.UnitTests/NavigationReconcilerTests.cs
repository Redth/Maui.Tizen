using System;
using System.Linq;
using Microsoft.Maui;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Reconciliation of arbitrary navigation stack changes.
	/// </summary>
	/// <remarks>
	/// The manager itself derives from a NUI view and can only be compile-checked, so the decision
	/// - which pages to pop and push - is what these exercise. That is where the defects were.
	/// </remarks>
	public class NavigationReconcilerTests
	{
		/// <summary>A named page. Reuses the shared StubView so this file does not re-implement IView.</summary>
		sealed class Page : StubView
		{
			public Page(string name) => Name = name;

			public string Name { get; }

			public override string ToString() => Name;
		}

		static string[] Names(System.Collections.Generic.IReadOnlyList<IView> views) =>
			views.Cast<Page>().Select(p => p.Name).ToArray();

		[Fact]
		public void ReplacingPagesPopsTheOldOnesInsteadOfStackingOnTopOfThem()
		{
			// The defect. Pushing started at the old stack's Count, so [A, B] -> [A, C, D] pushed
			// C and D on top of B and left B in the platform stack. The managed and native stacks
			// then disagreed permanently, and nothing threw to say so.
			var a = new Page("A");
			var b = new Page("B");
			var plan = TizenNavigationReconciler.Reconcile(
				new IView[] { a, b },
				new IView[] { a, new Page("C"), new Page("D") });

			Assert.Equal(1, plan.CommonPrefix);
			Assert.Equal(new[] { "B" }, Names(plan.Pops));
			Assert.Equal(new[] { "C", "D" }, Names(plan.Pushes));
		}

		[Fact]
		public void AWhollyDifferentStackIsRebuilt()
		{
			var plan = TizenNavigationReconciler.Reconcile(
				new IView[] { new Page("A"), new Page("B") },
				new IView[] { new Page("X"), new Page("Y") });

			Assert.Equal(0, plan.CommonPrefix);

			// Popped top-most first, so the visible transition is the first one.
			Assert.Equal(new[] { "B", "A" }, Names(plan.Pops));
			Assert.Equal(new[] { "X", "Y" }, Names(plan.Pushes));
		}

		[Fact]
		public void PushingOntoAnUnchangedStackPopsNothing()
		{
			var a = new Page("A");
			var plan = TizenNavigationReconciler.Reconcile(new IView[] { a }, new IView[] { a, new Page("B") });

			Assert.Empty(plan.Pops);
			Assert.Equal(new[] { "B" }, Names(plan.Pushes));
		}

		[Fact]
		public void PoppingToTheRootPushesNothing()
		{
			var a = new Page("A");
			var b = new Page("B");
			var c = new Page("C");

			var plan = TizenNavigationReconciler.Reconcile(new IView[] { a, b, c }, new IView[] { a });

			Assert.Equal(new[] { "C", "B" }, Names(plan.Pops));
			Assert.Empty(plan.Pushes);
		}

		[Fact]
		public void AnIdenticalStackIsANoOp()
		{
			var a = new Page("A");
			var b = new Page("B");

			var plan = TizenNavigationReconciler.Reconcile(new IView[] { a, b }, new IView[] { a, b });

			Assert.Equal(2, plan.CommonPrefix);
			Assert.Empty(plan.Pops);
			Assert.Empty(plan.Pushes);
		}

		[Fact]
		public void EmptyingTheStackPopsEverything()
		{
			var plan = TizenNavigationReconciler.Reconcile(
				new IView[] { new Page("A"), new Page("B") },
				Array.Empty<IView>());

			Assert.Equal(new[] { "B", "A" }, Names(plan.Pops));
			Assert.Empty(plan.Pushes);
		}

		[Fact]
		public void InsertingSeveralPagesBeneathAnUnchangedTopDoesNotGoOutOfRange()
		{
			// The other defect, which threw rather than desynchronising: the insert path indexed
			// the OLD stack with a position taken from the LONGER new one, so inserting more than
			// one page raised ArgumentOutOfRangeException.
			var root = new Page("Root");
			var top = new Page("Top");

			var insertions = TizenNavigationReconciler.PlanInsertions(
				new IView[] { root, top },
				new IView[] { root, new Page("M1"), new Page("M2"), top });

			Assert.Equal(2, insertions.Count);
			Assert.All(insertions, i => Assert.Same(top, i.Before));
			Assert.Equal(new[] { "M1", "M2" }, insertions.Select(i => ((Page)i.Page).Name).ToArray());
		}

		[Fact]
		public void InsertionsAndRemovalsAreBothPlannedForTheSameChange()
		{
			// The old code chose between inserting and removing by comparing stack LENGTHS, so a
			// change that both added and removed pages while keeping the count the same did only
			// half the work.
			var root = new Page("Root");
			var top = new Page("Top");
			var dropped = new Page("Dropped");
			var added = new Page("Added");

			var current = new IView[] { root, dropped, top };
			var target = new IView[] { root, added, top };

			var insertions = TizenNavigationReconciler.PlanInsertions(current, target);
			var removals = TizenNavigationReconciler.PlanRemovals(current, target);

			Assert.Single(insertions);
			Assert.Same(added, insertions[0].Page);
			Assert.Same(top, insertions[0].Before);

			Assert.Single(removals);
			Assert.Same(dropped, removals[0]);
		}

		[Fact]
		public void PagesAreRemovedEvenWhenTheStackGrows()
		{
			// The length-branch bug precisely. The old code picked "insert" or "remove" by
			// comparing stack COUNTS, so a change that grew the stack while also dropping a page
			// did the insertions and silently skipped the removal, leaving an orphaned page in the
			// platform stack and its handler undisposed.
			//
			// The equal-length case alone does not catch this: the faulty branch only triggers when
			// the target is longer, which an earlier version of these tests never exercised.
			var root = new Page("Root");
			var top = new Page("Top");
			var dropped = new Page("Dropped");

			var current = new IView[] { root, dropped, top };
			var target = new IView[] { root, new Page("A1"), new Page("A2"), new Page("A3"), top };

			var removals = TizenNavigationReconciler.PlanRemovals(current, target);

			Assert.Same(dropped, Assert.Single(removals));
			Assert.True(target.Length > current.Length, "The target must be longer or this proves nothing.");
		}

		[Fact]
		public void RepeatedPageInstancesAreComparedByReference()
		{
			// Two pages that are equal by value but distinct instances must not be conflated - a
			// navigation stack routinely holds several pages of the same type.
			var a = new Page("A");
			var b1 = new Page("Same");
			var b2 = new Page("Same");

			var plan = TizenNavigationReconciler.Reconcile(new IView[] { a, b1 }, new IView[] { a, b2 });

			Assert.Equal(1, plan.CommonPrefix);
			Assert.Same(b1, Assert.Single(plan.Pops));
			Assert.Same(b2, Assert.Single(plan.Pushes));
		}
	}
}
