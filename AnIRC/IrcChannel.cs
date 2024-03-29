﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

using static AnIRC.Replies;

namespace AnIRC;

/// <summary>
/// Represents an IRC channel.
/// </summary>
public class IrcChannel : IrcMessageTarget, INamedEntity, INotifyPropertyChanged {
	public override string Target => this.Name;

	/// <summary>The name of the channel.</summary>
	public string Name { get; }
	/// <summary>The modes on the channel.</summary>
	public virtual ModeSet Modes { get; } = new ModeSet();

	private DateTime timestamp;
	/// <summary>The time the channel was created.</summary>
	public DateTime Timestamp {
		get => this.timestamp;
		set { this.timestamp = value; this.OnPropertyChanged(new(nameof(this.Timestamp))); }
	}
	private string? topic;
	/// <summary>The channel topic, or null if none is set.</summary>
	public string? Topic {
		get => this.topic;
		set { this.topic = value; this.OnPropertyChanged(new(nameof(this.Topic))); }
	}
	private string? topicSetter;
	/// <summary>The name or hostmask of the user who set the topic (whichever the server decided to send).</summary>
	public string? TopicSetter {
		get => this.topicSetter;
		set { this.topicSetter = value; this.OnPropertyChanged(new(nameof(this.TopicSetter))); }
	}
	private DateTime? topicStamp;
	/// <summary>The time in UTC when the topic was last changed, or null if not known.</summary>
	public DateTime? TopicStamp {
		get => this.topicStamp;
		set { this.topicStamp = value; this.OnPropertyChanged(new(nameof(this.TopicStamp))); }
	}
	/// <summary>The users on the channel.</summary>
	public IrcChannelUserCollection Users { get; internal set; }
	private int userLimit;
	/// <summary>The maximum number of users that can join the channel, or int.MaxValue if no limit is set.</summary>
	public int UserLimit {
		get => this.userLimit;
		set { this.userLimit = value; this.OnPropertyChanged(new(nameof(this.UserLimit))); }
	}
	private string? key;
	/// <summary>The key to the channel, or null if none is set.</summary>
	public string? Key {
		get => this.key;
		set { this.key = value; this.OnPropertyChanged(new(nameof(this.Key))); }
	}

	/// <summary>Returns the <see cref="IrcChannelUser"/> representing the local user, or null if we aren't in the list.</summary>
	public IrcChannelUser? Me {
		get {
			this.Users.TryGetValue(this.Client.Me.Nickname, out var result);
			return result;
		}
	}
	/// <summary>Returns the local user's status on the list, or null if we aren't on the list.</summary>
	public ChannelStatus? MyStatus => this.Me?.Status;

