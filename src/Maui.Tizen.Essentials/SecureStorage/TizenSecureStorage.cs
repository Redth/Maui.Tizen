using System;
using System.Collections.Generic;
using System.Linq;
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
		static readonly UTF8Encoding StrictUtf8 = new(false, true);
		const string AliasVersion = "~v2~";
		const string TransactionVersion = "~tx~";
		readonly ITizenSecureRepository _repository;
		readonly ITizenSecureStorageTombstones _tombstones;
		readonly Func<string?> _currentPackageId;

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
			: this(
				TizenSecureRepository.Instance,
				TizenSecureStorageTombstones.Instance,
				static () => TizenPlatform.CurrentPackageId)
		{
		}

		internal TizenSecureStorage(
			ITizenSecureRepository repository,
			ITizenSecureStorageTombstones? tombstones = null,
			Func<string?>? currentPackageId = null)
		{
			_repository = repository;
			_tombstones = tombstones ?? new InMemorySecureStorageTombstones();
			_currentPackageId = currentPackageId ?? (static () => null);
		}

		/// <inheritdoc/>
		public Task<string?> GetAsync(string key)
		{
			ArgumentNullException.ThrowIfNull(key);

			lock (Locker)
			{
				if (_tombstones.Contains(key))
					return Task.FromResult<string?>(null);

				RecoverStagedValue(key);

				try
				{
					// The second parameter is the data password, not a default value.
					return Task.FromResult<string?>(Encoding.UTF8.GetString(_repository.Get(ToAlias(key))));
				}
				catch (InvalidOperationException)
				{
					// The namespaced alias does not exist.
				}

				byte[] legacyValue;
				string? migratedOwnedAlias = null;

				try
				{
					if (!CanUseLegacyNamespacedAlias(key))
						throw new InvalidOperationException();

					migratedOwnedAlias = ToLegacyNamespacedAlias(key);
					legacyValue = _repository.Get(migratedOwnedAlias);
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
					catch (ArgumentException)
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
					catch (ArgumentException)
					{
						return Task.FromResult<string?>(null);
					}
				}

				// Legacy namespaced and raw aliases are copied into v2. Raw aliases are unowned and
				// may belong to another component, so they are never deleted.
				_repository.Save(ToAlias(key), legacyValue);
				if (migratedOwnedAlias is not null)
				{
					try
					{
						_repository.RemoveAlias(migratedOwnedAlias);
					}
					catch (Exception)
					{
						// The v2 copy is committed and wins. A duplicate v1 alias is harmless and
						// can be cleaned by the next Set/Remove without failing this read.
					}
				}

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
				RecoverStagedValue(key);
				ReplaceOwnedAliases(
					key,
					alias,
					Encoding.UTF8.GetBytes(value));
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
				var currentAlias = ToAlias(key);
				// Commit deletion intent before touching repository aliases. Even if a native
				// deletion fails, subsequent reads cannot expose the old current/staged value.
				_tombstones.Add(key);
				var removed = false;
				var failures = new List<Exception>();
				removed |= RemoveForDeletion(currentAlias, failures);
				if (CanUseLegacyNamespacedAlias(key))
					removed |= RemoveForDeletion(ToLegacyNamespacedAlias(key), failures);

				foreach (var stagedAlias in GetStagedAliases(key).ToList())
					removed |= RemoveForDeletion(stagedAlias, failures);

				ThrowDeletionFailures(failures);
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

				var aliases = _repository.GetAliases().ToList();

				var currentPackageId = _currentPackageId();
				var failures = new List<Exception>();
				foreach (var alias in aliases)
				{
					if (!IsOwnedAlias(alias, currentPackageId))
						continue;

					RemoveForDeletion(alias, failures);
				}

				ThrowDeletionFailures(failures);
			}
		}

		/// <summary>
		/// Maps a caller key onto the private alias actually stored in the key manager.
		/// </summary>
		/// <param name="key">The caller's key.</param>
		/// <returns>The namespaced alias.</returns>
		/// <remarks>
		/// Version 2 uses a <c>~v2~</c> discriminator that version 1's encoder can only emit escaped,
		/// followed by strict UTF-8 encoded as unpadded Base64url. This makes aliases injective both
		/// within and across versions. Ill-formed UTF-16 is rejected before repository mutation.
		/// Aliases are never truncated; native alias-length or format failures are surfaced.
		/// </remarks>
		internal static string ToAlias(string key)
		{
			ArgumentNullException.ThrowIfNull(key);

			var encoded = Convert.ToBase64String(StrictUtf8.GetBytes(key))
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
		/// Tizen may return aliases qualified with the owning package id (<c>pkgid alias</c>).
		/// Qualified aliases are accepted only when that owner exactly equals the current package;
		/// another package can use the same raw prefix without transferring ownership.
		/// </remarks>
		internal static bool IsOwnedAlias(string alias, string? currentPackageId = null)
		{
			if (string.IsNullOrEmpty(alias))
				return false;

			if (alias.StartsWith(AliasPrefix, StringComparison.Ordinal))
				return true;

			var separator = alias.IndexOf(' ');
			if (separator <= 0 || separator == alias.Length - 1)
				return false;

			return !string.IsNullOrEmpty(currentPackageId) &&
				alias.AsSpan(0, separator).Equals(currentPackageId, StringComparison.Ordinal) &&
				alias.AsSpan(separator + 1).StartsWith(AliasPrefix, StringComparison.Ordinal);
		}

		void ReplaceOwnedAliases(string key, string alias, byte[] value)
		{
			var legacyAlias = CanUseLegacyNamespacedAlias(key)
				? ToLegacyNamespacedAlias(key)
				: null;
			var hadCurrent = TryGet(alias, out var previousCurrent);
			byte[]? previousLegacy = null;
			var hadLegacy = legacyAlias is not null && TryGet(legacyAlias, out previousLegacy);
			var stagedAlias = GetTransactionPrefix(key) + Guid.NewGuid().ToString("N");

			// Stage first. If this fails, every prior alias is untouched.
			_repository.Save(stagedAlias, value);

			try
			{
				RemoveOwnedAliasIfPresent(alias);
				if (legacyAlias is not null)
					RemoveOwnedAliasIfPresent(legacyAlias);

				_repository.Save(alias, value);
			}
			catch (Exception commitFailure)
			{
				var restoreFailures = new List<Exception>();
				Restore(alias, hadCurrent, previousCurrent, restoreFailures);
				if (legacyAlias is not null)
					Restore(legacyAlias, hadLegacy, previousLegacy, restoreFailures);

				if (restoreFailures.Count == 0)
				{
					RemoveOwnedAliasIfPresent(stagedAlias);
					throw;
				}

				// The staged new value is deliberately retained when restoration fails. Its
				// versioned key lets the next read recover rather than silently lose both values.
				throw new AggregateException(
					"SecureStorage replacement failed and one or more previous aliases could not be restored. " +
					"The staged value was retained for recovery.",
					new[] { commitFailure }.Concat(restoreFailures));
			}

			RemoveOwnedAliasIfPresent(stagedAlias);
		}

		void RecoverStagedValue(string key)
		{
			var alias = ToAlias(key);
			var staged = GetStagedAliases(key).ToList();
			if (staged.Count == 0)
				return;

			if (!TryGet(alias, out _))
			{
				// Multiple staged aliases can only result from interrupted retries. The repository's
				// enumeration order is not a commit order, so fail closed rather than choosing one.
				if (staged.Count != 1)
				throw new InvalidOperationException(
					$"Multiple interrupted SecureStorage replacements exist for '{key}'.");

				_repository.Save(alias, _repository.Get(staged[0]));
			}

			foreach (var stagedAlias in staged)
				RemoveOwnedAliasIfPresent(stagedAlias);
		}

		IEnumerable<string> GetStagedAliases(string key)
		{
			var prefix = GetTransactionPrefix(key);
			var currentPackageId = _currentPackageId();
			foreach (var qualifiedAlias in _repository.GetAliases())
			{
				if (!TryGetOwnedRawAlias(qualifiedAlias, currentPackageId, out var rawAlias))
					continue;
				if (rawAlias.StartsWith(prefix, StringComparison.Ordinal))
					yield return qualifiedAlias;
			}
		}

		static bool TryGetOwnedRawAlias(
			string alias,
			string? currentPackageId,
			out string rawAlias)
		{
			if (alias.StartsWith(AliasPrefix, StringComparison.Ordinal))
			{
				rawAlias = alias;
				return true;
			}

			var separator = alias.IndexOf(' ');
			if (separator > 0 &&
				!string.IsNullOrEmpty(currentPackageId) &&
				alias.AsSpan(0, separator).Equals(currentPackageId, StringComparison.Ordinal) &&
				alias.AsSpan(separator + 1).StartsWith(AliasPrefix, StringComparison.Ordinal))
			{
				rawAlias = alias[(separator + 1)..];
				return true;
			}

			rawAlias = string.Empty;
			return false;
		}

		static string GetTransactionPrefix(string key) =>
			AliasPrefix + TransactionVersion +
			Convert.ToBase64String(StrictUtf8.GetBytes(key))
				.TrimEnd('=')
				.Replace('+', '-')
				.Replace('/', '_') +
			"~";

		bool TryGet(string alias, out byte[] value)
		{
			try
			{
				value = _repository.Get(alias);
				return true;
			}
			catch (InvalidOperationException)
			{
				value = [];
				return false;
			}
		}

		void Restore(
			string alias,
			bool existed,
			byte[]? value,
			ICollection<Exception> failures)
		{
			if (!existed || value is null)
				return;

			try
			{
				if (!TryGet(alias, out _))
					_repository.Save(alias, value);
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}
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

		bool RemoveForDeletion(string alias, ICollection<Exception> failures)
		{
			try
			{
				_repository.RemoveAlias(alias);
				return true;
			}
			catch (InvalidOperationException)
			{
				// Missing aliases are expected during idempotent removal.
				return false;
			}
			catch (Exception exception)
			{
				failures.Add(exception);
				return false;
			}
		}

		static void ThrowDeletionFailures(IReadOnlyCollection<Exception> failures)
		{
			if (failures.Count == 1)
				throw failures.First();
			if (failures.Count > 1)
				throw new AggregateException("One or more SecureStorage aliases could not be removed.", failures);
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
		const string LivePrefix = Prefix + "live:";
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
			{
				if (_store.Contains(GetKeyTombstone(key)))
					return true;

				return _store.Contains(AllKey) && !_store.Contains(GetLiveMarker(key));
			}
		}

		public void Add(string key)
		{
			lock (_store.SyncRoot)
			{
				var live = GetLiveMarker(key);
				if (_store.Contains(live))
					_store.Remove(live);
				_store.Set(GetKeyTombstone(key), true);
			}
		}

		public void Remove(string key)
		{
			lock (_store.SyncRoot)
			{
				var tombstone = GetKeyTombstone(key);
				if (_store.Contains(tombstone))
					_store.Remove(tombstone);

				var live = GetLiveMarker(key);
				if (_store.Contains(AllKey))
					_store.Set(live, true);
				else if (_store.Contains(live))
					_store.Remove(live);
			}
		}

		public void AddAll()
		{
			lock (_store.SyncRoot)
			{
				_store.Set(AllKey, true);
				foreach (var key in _store.Keys
					.Where(key => key.StartsWith(LivePrefix, StringComparison.Ordinal))
					.ToList())
				{
					_store.Remove(key);
				}
			}
		}

		static string GetKeyTombstone(string key) =>
			Prefix + "key:" + TizenStorageKeyEncoding.Encode(key);

		static string GetLiveMarker(string key) =>
			LivePrefix + TizenStorageKeyEncoding.Encode(key);
	}

	sealed class InMemorySecureStorageTombstones : ITizenSecureStorageTombstones
	{
		readonly HashSet<string> _keys = new(StringComparer.Ordinal);
		readonly HashSet<string> _liveAfterAll = new(StringComparer.Ordinal);

		public bool ContainsAll { get; private set; }

		public bool Contains(string key) =>
			_keys.Contains(key) || (ContainsAll && !_liveAfterAll.Contains(key));

		public void Add(string key)
		{
			_liveAfterAll.Remove(key);
			_keys.Add(key);
		}

		public void Remove(string key)
		{
			_keys.Remove(key);
			if (ContainsAll)
				_liveAfterAll.Add(key);
			else
				_liveAfterAll.Remove(key);
		}

		public void AddAll()
		{
			ContainsAll = true;
			_liveAfterAll.Clear();
		}
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
		readonly Func<IEnumerable<string>> _getAliases;

		public static TizenSecureRepository Instance { get; } =
			new(DataManager.GetAliases);

		internal TizenSecureRepository(Func<IEnumerable<string>> getAliases)
		{
			_getAliases = getAliases;
		}

		public byte[] Get(string alias) =>
			DataManager.Get(alias, null);

		public void Save(string alias, byte[] value) =>
			DataManager.Save(alias, value, new Policy());

		public void RemoveAlias(string alias) =>
			DataManager.RemoveAlias(alias);

		public IEnumerable<string> GetAliases() =>
			NormalizeAliases(_getAliases);

		internal static IReadOnlyList<string> NormalizeAliases(
			Func<IEnumerable<string>> getAliases)
		{
			try
			{
				return getAliases().ToArray();
			}
			catch (ArgumentException exception) when (exception.ParamName is null)
			{
				// API15 documents ArgumentException from this parameterless call specifically as
				// "there's no alias to get". An ArgumentException naming a parameter is not that
				// sentinel and must propagate as a genuine repository failure.
				return [];
			}
		}
	}
}
