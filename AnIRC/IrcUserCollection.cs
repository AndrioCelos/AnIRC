namespace AnIRC {
	/// <summary>
	/// Represents a read-only list of users on IRC.
	/// </summary>
	/// <seealso cref="IrcUser"/>
	public class IrcUserCollection : IrcNamedEntityCollection<IrcUser> {
		/// <summary>Initializes a new <see cref="IrcUserCollection"/> belonging to the specified <see cref="IrcClient"/>.</summary>
		protected internal IrcUserCollection(IrcClient client) : base(client) { }

		internal IrcUser Get(string mask, bool add)
			=> this.Get(Hostmask.GetNickname(mask), Hostmask.GetIdent(mask), Hostmask.GetHost(mask), null, null, false, add);
		internal IrcUser GetFromExtendedJoin(string mask, string? account, string? fullName, bool add)
			=> this.Get(Hostmask.GetNickname(mask), Hostmask.GetIdent(mask), Hostmask.GetHost(mask), account, fullName, false, add);
		internal IrcUser Get(string nickname, string? ident, string? host, bool add)
			=> this.Get(nickname, ident, host, null, null, false, add);
		internal IrcUser GetFromMonitor(string mask, bool add)
			=> this.Get(Hostmask.GetNickname(mask), Hostmask.GetIdent(mask), Hostmask.GetHost(mask), null, null, true, add);
		internal IrcUser GetFromMonitor(string nickname, string? ident, string? host, bool add)
			=> this.Get(nickname, ident, host, null, null, true, add);
		/// <summary>
		/// Returns the <see cref="IrcUser"/> object representing the user with the specified nickname, creating one if necessary,
		/// and updates its details with the specified ones.
		/// </summary>
		internal IrcUser Get(string nickname, string? ident, string? host, string? account, string? fullName, bool monitoring, bool add) {
			if (this.TryGetValue(nickname, out var user)) {
				if (ident is not null and not "*") user.Ident = ident;
				if (host is not null and not "*") user.Host = host;
				if (account is not null) user.Account = account == "*" ? null : account;
				if (fullName is not null) user.FullName = fullName;
			} else {
				user = new IrcUser(this.Client, nickname, ident, host, account == "*" ? null : account, fullName ?? nickname) { Monitoring = monitoring };
				if (add) {
					this.Add(user);
					this.Client.OnUserAppeared(new IrcUserEventArgs(user));
				}
			}
			return user;
		}
	}
}

