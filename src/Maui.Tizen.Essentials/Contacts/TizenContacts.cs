using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Tizen.Pims.Contacts;
using TizenAppControl = Tizen.Applications.AppControl;
using TizenAppControlData = Tizen.Applications.AppControlData;
using TizenAppControlLaunchMode = Tizen.Applications.AppControlLaunchMode;
using TizenAppControlOperations = Tizen.Applications.AppControlOperations;
using TizenAppControlReplyResult = Tizen.Applications.AppControlReplyResult;
using TizenContact = Tizen.Pims.Contacts.ContactsViews.Contact;
using TizenEmailView = Tizen.Pims.Contacts.ContactsViews.Email;
using TizenNameView = Tizen.Pims.Contacts.ContactsViews.Name;
using TizenNumberView = Tizen.Pims.Contacts.ContactsViews.Number;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IContacts"/>, backed by <c>Tizen.Pims.Contacts</c>.
	/// </summary>
	public sealed class TizenContacts : IContacts
	{
		static readonly Lazy<ContactsManager> Manager = new(static () => new ContactsManager());

		/// <inheritdoc/>
		public async Task<Contact?> PickContactAsync()
		{
			TizenPermissions.EnsureDeclared<Permissions.ContactsRead>();
			TizenPermissions.EnsureDeclared<Permissions.LaunchApp>();
			await TizenPermissions.EnsureGrantedAsync<Permissions.ContactsRead>().ConfigureAwait(false);

			var appControl = new TizenAppControl
			{
				Operation = TizenAppControlOperations.Pick,
				LaunchMode = TizenAppControlLaunchMode.Single,
				Mime = "application/vnd.tizen.contact",
			};
			appControl.ExtraData.Add(TizenAppControlData.SectionMode, "single");

			return await TizenAppControlReply.RunAsync<TizenAppControl, TizenAppControlReplyResult, Contact?>(
				callback => TizenAppControl.SendLaunchRequest(
					appControl,
					TizenAppControlReply.NativeTimeoutMilliseconds,
					(_, reply, result) => callback(reply, result)),
				static (reply, result) =>
				{
					if (result != TizenAppControlReplyResult.Succeeded ||
						!reply.ExtraData.TryGet(
							TizenAppControlData.Selected,
							out IEnumerable<string> selected) ||
						!int.TryParse(selected?.FirstOrDefault(), out var id))
					{
						return null;
					}

					using var record = Manager.Value.Database.Get(TizenContact.Uri, id);
					return record is null ? null : ToContact(record);
				}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para>
		/// Requests the runtime <c>contact.read</c> consent before reading, not merely checking that
		/// the privilege is declared. Declaration alone does not grant a Tizen privacy privilege, so
		/// the previous implementation could reach the database call and fail there instead of
		/// prompting.
		/// </para>
		/// <para>
		/// The result is materialised eagerly and the native <c>ContactsList</c> disposed before
		/// returning. Deferred iteration handed the caller a lazy sequence over an undisposed native
		/// cursor whose lifetime then depended on whether - and when - the caller finished
		/// enumerating.
		/// </para>
		/// </remarks>
		public async Task<IEnumerable<Contact>> GetAllAsync(CancellationToken cancellationToken = default)
		{
			await TizenPermissions.EnsureGrantedAsync<Permissions.ContactsRead>().ConfigureAwait(false);

			cancellationToken.ThrowIfCancellationRequested();

			var contacts = new List<Contact>();

			using var contactsList = Manager.Value.Database.GetAll(TizenContact.Uri, 0, 0);

			if (contactsList is null)
				return contacts;

			for (var i = 0; i < contactsList.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				using var record = contactsList.GetCurrentRecord();
				if (record is not null)
					contacts.Add(ToContact(record));

				contactsList.MoveNext();
			}

			return contacts;
		}

		static Contact ToContact(ContactsRecord contactsRecord)
		{
			using var nameRecord = contactsRecord.GetChildRecord(TizenContact.Name, 0);

			var phones = new List<ContactPhone>();
			var phoneCount = contactsRecord.GetChildRecordCount(TizenContact.Number);
			for (var i = 0; i < phoneCount; i++)
			{
				using var numberRecord = contactsRecord.GetChildRecord(TizenContact.Number, i);
				phones.Add(new ContactPhone(numberRecord.Get<string>(TizenNumberView.NumberData)));
			}

			var emails = new List<ContactEmail>();
			var emailCount = contactsRecord.GetChildRecordCount(TizenContact.Email);
			for (var i = 0; i < emailCount; i++)
			{
				using var emailRecord = contactsRecord.GetChildRecord(TizenContact.Email, i);
				emails.Add(new ContactEmail(emailRecord.Get<string>(TizenEmailView.Address)));
			}

			return new Contact(
				nameRecord is null ? null : nameRecord.Get<int>(TizenNameView.ContactId).ToString(System.Globalization.CultureInfo.InvariantCulture),
				nameRecord?.Get<string>(TizenNameView.Prefix),
				nameRecord?.Get<string>(TizenNameView.First),
				nameRecord?.Get<string>(TizenNameView.Addition),
				nameRecord?.Get<string>(TizenNameView.Last),
				nameRecord?.Get<string>(TizenNameView.Suffix),
				phones,
				emails);
		}

	}
}
