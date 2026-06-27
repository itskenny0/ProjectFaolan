using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Faolan.Core.Data;
using Faolan.Core.Network;
using Microsoft.EntityFrameworkCore;

namespace Faolan.Core.Database
{
	public interface IDatabaseRepository
	{
		IDatabaseContext Context { get; }

		Task<bool> CheckLogin(string username, string password);

		Task<bool> UpdateLastInfo(Account account, INetworkClient client);
		Task<bool> UpdateLastInfo(Character character, INetworkClient client);

		Task<bool> UpdateClientInstance(Account account, uint characterId);

		Task<Account> GetAccount(uint id);
		Task<Account> GetAccount(string userName);

		Task<Character> GetCharacter(uint id);
		Task<Character[]> GetCharactersByAccount(uint id, bool skipUninitialized = true);
		Task<Character> CreateCharacter(uint accountId, uint realmId);
		Task<bool> UpdateCharacterPosition(uint id, Vector3? position = null, Vector3? rotation = null);

		Task<Realm> GetRealm(uint id);

		Task<Map> GetMap(uint id);

		Task<Spell> GetSpell(uint id);
	}

	public class DatabaseRepository : IDatabaseRepository
	{
		// A single DbContext is shared by every listener for the whole process (the listeners are
		// singletons; the scoped repository resolves once from the root scope). A real client holds its
		// PlayerAgent + AgentServer + GameServer connections open at the same time and their packet
		// handlers run concurrently on threadpool threads — and DbContext is NOT thread-safe ("A second
		// operation was started on this context instance"). Serialize every context access through this
		// gate so concurrent handlers can't corrupt the context. DB work here is tiny and infrequent
		// (login / char-list / enter-world), so serializing it has no meaningful cost.
		private readonly SemaphoreSlim _gate = new(1, 1);

		public DatabaseRepository(IDatabaseContext databaseContext)
		{
			Context = databaseContext;
		}

		public IDatabaseContext Context { get; }

		private async Task<T> Guarded<T>(Func<Task<T>> op)
		{
			await _gate.WaitAsync();
			try
			{
				return await op();
			}
			finally
			{
				_gate.Release();
			}
		}

		public Task<bool> CheckLogin(string username, string password)
		{
			username = username.ToLower(); // skip check password for now
			return Guarded(() => Context.Accounts.AnyAsync(a => a.UserName.ToLower() == username));
		}

		public Task<bool> UpdateLastInfo(Account account, INetworkClient client)
		{
			return Guarded(async () =>
			{
				account.LastConnection = DateTime.UtcNow;
				account.LastIpAddress = client.IpAddress;
				return await Context.SaveChangesAsync() > 0;
			});
		}

		public Task<bool> UpdateLastInfo(Character character, INetworkClient client)
		{
			return Guarded(async () =>
			{
				character.LastConnection = DateTime.UtcNow;
				character.LastIpAddress = client.IpAddress;
				return await Context.SaveChangesAsync() > 0;
			});
		}

		public Task<bool> UpdateClientInstance(Account account, uint characterId)
		{
			return Guarded(async () =>
			{
				if (account.ClientInstance == characterId)
					return true;

				account.ClientInstance = characterId;
				return await Context.SaveChangesAsync() > 0;
			});
		}

		public Task<Account> GetAccount(uint id)
		{
			return Guarded(() => Context.Accounts.FirstOrDefaultAsync(a => a.Id == id));
		}

		public Task<Account> GetAccount(string userName)
		{
			return Guarded(() => Context.Accounts.FirstOrDefaultAsync(a => a.UserName == userName));
		}

		public Task<Character> GetCharacter(uint id)
		{
			return Guarded(() => Context.Characters.FirstOrDefaultAsync(a => a.Id == id));
		}

		public Task<Character[]> GetCharactersByAccount(uint id, bool skipUninitialized = true)
		{
			return Guarded(() =>
			{
				var q = Context.Characters.Where(a => a.AccountId == id);
				if (skipUninitialized)
					q = q.Where(c => c.Name != null);

				return q.ToArrayAsync();
			});
		}

		public Task<Character> CreateCharacter(uint accountId, uint realmId)
		{
			return Guarded(async () =>
			{
				var character = await Context.Characters.FirstOrDefaultAsync(c => c.AccountId == accountId && c.Name == null);
				if (character == null)
				{
					character = new Character
					{
						AccountId = accountId,
						RealmId = realmId,
						CreationDate = DateTime.UtcNow
					};

					// ReSharper disable once MethodHasAsyncOverload
					Context.Characters.Add(character);
					await Context.SaveChangesAsync();
				}

				return character;
			});
		}

		public Task<bool> UpdateCharacterPosition(uint id, Vector3? position = null, Vector3? rotation = null)
		{
			return Guarded(async () =>
			{
				var character = await Context.Characters.FirstOrDefaultAsync(c => c.Id == id);
				if (character == null)
					return false;

				if (position.HasValue)
					character.Position = position.Value;

				if (rotation.HasValue)
					character.Rotation = rotation.Value;

				return await Context.SaveChangesAsync() > 0;
			});
		}

		public Task<Realm> GetRealm(uint id)
		{
			return Guarded(() => Context.Realms.FirstOrDefaultAsync(r => r.Id == id));
		}

		public Task<Map> GetMap(uint id)
		{
			return Guarded(() => Context.Maps.FirstOrDefaultAsync(m => m.Id == id));
		}

		public Task<Spell> GetSpell(uint id)
		{
			return Guarded(() => Context.Spells.FirstOrDefaultAsync(s => s.Id == id));
		}
	}
}
