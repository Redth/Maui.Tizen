using System;
using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Works out how to turn one navigation stack into another.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Split out from <c>TizenStackNavigationManager</c> because the manager derives from a NUI
	/// view and can only ever be compile-checked, while this is the part that was actually wrong.
	/// The previous reconciliation assumed the new stack shared a prefix with the old one and
	/// differed only in length:
	/// </para>
	/// <list type="bullet">
	/// <item>
	/// Pushing started at <c>_navigationStack.Count</c>, so replacing pages - [A, B] becoming
	/// [A, C, D] - pushed C and D on top of B and left B in the platform stack. The managed stack
	/// and the native stack then disagreed permanently.
	/// </item>
	/// <item>
	/// The insert path indexed <c>_navigationStack[i]</c> while <c>i</c> ranged over the LONGER new
	/// stack, so inserting more than one page threw <see cref="ArgumentOutOfRangeException"/>.
	/// </item>
	/// </list>
	/// <para>
	/// Both are fixed by finding the longest common prefix, popping everything above it, and
	/// pushing the new remainder - which handles arbitrary replacement, insertion and truncation
	/// without special cases.
	/// </para>
	/// </remarks>
	internal static class TizenNavigationReconciler
	{
		/// <summary>A plan for moving from one stack to another.</summary>
		/// <param name="CommonPrefix">Pages shared by both stacks, which stay untouched.</param>
		/// <param name="Pops">Pages to pop, ordered top-most first.</param>
		/// <param name="Pushes">Pages to push, ordered bottom-most first.</param>
		internal sealed record Plan(
			int CommonPrefix,
			IReadOnlyList<IView> Pops,
			IReadOnlyList<IView> Pushes);

		/// <summary>Builds the plan to move from <paramref name="previous"/> to <paramref name="target"/>.</summary>
		public static Plan Reconcile(IReadOnlyList<IView> previous, IReadOnlyList<IView> target)
		{
			ArgumentNullException.ThrowIfNull(previous);
			ArgumentNullException.ThrowIfNull(target);

			var common = CommonPrefixLength(previous, target);

			var pops = new List<IView>();
			for (var i = previous.Count - 1; i >= common; i--)
				pops.Add(previous[i]);

			var pushes = new List<IView>();
			for (var i = common; i < target.Count; i++)
				pushes.Add(target[i]);

			return new Plan(common, pops, pushes);
		}

		/// <summary>Length of the longest shared prefix of the two stacks.</summary>
		public static int CommonPrefixLength(IReadOnlyList<IView> previous, IReadOnlyList<IView> target)
		{
			ArgumentNullException.ThrowIfNull(previous);
			ArgumentNullException.ThrowIfNull(target);

			var max = Math.Min(previous.Count, target.Count);
			var i = 0;

			while (i < max && ReferenceEquals(previous[i], target[i]))
				i++;

			return i;
		}

		/// <summary>
		/// For a stack whose top is unchanged, finds the page each newly inserted page should be
		/// inserted before.
		/// </summary>
		/// <remarks>
		/// The anchor is always drawn from <paramref name="target"/> and is always a page that is
		/// already present, which is what makes this safe - the previous implementation indexed the
		/// OLD stack with a position from the new one.
		/// </remarks>
		/// <param name="current">The stack as it is now.</param>
		/// <param name="target">The stack as it should be.</param>
		/// <returns>Pairs of (page to insert, page to insert it before), in bottom-up order.</returns>
		public static IReadOnlyList<(IView Page, IView Before)> PlanInsertions(
			IReadOnlyList<IView> current,
			IReadOnlyList<IView> target)
		{
			ArgumentNullException.ThrowIfNull(current);
			ArgumentNullException.ThrowIfNull(target);

			var insertions = new List<(IView, IView)>();

			for (var i = 0; i < target.Count; i++)
			{
				var page = target[i];

				if (Contains(current, page))
					continue;

				// The next page that already exists is where this one goes in front of.
				IView? anchor = null;
				for (var j = i + 1; j < target.Count; j++)
				{
					if (Contains(current, target[j]))
					{
						anchor = target[j];
						break;
					}
				}

				// Callers only use this when the top is unchanged, so an anchor always exists.
				if (anchor is not null)
					insertions.Add((page, anchor));
			}

			return insertions;
		}

		/// <summary>Pages present now that the target stack no longer contains.</summary>
		public static IReadOnlyList<IView> PlanRemovals(
			IReadOnlyList<IView> current,
			IReadOnlyList<IView> target)
		{
			ArgumentNullException.ThrowIfNull(current);
			ArgumentNullException.ThrowIfNull(target);

			var removals = new List<IView>();

			foreach (var page in current)
			{
				if (!Contains(target, page))
					removals.Add(page);
			}

			return removals;
		}

		static bool Contains(IReadOnlyList<IView> stack, IView page)
		{
			for (var i = 0; i < stack.Count; i++)
			{
				if (ReferenceEquals(stack[i], page))
					return true;
			}

			return false;
		}
	}

	internal sealed class NavigationRequestGeneration<TOwner>
		where TOwner : class
	{
		int _generation;

		public (int Generation, TOwner Owner) Begin(TOwner owner)
		{
			ArgumentNullException.ThrowIfNull(owner);
			return (++_generation, owner);
		}

		public void Invalidate() => _generation++;

		public bool IsCurrent((int Generation, TOwner Owner) request, TOwner? currentOwner) =>
			request.Generation == _generation && ReferenceEquals(request.Owner, currentOwner);
	}
}
