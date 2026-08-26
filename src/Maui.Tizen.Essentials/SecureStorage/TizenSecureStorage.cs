using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Tizen.Security.SecureRepository;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="ISecureStorage"/>, backed by the Tizen key manager
	/// (<c>Tizen.Security.SecureRepository.DataManager</c>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Aliases are stored under a private prefix rather than using the caller's key directly. The
	/// key manager alias space is shared with the rest of the application - certificates, keys and
	/// any other component's data all live in it - so writing raw keys risked colliding with, and
	/// overwriting, data this API does not own.
	/// </para>
	/// <para>
	/// That matters most for <see cref="RemoveAll"/>: it previously enumerated every alias the
	/// application could see and deleted all of them, destroying unrelated secrets. It now removes
	/// only aliases this API created.
	/// </para>
	/// </remarks>
	public sealed class TizenSecureStorage : ISecureStorage
	{
		/// <summary>
		/// Prefix identifying aliases owned by this API.
		/// </summary>
		/// <remarks>
		/// The trailing separator cannot appear in an encoded key, so a prefixed alias can never be
		/// confused with another component's alias that merely starts with the same text.
		/// </remarks>
		internal const string AliasPrefix = "maui.tizen.securestorage:";

		/// <inheritdoc/>
		public Task<string?> GetAsync(string key)
		{
			ArgumentNullException.ThrowIfNull(key);

			try
			{
				// The second parameter is the data password, not a default value.
				return Task.FromResult<string?>(Encoding.UTF8.GetString(DataManager.Get(ToAlias(key), null)));
			}
			catch (InvalidOperationException)
			{
				// DataManager.Get throws when the alias does not exist. That is expected and normal.
				return Task.FromResult<string?>(null);
			}
			catch
			{
				global::Tizen.Log.Error(TizenPlatform.CurrentPackageLogTag, "Failed to load data.");
				throw;
			}
		}

		/// <inheritdoc/>
		public Task SetAsync(string key, string value)
		{
			ArgumentNullException.ThrowIfNull(key);
			ArgumentNullException.ThrowIfNull(value);

			var alias = ToAlias(key);

			try
			{
				try
				{
					// DataManager.Save throws when the alias already exists, and Tizen offers no
					// existence probe that does not throw, so remove unconditionally first.
					DataManager.RemoveAlias(alias);
				}
				catch
				{
					// Expected when the alias did not exist.
				}

				DataManager.Save(alias, Encoding.UTF8.GetBytes(value), new Policy());

				return Task.CompletedTask;
			}
			catch
			{
				global::Tizen.Log.Error(TizenPlatform.CurrentPackageLogTag, "Failed to save data.");
				throw;
			}
		}

		/// <inheritdoc/>
		public bool Remove(string key)
		{
			ArgumentNullException.ThrowIfNull(key);

			try
			{
				DataManager.RemoveAlias(ToAlias(key));
				return true;
			}
			catch
			{
				global::Tizen.Log.Info(TizenPlatform.CurrentPackageLogTag, "Failed to remove data.");
				return false;
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Removes only the aliases this API owns. Other secure-repository entries belonging to the
		/// application are left untouched.
		/// </remarks>
		public void RemoveAll()
		{
			IEnumerable<string> aliases;

			try
			{
				aliases = DataManager.GetAliases();
			}
			catch
			{
				global::Tizen.Log.Info(TizenPlatform.CurrentPackageLogTag, "No saved data.");
				return;
			}

			foreach (var alias in aliases)
			{
				if (!IsOwnedAlias(alias))
					continue;

				try
				{
					DataManager.RemoveAlias(alias);
				}
				catch
				{
					global::Tizen.Log.Info(TizenPlatform.CurrentPackageLogTag, "Failed to remove data.");
				}
			}
		}

		/// <summary>
		/// Maps a caller key onto the private alias actually stored in the key manager.
		/// </summary>
		/// <param name="key">The caller's key.</param>
		/// <returns>The namespaced alias.</returns>
		internal static string ToAlias(string key) =>
			AliasPrefix + TizenStorageKeyEncoding.Encode(key);

		/// <summary>
		/// Determines whether an alias was created by this API.
		/// </summary>
		/// <param name="alias">The raw key manager alias.</param>
		/// <returns><see langword="true"/> when the alias is owned by this API.</returns>
		/// <remarks>
		/// Tizen may return aliases qualified with the owning package id (<c>pkgid alias</c>), so a
		/// trailing match is accepted as well as an exact prefix match.
		/// </remarks>
		internal static bool IsOwnedAlias(string alias)
		{
			if (string.IsNullOrEmpty(alias))
				return false;

			if (alias.StartsWith(AliasPrefix, StringComparison.Ordinal))
				return true;

			var separator = alias.LastIndexOf(' ');

			return separator >= 0 &&
				alias.AsSpan(separator + 1).StartsWith(AliasPrefix, StringComparison.Ordinal);
		}
	}
}
