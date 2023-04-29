using System;
using System.Diagnostics;

namespace AnIRC;

/// <summary>
/// Represents a user's presence on a channel.
/// </summary>
[DebuggerDisplay("Count = {Count}")]
public class IrcChannelUser : INamedEntity {
	/// <summary>The <see cref="IrcClient"/> that this <see cref="IrcChannelUser"/> belongs to.</summary>
	public IrcClient Client { get; }
	/// <summary>The <see cref="IrcChannel"/> that this <see cref="IrcChannelUser"/> belongs to.</summary>
	public IrcChannel Channel { get; }
	/// <summary>The user's nickname.</summary>
	public string Nickname { get; internal set; }
	/// <summary>The user's access level on the channel.</summary>
	public ChannelStatus Status { get; internal set; }
	/// <summary>The time in UTC when the user last spoke in the channel, or null if they haven't yet spoken.</summary>
	public DateTime? LastActive { get; internal set; }

	/// <summary>The time in UTC when the user joined the channel, or null if not known.</summary>
	public DateTime? JoinTime { get; internal set; }

	/// <summary>Returns true if this user is the local user for its <see cref="IrcClient"/> object.</summary>
	public bool IsMe => this.Client != null && this.Client.CaseMappingComparer.Equals(this.Nickname, this.Client.Me.Nickname);

	/// <summary>Creates a <see cref="IrcChannelUser"/> object representing the specified user.</summary>
	/// <param name="nickname">The user's nickname.</param>
	/// <param name="client">The IRC client that this <see cref="IrcChannelUser"/> belongs to.</param>
	public IrcChannelUser(IrcClient client, IrcChannel channel, string nickname) : this(client, channel, nickname, new ChannelStatus(client)) { }
	/// <summary>Creates a <see cref="IrcChannelUser"/> object representing the specified user with the specified status.</summary>
	/// <param name="nickname">The user's nickname.</param>
	/// <param name="client">The IRC client that this <see cref="IrcChannelUser"/> belongs to.</param>
	/// <param name="status">The status that this user has.</param>
	public IrcChannelUser(IrcClient client, IrcChannel channel, string nickname, ChannelStatus status) {
		this.Nickname = nickname;
		this.Channel = channel;
		this.Client = client;
		this.Status = status;
	}

	/// <summary>Returns the User object that represents this user.</summary>
	public IrcUser User => this.Client.Users.TryGetValue(this.Nickname, out var user) ? user : new(this.Client, this.Nickname, "*", "*", null, this.Nickname);

	string INamedEntity.Name => this.Nickname;

	/// <summary>Returns the user's nickname and status prefixes.</summary>
	public override string ToString() => this.Status.GetPrefixes() + this.Nickname;
}
