using System;
using System.Collections.Generic;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Caches the platform view created for each shell section, and tracks which one is mounted.
	/// </summary>
	/// <typeparam name="TSection">The shell section type.</typeparam>
	/// <typeparam name="TPlatformView">The platform view type created for a section.</typeparam>
	/// <remarks>
	/// <para>
	/// Shell content is created lazily: a section's platform view is built the first time that
	/// section becomes current, and reused afterwards. Getting that wrong is expensive in both
	/// directions - rebuilding on every switch throws away navigation stacks and scroll positions,
	/// while never releasing leaks a view per section for the life of the shell.
	/// </para>
	/// <para>
	/// The rule is generic over the platform view type so it holds no NUI reference and can be
	/// executed in a host test. <c>TizenShellItemView</c> is an <c>NView</c> and cannot be
	/// instantiated off-device, so without this split the lazy-creation and content-switch
	/// behaviour could only ever be asserted about, never run.
	/// </para>
	/// </remarks>
	internal sealed class ShellSectionViewCache<TSection, TPlatformView>
		where TSection : class
		where TPlatformView : class
	{
		readonly Dictionary<TSection, TPlatformView> _cache = new();

		/// <summary>Gets the section currently mounted, if any.</summary>
		public TSection? CurrentSection { get; private set; }

		/// <summary>Gets the platform view currently mounted, if any.</summary>
		public TPlatformView? CurrentView { get; private set; }

		/// <summary>Gets how many sections have had a platform view created.</summary>
		public int CreatedCount => _cache.Count;

		/// <summary>Gets whether <paramref name="section"/> already has a platform view.</summary>
		public bool IsCreated(TSection section) => section is not null && _cache.ContainsKey(section);

		/// <summary>
		/// Makes <paramref name="section"/> current, creating its platform view only if this is the
		/// first time it has been shown.
		/// </summary>
		/// <param name="section">The section becoming current.</param>
		/// <param name="create">Creates the platform view for a section.</param>
		/// <param name="unmount">Detaches the previously mounted view, if there was one.</param>
		/// <returns>The platform view now mounted.</returns>
		public TPlatformView? SetCurrent(
			TSection? section,
			Func<TSection, TPlatformView> create,
			Action<TPlatformView>? unmount = null)
		{
			ArgumentNullException.ThrowIfNull(create);

			if (ReferenceEquals(CurrentSection, section))
				return CurrentView;

			if (CurrentView is { } previous)
			{
				// Detached, NOT disposed: the view stays in the cache so returning to this section
				// restores its state instead of rebuilding it.
				unmount?.Invoke(previous);
			}

			CurrentSection = section;

			if (section is null)
			{
				CurrentView = null;
				return null;
			}

			if (!_cache.TryGetValue(section, out TPlatformView? view))
			{
				view = create(section);
				_cache[section] = view;
			}

			CurrentView = view;
			return view;
		}

		/// <summary>
		/// Drops <paramref name="section"/>'s cached view, disposing it through
		/// <paramref name="dispose"/>.
		/// </summary>
		/// <remarks>
		/// Used when a section is removed from the shell. If the removed section is the mounted one,
		/// the current tracking is cleared too, so nothing keeps pointing at a disposed view.
		/// </remarks>
		public bool Remove(TSection section, Action<TPlatformView>? dispose = null)
		{
			if (section is null || !_cache.TryGetValue(section, out TPlatformView? view))
			{
				return false;
			}

			_cache.Remove(section);
			dispose?.Invoke(view);

			if (ReferenceEquals(CurrentSection, section))
			{
				CurrentSection = null;
				CurrentView = null;
			}

			return true;
		}

		/// <summary>
		/// Disposes every cached view and clears the cache.
		/// </summary>
		public void Clear(Action<TPlatformView>? dispose = null)
		{
			foreach (TPlatformView view in _cache.Values)
			{
				dispose?.Invoke(view);
			}

			_cache.Clear();
			CurrentSection = null;
			CurrentView = null;
		}
	}
}
