using System;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>Keeps a lazily-created Shell root synchronized with the current content.</summary>
	internal sealed class ShellRootMountCoordinator<TContent, TRoot>
		where TContent : class
		where TRoot : class
	{
		TContent? _current;

		public TRoot? Root { get; private set; }

		public void SetCurrent(TContent? current, Action<TRoot, TContent> update)
		{
			ArgumentNullException.ThrowIfNull(update);

			_current = current;
			if (Root is not null && current is not null)
				update(Root, current);
		}

		public TRoot GetOrCreate(Func<TRoot> create, Action<TRoot, TContent> update)
		{
			ArgumentNullException.ThrowIfNull(create);
			ArgumentNullException.ThrowIfNull(update);

			Root ??= create();
			if (_current is not null)
				update(Root, _current);

			return Root;
		}

		public void Clear(Action<TRoot>? dispose = null)
		{
			if (Root is { } root)
				dispose?.Invoke(root);

			Root = null;
			_current = null;
		}
	}
}
