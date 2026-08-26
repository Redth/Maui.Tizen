using System;
using System.Text;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Encodes Essentials keys into the flat key spaces Tizen provides.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Tizen's preference store and key manager are both flat: there is no per-shared-name
	/// namespace, so the shared name and the key have to be combined into a single string. Doing
	/// that by simple concatenation is ambiguous, and silently so.
	/// </para>
	/// <para>
	/// With the previous <c>{sharedName}~{key}</c> scheme, shared name <c>a</c> with key
	/// <c>b~c</c> and shared name <c>a~b</c> with key <c>c</c> both produced <c>a~b~c</c>. Two
	/// different logical entries then shared one physical entry: writing one overwrote the other,
	/// and <c>Clear("a")</c> removed the unrelated <c>a~b</c> entry because it merely matched the
	/// prefix.
	/// </para>
	/// <para>
	/// The separator is therefore escaped inside each component before joining, which makes the
	/// encoding injective: distinct (shared name, key) pairs always produce distinct strings.
	/// </para>
	/// </remarks>
	public static class TizenStorageKeyEncoding
	{
		const char Separator = '~';
		const char Escape = '\\';

		/// <summary>
		/// Encodes a single component so it can be joined with <see cref="Separator"/> unambiguously.
		/// </summary>
		/// <param name="value">The component to encode.</param>
		/// <returns>The escaped component.</returns>
		public static string Encode(string value)
		{
			ArgumentNullException.ThrowIfNull(value);

			// Fast path: nothing to escape, which is the overwhelmingly common case.
			if (value.IndexOf(Escape) < 0 && value.IndexOf(Separator) < 0)
				return value;

			var builder = new StringBuilder(value.Length + 8);

			foreach (var c in value)
			{
				if (c is Escape or Separator)
					builder.Append(Escape);

				builder.Append(c);
			}

			return builder.ToString();
		}

		/// <summary>
		/// Combines an optional shared name and a key into a single storage key.
		/// </summary>
		/// <param name="key">The entry key.</param>
		/// <param name="sharedName">The optional shared name.</param>
		/// <returns>The combined storage key.</returns>
		public static string GetFullKey(string key, string? sharedName)
		{
			ArgumentNullException.ThrowIfNull(key);

			// No shared name means the key is used verbatim, which preserves compatibility with
			// entries written by the in-box dotnet/maui Tizen backend for the default store - the
			// overwhelmingly common case.
			if (string.IsNullOrEmpty(sharedName))
				return key;

			return string.Concat(Encode(sharedName), Separator.ToString(), Encode(key));
		}

		/// <summary>
		/// Gets the prefix that every key belonging to a shared name starts with.
		/// </summary>
		/// <param name="sharedName">The shared name.</param>
		/// <returns>The prefix, including the trailing separator.</returns>
		/// <remarks>
		/// Because the shared name is escaped, this prefix cannot match a different shared name that
		/// happens to begin with the same characters.
		/// </remarks>
		public static string GetSharedNamePrefix(string sharedName)
		{
			ArgumentNullException.ThrowIfNull(sharedName);

			return Encode(sharedName) + Separator;
		}
	}
}
