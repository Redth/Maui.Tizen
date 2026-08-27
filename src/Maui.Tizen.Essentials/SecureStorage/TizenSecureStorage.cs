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
		static readonly object Locker = new();
		const string AliasVersion = "v2:";
		readonly ITizenSecureRepository _repository;
		readonly ITizenSecureStorageTombstones _tombstones;

		/// <summary>
		/// Prefix identifying aliases owned by this API.
		/// </summary>
		/// <remarks>
		/// The trailing separator cannot appear in an encoded key, so a prefixed alias can never be
		/// confused with another component's alias that merely starts with the same text.
		/// </remarks>
		internal const string AliasPrefix = "maui.tizen.securestorage:";

		/// <summary>
		/// Creates a secure-storage service backed by Tizen's secure repository.
		/// </summary>
		public TizenSecureStorage()
			: this(TizenSecureRepository.Instance, TizenSecureStorageTombstones.Instance)
		{
		}

		internal TizenSecureStorage(
			ITizenSecureRepository repository,
			ITizenSecureStorageTombstones? tombstones = null)
		{
			_repository = repository;
			_tombstones = tombstones ?? new InMemorySecureStorageTombstones();
		}

		/// <inheritdoc/>
		public Task<string?> GetAsync(string key)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (Locker)
			{
				try
				{
					// The second parameter is the data password, not a default value.
					return Task.FromResult<string?>(Encoding.UTF8.GetString(_repository.Get(ToAlias(key))));
				}
				catch (InvalidOperationException)
				{
					// The namespaced alias does not exist.
				}

				if (_tombstones.Contains(key) || _tombstones.ContainsAll)
					return Task.FromResult<string?>(null);

				byte[] legacyValue;

				try
				{
					if (!CanUseLegacyNamespacedAlias(key))
						throw new InvalidOperationException();

					legacyValue = _repository.Get(ToLegacyNamespacedAlias(key));
				}
				catch (InvalidOperationException)
				{
					try
					{
						legacyValue = _repository.Get(key);
					}
					catch (InvalidOperationException)
					{
						return Task.FromResult<string?>(null);
					}
				}
				catch (ArgumentException)
				{
					try
					{
						legacyValue = _repository.Get(key);
					}
					catch (InvalidOperationException)
					{
						return Task.FromResult<string?>(null);
					}
				}

				// Legacy namespaced and raw aliases are copied into v2. Raw aliases are unowned and
				// may belong to another component, so they are never deleted.
				_repository.Save(ToAlias(key), legacyValue);

				return Task.FromResult<string?>(Encoding.UTF8.GetString(legacyValue));
			}
		}

		/// <inheritdoc/>
		public Task SetAsync(string key, string value)
		{
			ArgumentNullException.ThrowIfNull(key);
			ArgumentNullException.ThrowIfNull(value);

			lock (Locker)
			{
				var alias = ToAlias(key);
				RemoveOwnedAliasIfPresent(alias);
				if (CanUseLegacyNamespacedAlias(key))
					RemoveOwnedAliasIfPresent(ToLegacyNamespacedAlias(key));

				_repository.Save(alias, Encoding.UTF8.GetBytes(value));
				_tombstones.Remove(key);
				return Task.CompletedTask;
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Removes only the namespaced alias owned by this implementation. A tombstone suppresses
		/// fallback to a same-named legacy raw alias, which cannot be safely deleted because the
		/// secure-repository alias space is shared with unrelated application components.
		/// </remarks>
		public bool Remove(string key)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (Locker)
			{
				var removed = false;
				try
				{
					_repository.RemoveAlias(ToAlias(key));
					removed = true;
				}
				catch (InvalidOperationException)
				{
					// Missing namespaced data is expected when only an ambiguous raw alias exists.
				}

				try
				{
					if (CanUseLegacyNamespacedAlias(key))
					{
						_repository.RemoveAlias(ToLegacyNamespacedAlias(key));
						removed = true;
					}
				}
				catch (InvalidOperationException)
				{
					// Missing v1 namespaced data is expected.
				}

				_tombstones.Add(key);
				return removed;
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Removes only the aliases this API owns. Other secure-repository entries belonging to the
		/// application are left untouched. A persistent global tombstone suppresses fallback to
		/// legacy raw aliases after the clear. Tombstones intentionally accumulate: they cannot be
		/// garbage-collected while a shadowed raw alias exists because that would resurrect data.
		/// </remarks>
		public void RemoveAll()
		{
			lock (Locker)
			{
				_tombstones.AddAll();

				IEnumerable<string> aliases;
				try
				{
					aliases = _repository.GetAliases();
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
						_repository.RemoveAlias(alias);
					}
					catch
					{
						global::Tizen.Log.Info(TizenPlatform.CurrentPackageLogTag, "Failed to remove data.");
					}
				}
			}
		}

		/// <summary>
		/// Maps a caller key onto the private alias actually stored in the key manager.
		/// </summary>
		/// <param name="key">The caller's key.</param>
		/// <returns>The namespaced alias.</returns>
		/// <remarks>
		/// Version 2 encodes the UTF-8 key as unpadded Base64url, producing an injective alias with
		/// no whitespace or delimiters rejected by Tizen's secure repository. Aliases are never
		/// truncated; native alias-length or format failures are surfaced to the caller.
		/// </remarks>
		internal static string ToAlias(string key)
		{
			ArgumentNullException.ThrowIfNull(key);

			var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
				.TrimEnd('=')
				.Replace('+', '-')
				.Replace('/', '_');

			return AliasPrefix + AliasVersion + encoded;
		}

		internal static string ToLegacyNamespacedAlias(string key) =>
			AliasPrefix + TizenStorageKeyEncoding.Encode(key);

		internal static bool CanUseLegacyNamespacedAlias(string key)
		{
			foreach (var character in key)
			{
				if (char.IsWhiteSpace(character))
					return false;
			}

			return true;
		}

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

		void RemoveOwnedAliasIfPresent(string alias)
		{
			try
			{
				_repository.RemoveAlias(alias);
			}
			catch (InvalidOperationException)
			{
				// Expected when the owned alias did not exist.
			}
		}
	}

	internal interface ITizenSecureStorageTombstones
	{
		bool ContainsAll { get; }

		bool Contains(string key);

		void Add(string key);

		void Remove(string key);

		void AddAll();
	}

	sealed class TizenSecureStorageTombstones : ITizenSecureStorageTombstones
	{
		const string Prefix = "maui.tizen.securestorage.tombstone:v1:";
		const string AllKey = Prefix + "all";
		readonly ITizenPreferencesStore _store;

		public static TizenSecureStorageTombstones Instance { get; } =
			new(TizenPreferencesStore.Instance);

		internal TizenSecureStorageTombstones(ITizenPreferencesStore store)
		{
			_store = store;
		}

		public bool ContainsAll
		{
			get
			{
				lock (_store.SyncRoot)
					return _store.Contains(AllKey);
			}
		}

		public bool Contains(string key)
		{
			lock (_store.SyncRoot)
				return _store.Contains(GetKeyTombstone(key));
		}

		public void Add(string key)
		{
			lock (_store.SyncRoot)
				_store.Set(GetKeyTombstone(key), true);
		}

		public void Remove(string key)
		{
			lock (_store.SyncRoot)
			{
				var tombstone = GetKeyTombstone(key);
				if (_store.Contains(tombstone))
					_store.Remove(tombstone);
			}
		}

		public void AddAll()
		{
			lock (_store.SyncRoot)
				_store.Set(AllKey, true);
		}

		static string GetKeyTombstone(string key) =>
			Prefix + "key:" + TizenStorageKeyEncoding.Encode(key);
	}

	sealed class InMemorySecureStorageTombstones : ITizenSecureStorageTombstones
	{
		readonly HashSet<string> _keys = new(StringComparer.Ordinal);

		public bool ContainsAll { get; private set; }

		public bool Contains(string key) => _keys.Contains(key);

		public void Add(string key) => _keys.Add(key);

		public void Remove(string key) => _keys.Remove(key);

		public void AddAll() => ContainsAll = true;
	}

	internal interface ITizenSecureRepository
	{
		byte[] Get(string alias);

		void Save(string alias, byte[] value);

		void RemoveAlias(string alias);

		IEnumerable<string> GetAliases();
	}

	sealed class TizenSecureRepository : ITizenSecureRepository
	{
		public static TizenSecureRepository Instance { get; } = new();

		TizenSecureRepository()
		{
		}

		public byte[] Get(string alias) =>
			DataManager.Get(alias, null);

		public void Save(string alias, byte[] value) =>
			DataManager.Save(alias, value, new Policy());

		public void RemoveAlias(string alias) =>
			DataManager.RemoveAlias(alias);

		public IEnumerable<string> GetAliases() =>
			DataManager.GetAliases();
	}
}