	/// <summary>
	/// Creates a new <see cref="IrcChannel"/> object with the specified name and associated with the given <see cref="IrcClient"/> object.
	/// </summary>
	/// <param name="client">The <see cref="IrcClient"/> that this channel belongs to.</param>
	/// <param name="name">The name of the channel.</param>
	public IrcChannel(IrcClient client, string name) : base(client, false) {
		this.Users = new IrcChannelUserCollection(client);
		this.Name = name;
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	internal void OnPropertyChanged(PropertyChangedEventArgs e) => this.PropertyChanged?.Invoke(this, e);

	/// <summary>
	/// Sets a list mode or status mode (v, o, b, e, etc.) on one or more users on the channel.
	/// </summary>
	/// <param name="direction">The direction of the mode change; '+' or '-'.</param>
	/// <param name="mode">The channel mode to change.</param>
	/// <param name="members">The users to affect.</param>
	public void SetMode(char direction, char mode, params string[] members) {
		var builder1 = new StringBuilder();
		var parameters = new List<string>();
		int i = 0; int count;

		while (i < members.Length) {
			count = 0;
			builder1.Append(direction);
			do {
				builder1.Append(mode);
				parameters.Add(members[i]);
				++i; ++count;
			} while (count < this.Client.Extensions.Modes && i < members.Length);
			this.Client.Send(MODE, new[] { this.Name, builder1.ToString() }.Concat(parameters));
			builder1.Clear();
			parameters.Clear();
		}
	}
	/// <summary>Removes voice from one or more users on this channel.</summary>
	/// <param name="members">The users to affect.</param>
	public void DeVoice(params string[] members) => this.SetMode('-', 'v', members);
	/// <summary>Removes half-operator status from one or more users on this channel.</summary>
	/// <param name="members">The users to affect.</param>
	public void DeHalfOp(params string[] members) => this.SetMode('-', 'h', members);
	/// <summary>Removes operator status from one or more users on this channel.</summary>
	/// <param name="members">The users to affect.</param>
	public void DeOp(params string[] members) => this.SetMode('-', 'o', members);
	/// <summary>Removes admin status from one or more users on this channel.</summary>
	/// <param name="members">The users to affect.</param>
	public void DeAdmin(params string[] members) => this.SetMode('-', 'a', members);
	/// <summary>Gives voice to one or more users on this channel.</summary>
	/// <param name="members">The users to affect.</param>
	public void Voice(params string[] members) => this.SetMode('+', 'v', members);
	/// <summary>Gives half-operator status to one or more users on this channel.</summary>
	/// <param name="members">The users to affect.</param>
	public void HalfOp(params string[] members) => this.SetMode('+', 'h', members);
	/// <summary>Gives operator status to one or more users on this channel.</summary>
	/// <param name="members">The users to affect.</param>
	public void Op(params string[] members) => this.SetMode('+', 'o', members);
	/// <summary>Gives admin status to one or more users on this channel.</summary>
	/// <param name="members">The users to affect.</param>
	public void Admin(params string[] members) => this.SetMode('+', 'a', members);
	/// <summary>Bans a hostmasks from the channel.</summary>
	/// <param name="target">The hostmask to ban.</param>
	public void Ban(string target) => this.SetMode('+', 'b', new string[] { target });
	/// <summary>Bans one or more hostmasks from the channel.</summary>
	/// <param name="targets">The hostmasks to ban.</param>
	public void Ban(string[] targets) => this.SetMode('+', 'b', targets);
	/// <summary>Joins this channel.</summary>
	public void Join() => this.Client.Send("JOIN", this.Name);
	/// <summary>Joins this channel using the specified key.</summary>
	/// <param name="key">The channel key.</param>
	public void Join(string key) => this.Client.Send("JOIN", this.Name, key);
	/// <summary>Kicks a user out of this channel.</summary>
	/// <param name="target">The user to kick.</param>
	public void Kick(string target) => this.Client.Send("KICK", this.Name, target);
	/// <summary>Kicks a user out of this channel with the specified message.</summary>
	/// <param name="target">The user to kick.</param>
	/// <param name="message">The kick message.</param>
	public void Kick(string target, string message) => this.Client.Send("KICK", this.Name, target, message);
	/// <summary>Kicks one or more users out of this channel.</summary>
	/// <param name="targets">The users to kick.</param>
	public void Kick(string[] targets) => this.Client.Send("KICK", this.Name, string.Join(",", targets));
	/// <summary>Kicks one or more users out of this channel with the specified message.</summary>
	/// <param name="targets">The users to kick.</param>
	/// <param name="message">The kick message.</param>
	public void Kick(string[] targets, string message) => this.Client.Send("KICK", this.Name, string.Join(",", targets), message);
	/// <summary>Leaves this channel.</summary>
	public void Part() => this.Client.Send("PART", this.Name);
	/// <summary>Leaves this channel with the specified message.</summary>
	/// <param name="message">The part message.</param>
	public void Part(string message) => this.Client.Send("PART", this.Name, message);
	/// <summary>Sends a message to this channel.</summary>
	/// <param name="message">What to say.</param>
	public override void Say(string message) => this.Client.Send("PRIVMSG", this.Name, message);
	/// <summary>Sends a notice to this channel.</summary>
	/// <param name="message">What to say.</param>
	public override void Notice(string message) => this.Client.Send("NOTICE", this.Name, message);
	/// <summary>Sets a ban exception on a hostmask. Not all IRC networks support ban exceptions.</summary>
	/// <param name="targets">The hostmask to exempt.</param>
	public void BanExcept(string target) => this.BanExcept(new string[] { target });
	/// <summary>Sets one or more ban exceptions. Not all IRC networks support ban exceptions.</summary>
	/// <param name="targets">The hostmasks to exempt.</param>
	public void BanExcept(string[] targets) {
		if (!this.Client.Extensions.SupportsBanExceptions) throw new NotSupportedException("The IRC network does not support ban exceptions.");
		this.SetMode('+', this.Client.Extensions.BanExceptionsMode, targets);
	}
	/// <summary>Sets an invite exception on a hostmask. Not all IRC networks support invite exceptions.</summary>
	/// <param name="targets">The hostmask to exempt.</param>
	public void InviteExcept(string target) => this.InviteExcept(new string[] { target });
	/// <summary>Sets one or more invite exceptions. Not all IRC networks support invite exceptions.</summary>
	/// <param name="targets">The hostmasks to exempt.</param>
	public void InviteExcept(string[] targets) {
		if (!this.Client.Extensions.SupportsInviteExceptions) throw new NotSupportedException("The IRC network does not support invite exceptions.");
		this.SetMode('+', this.Client.Extensions.InviteExceptionsMode, targets);
	}
	/// <summary>Unbans a hostmask from the channel.</summary>
	/// <param name="targets">The hostmask to unban.</param>
	public void Unban(string target) => this.Unban(new string[] { target });
	/// <summary>Unbans one or more hostmasks from the channel.</summary>
	/// <param name="targets">The hostmasks to unban.</param>
	public void Unban(string[] targets) => this.SetMode('-', 'b', targets);
	/// <summary>Removes a ban exception. Not all IRC networks support ban exceptions.</summary>
	/// <param name="targets">The hostmask to remove an exempt for.</param>
	public void BanUnExcept(string target) => this.BanUnExcept(new string[] { target });
	/// <summary>Removes one or more ban exceptions. Not all IRC networks support ban exceptions.</summary>
	/// <param name="targets">The hostmasks to remove an exempt for.</param>
	public void BanUnExcept(string[] targets) {
		if (!this.Client.Extensions.SupportsBanExceptions) throw new NotSupportedException("The IRC network does not support ban exceptions.");
		this.SetMode('-', this.Client.Extensions.BanExceptionsMode, targets);
	}
	/// <summary>Removes an invite exception. Not all IRC networks support invite exceptions.</summary>
	/// <param name="targets">The hostmask to remove an exempt for.</param>
	public void InviteUnExcept(string target) => this.InviteUnExcept(new string[] { target });
	/// <summary>Removes one or more invite exceptions. Not all IRC networks support invite exceptions.</summary>
	/// <param name="targets">The hostmasks to remove an exempt for.</param>
	public void InviteUnExcept(string[] targets) {
		if (!this.Client.Extensions.SupportsInviteExceptions) throw new NotSupportedException("The IRC network does not support invite exceptions.");
		this.SetMode('-', this.Client.Extensions.InviteExceptionsMode, targets);
	}
	/// <summary>Quiets a hostmask. Not all IRC networks support quiet bans.</summary>
	/// <param name="targets">The hostmask to quiet.</param>
	public void Quiet(string target) => this.Quiet(new string[] { target });
	/// <summary>Quiets one or more hostmasks. Not all IRC networks support quiet bans.</summary>
	/// <param name="targets">The hostmasks to quiet.</param>
	public void Quiet(string[] targets) {
		if (this.Client.Extensions.ChanModes.ModeType('q') != 'A') throw new NotSupportedException("The IRC network does not support quiet bans.");
		this.SetMode('+', 'q', targets);
	}
	/// <summary>Unquiets a hostmask. Not all IRC networks support quiet bans.</summary>
	/// <param name="targets">The hostmask to unquiet.</param>
	public void UnQuiet(string target) => this.UnQuiet(new string[] { target });
	/// <summary>Unquiets one or more hostmasks. Not all IRC networks support quiet bans.</summary>
	/// <param name="targets">The hostmasks to unquiet.</param>
	public void UnQuiet(string[] targets) {
		if (this.Client.Extensions.ChanModes.ModeType('q') != 'A') throw new NotSupportedException("The IRC network does not support quiet bans.");
		this.SetMode('-', 'q', targets);
	}

	public override string ToString() => $"{{{this.Name}}}";
}
