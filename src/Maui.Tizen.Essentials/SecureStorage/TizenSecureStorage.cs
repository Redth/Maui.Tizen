using System;
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
	public sealed class TizenSecureStorage : ISecureStorage
	{
		/// <inheritdoc/>
		public Task<string?> GetAsync(string key)
		{
			ArgumentNullException.ThrowIfNull(key);

			try
			{
				// The second parameter is the data password, not a default value.
				return Task.FromResult<string?>(Encoding.UTF8.GetString(DataManager.Get(key, null)));
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

			try
			{
				try
				{
					// DataManager.Save throws when the alias already exists, and Tizen offers no
					// existence probe that does not throw, so remove unconditionally first.
					DataManager.RemoveAlias(key);
				}
				catch
				{
					// Expected when the alias did not exist.
				}

				DataManager.Save(key, Encoding.UTF8.GetBytes(value), new Policy());

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
				DataManager.RemoveAlias(key);
				return true;
			}
			catch
			{
				global::Tizen.Log.Info(TizenPlatform.CurrentPackageLogTag, "Failed to remove data.");
				return false;
			}
		}

		/// <inheritdoc/>
		public void RemoveAll()
		{
			try
			{
				foreach (var alias in DataManager.GetAliases())
					DataManager.RemoveAlias(alias);
			}
			catch
			{
				global::Tizen.Log.Info(TizenPlatform.CurrentPackageLogTag, "No saved data.");
			}
		}
	}
}
