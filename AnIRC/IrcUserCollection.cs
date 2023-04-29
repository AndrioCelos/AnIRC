using System.Collections.Generic;
using System.Diagnostics;

namespace AnIRC;

/// <summary>
/// Represents a read-only list of users on IRC.
/// </summary>
/// <seealso cref="IrcUser"/>
[DebuggerDisplay("Count = {Count}")]
public class IrcUserCollection : IrcNamedEntityCollection<IrcUser> {
	/// <summary>Initializes a new <see cref="IrcUserCollection"/> belonging to the specified <see cref="IrcClient"/>.</summary>
	protected internal IrcUserCollection(IrcClient client) : base(client) { }

	internal IrcUser Get(string mask, bool add)
		=> this.Get(Hostmask.GetNickname(mask), Hostmask.GetIdent(mask), Hostmask.GetHost(mask), null, null, null, false, add);
	internal IrcUser Get(string mask, IReadOnlyDictionary<string, string>? tags, bool add)
		=> this.Get(Hostmask.GetNickname(mask), Hostmask.GetIdent(mask), Hostmask.GetHost(mask), null, null, tags, false, add);

	internal IrcUser GetFromExtendedJoin(string mask, string? account, string? fullName, bool add)
		=> this.Get(Hostmask.GetNickname(mask), Hostmask.GetIdent(mask), Hostmask.GetHost(mask), account, fullName, null, false, add);
	internal IrcUser GetFromExtendedJoin(string mask, string? account, string? fullName, IReadOnlyDictionary<string, string>? tags, bool add)
		=> this.Get(Hostmask.GetNickname(mask), Hostmask.GetIdent(mask), Hostmask.GetHost(mask), account, fullName, tags, false, add);
	internal IrcUser Get(string nickname, string? ident, string? host, bool add)
		=> this.Get(nickname, ident, host, null, null, null, false, add);
	internal IrcUser GetFromMonitor(string mask, bool add)
		=> this.Get(Hostmask.GetNickname(mask), Hostmask.GetIdent(mask), Hostmask.GetHost(mask), null, null, null, true, add);
	internal IrcUser GetFromMonitor(string nickname, string? ident, string? host, bool add)
		=> this.Get(nickname, ident, host, null, null, null, true, add);
	/// <summary>
	/// Returns the <see cref="IrcUser"/> object representing the user with the specified nickname, creating one if necessary,
	/// and updates its details with the specified ones.
	/// </summary>
	internal IrcUser Get(string nickname, string? ident, string? host, string? account, string? fullName, IReadOnlyDictionary<string, string>? tags, bool monitoring, bool add) {
		if (this.TryGetValue(nickname, out var user)) {
			if (ident is not null and not "*") user.Ident = ident;
			if (host is not null and not "*") user.Host = host;
			if (account is not null) user.Account = account == "*" ? null : account;
			if (fullName is not null) user.FullName = fullName;
		} else {
			user = new IrcUser(this.Client, nickname, ident ?? "*", host ?? "*", account == "*" ? null : account, fullName ?? nickname) { IsMonitored = monitoring };
			if (add) {
				this.Add(user);
				this.Client.OnUserAppeared(new(user));
			}
		}
		if (tags is not null && tags.Count != 0) {
			if (account is null && tags.TryGetValue("account", out account)) user.Account = account;
		}
		return user;
	}
}

