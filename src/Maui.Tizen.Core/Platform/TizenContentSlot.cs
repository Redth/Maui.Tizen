namespace Microsoft.Maui.Platforms.Tizen
{
	internal readonly record struct TizenContentSlotChange<T>(T? Previous, T? Current)
		where T : class;

	internal sealed class TizenContentSlot<T>
		where T : class
	{
		public T? Title { get; private set; }

		public T? Search { get; private set; }

		public T? Current => Search ?? Title;

		public TizenContentSlotChange<T> SetTitle(T? title)
		{
			var previous = Current;
			Title = title;
			return new(previous, Current);
		}

		public TizenContentSlotChange<T> SetSearch(T? search)
		{
			var previous = Current;
			Search = search;
			return new(previous, Current);
		}

		public void Clear()
		{
			Title = null;
			Search = null;
		}
	}
}
