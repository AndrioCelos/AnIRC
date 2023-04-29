using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace AnIRC;

public class AwayMessageEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname in the message.</summary>
	public string Nickname { get; }
	/// <summary>Returns the user's away message.</summary>
	public string Reason { get; }

	public AwayMessageEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string reason) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.Reason = reason;
	}
}

public class CapabilitiesEventArgs : IrcLineEventArgs {
	/// <summary>A set of capabilities that are available.</summary>
	public IrcNamedEntityCollection<IrcCapability> Capabilities { get; }

	public CapabilitiesEventArgs(IrcLine line, bool matchesAsyncRequests, IrcNamedEntityCollection<IrcCapability> capabilities) : base(line, matchesAsyncRequests)
		=> this.Capabilities = capabilities;
}

public class CapabilitiesAddedEventArgs : CapabilitiesEventArgs {
	/// <summary>A set of names of capabilities that should be enabled.</summary>
	public ISet<string> EnableCapabilities { get; }

	public CapabilitiesAddedEventArgs(IrcLine line, bool matchesAsyncRequests, IrcNamedEntityCollection<IrcCapability> capabilities, ISet<string> enableCapabilities)
		: base(line, matchesAsyncRequests, capabilities) => this.EnableCapabilities = enableCapabilities;

	/// <summary>Adds the specified capability to the list of capabilities to be enabled if it is supported by the server.</summary>
	/// <returns>True if the capability is supported; false otherwise.</returns>
	public bool EnableIfSupported(string name) {
		if (this.Capabilities.Contains(name)) {
			this.EnableCapabilities.Add(name);
			return true;
		}
		return false;
	}
	/// <summary>Adds those capabilities from the specified enumerable that are supported by the server to the list of capabilities to be enabled.</summary>
	/// <returns>An <see cref="ISet{T}"/> of <see cref="string"/> containing names of capaibilities that were enabled.</returns>
	public ISet<string> EnableIfSupported(IEnumerable<string> names) {
		var enabled = new HashSet<string>();
		foreach (var name in names) {
			if (this.Capabilities.Contains(name)) {
				this.EnableCapabilities.Add(name);
				enabled.Add(name);
			}
		}
		return enabled;
	}
	/// <summary>Adds those capabilities from the specified array that are supported by the server to the list of capabilities to be enabled.</summary>
	/// <returns>An <see cref="ISet{T}"/> of <see cref="string"/> containing names of capaibilities that were enabled.</returns>
	public ISet<string> EnableIfSupported(params string[] names) => this.EnableIfSupported((IEnumerable<string>) names);
}

public class ChannelChangeEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that is affected.</summary>
	public IrcChannel Channel { get; }

	public ChannelChangeEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
	}
}

public class ChannelJoinEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who is joining.</summary>
	public IrcUser Sender { get; }
	/// Returns an <see cref="IrcChannel"/> object representing the channel that is affected.
	public IrcChannel Channel { get; }
	/// <summary>If the local user joined a channel, returns a <see cref="Task"/> that will complete when the NAMES list is received.</summary>
	public Task? NamesTask { get; }

	public ChannelJoinEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel) : this(line, matchesAsyncRequests, sender, channel, null) { }
	public ChannelJoinEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, Task? namesTask) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.NamesTask = namesTask;
	}
}

public class ChannelJoinDeniedEventArgs : IrcLineEventArgs {
	/// <summary>Returns the name of the channel in the message.</summary>
	public string Channel { get; }
	/// <summary>Returns a <see cref="ChannelJoinDeniedReason"/> value representing the reason a join failed.</summary>
	public ChannelJoinDeniedReason Reason { get; }
	/// <summary>Returns the status message received from the server.</summary>
	public string Message { get; }

	public ChannelJoinDeniedEventArgs(IrcLine line, bool matchesAsyncRequests, string channel, ChannelJoinDeniedReason reason, string message) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Reason = reason;
		this.Message = message;
	}
}

public class ChannelKeyEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that is affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the new channel key, or null if a key was removed.</summary>
	public string? Key { get; }

	public ChannelKeyEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, string? key) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.Key = key;
	}
}

public class ChannelKickEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that is affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns an <see cref="IrcChannelUser"/> object representing the user who was kicked out.</summary>
	public IrcChannelUser Target { get; }
	/// <summary>Returns the reason provided by the kicker.</summary>
	public string Reason { get; }

	public ChannelKickEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, IrcChannelUser target, string reason) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.Target = target;
		this.Reason = reason;
	}
}

public class ChannelLimitEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that is affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the new limit.</summary>
	public int Limit { get; }

	public ChannelLimitEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, int limit) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.Limit = limit;
	}
}

public class ChannelListChangedEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that is affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the mode change.</summary>
	public ModeChange ModeChange { get; }
	/// <summary>Returns an <see cref="IEnumerable{T}"/> of <see cref="IrcChannelUser"/> that enumerates users on the channel who match the parameter.</summary>
	/// <remarks>This property uses deferred execution. This means that the user list is not actually searched until the enumerable is enumerated.</remarks>
	public IEnumerable<IrcChannelUser> MatchedUsers { get; }

	public ChannelListChangedEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, ModeChange modeChange) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.ModeChange = modeChange;
		this.MatchedUsers = modeChange.Parameter is not null ? this.Channel.Users.Matching(modeChange.Parameter) : Enumerable.Empty<IrcChannelUser>();
	}
}

public class ChannelListEventArgs : IrcLineEventArgs {
	/// <summary>Returns the name of a channel. Some servers may mask the name for private channels.</summary>
	public string Channel { get; }
	/// <summary>Returns the number of users on the channel, as received from the server.</summary>
	public int Users { get; }
	/// <summary>Returns the topic of the channel, as received from the server.</summary>
	public string Topic { get; }

	public ChannelListEventArgs(IrcLine line, bool matchesAsyncRequests, string channel, int users, string topic) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Users = users;
		this.Topic = topic;
	}
}

public class ChannelListEndEventArgs : IrcLineEventArgs {
	/// <summary>Returns the status message received from the server.</summary>
	public string Message { get; }

	public ChannelListEndEventArgs(IrcLine line, bool matchesAsyncRequests, string message) : base(line, matchesAsyncRequests) => this.Message = message;
}

public class ChannelMessageEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who sent the message.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel receiving the message.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the message text.</summary>
	public string Message { get; }
	/// <summary>Returns the status prefix used in the message, or <see cref="ChannelStatus.Empty"/> if none was used.</summary>
	public ChannelStatus Status { get; }

	public ChannelMessageEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, string message, ChannelStatus status) : base(line, matchesAsyncRequests) {
		this.Sender = sender ?? throw new ArgumentNullException(nameof(sender));
		this.Channel = channel ?? throw new ArgumentNullException(nameof(channel));
		this.Message = message ?? throw new ArgumentNullException(nameof(message));
		this.Status = status ?? throw new ArgumentNullException(nameof(status));
	}
}

public class ChannelTagMessageEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who sent the message.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel receiving the message.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the status prefix used in the message, or <see cref="ChannelStatus.Empty"/> if none was used.</summary>
	public ChannelStatus Status { get; }

	public ChannelTagMessageEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, ChannelStatus status) : base(line, matchesAsyncRequests) {
		this.Sender = sender ?? throw new ArgumentNullException(nameof(sender));
		this.Channel = channel ?? throw new ArgumentNullException(nameof(channel));
		this.Status = status ?? throw new ArgumentNullException(nameof(status));
	}
}

public class ChannelModeChangedEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that is affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns true if a mode was set, or false if one was removed.</summary>
	public bool Direction { get; }
	/// <summary>Returns the mode character of the mode that was changed.</summary>
	public char Mode { get; }
	/// <summary>Returns the parameter to the mode change, or null if there was no parameter.</summary>
	public string? Parameter { get; }

	public ChannelModeChangedEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, bool direction, char mode, string? parameter) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.Direction = direction;
		this.Mode = mode;
		this.Parameter = parameter;
	}
}

public class ChannelModeListEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that this message refers to.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the list entry in the message.</summary>
	public string Mask { get; }
	/// <summary>Returns the nickname or hostmask of the entity who added the entry. This may be reset during netsplits.</summary>
	public string AddedBy { get; }
	/// <summary>Returns the time when the entry was added. This may be reset during netsplits.</summary>
	public DateTime AddedOn { get; }

	public ChannelModeListEventArgs(IrcLine line, bool matchesAsyncRequests, IrcChannel channel, string mask, string addedBy, DateTime addedOn) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Mask = mask;
		this.AddedBy = addedBy;
		this.AddedOn = addedOn;
	}
}

public class ChannelModeListEndEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that this message refers to.</summary>
	public IrcChannel Channel { get; }

	public ChannelModeListEndEventArgs(IrcLine line, bool matchesAsyncRequests, IrcChannel channel) : base(line, matchesAsyncRequests) => this.Channel = channel;
}

public class ChannelModesGetEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that this message refers to.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns a <see cref="ModeSet"/> object representing the modes on the channel.</summary>
	public ModeSet Modes { get; }

	public ChannelModesGetEventArgs(IrcLine line, bool matchesAsyncRequests, IrcChannel channel, ModeSet modes) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Modes = modes;
	}
}

public class ChannelModesSetEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that was affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns a list of <see cref="ModeChange"/> values representing the changes that were made.</summary>
	public ReadOnlyCollection<ModeChange> Modes { get; }

	public ChannelModesSetEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, IList<ModeChange> modes) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.Modes = new ReadOnlyCollection<ModeChange>(modes);
	}
}

public class ChannelNamesEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that this message refers to.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the raw list fragment.</summary>
	public string Names { get; }

	public ChannelNamesEventArgs(IrcLine line, bool matchesAsyncRequests, IrcChannel channel, string names) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Names = names;
	}
}

public class ChannelPartEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who is leaving.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that was affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the part message, or null if there was no part message.</summary>
	public string? Message { get; }

	public ChannelPartEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, string? message) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.Message = message;
	}
}

public class ChannelStatusChangedEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that was affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns a <see cref="IrcChannelUser"/> object representing the user whose status changed.</summary>
	public IrcChannelUser Target { get; }
	/// <summary>Returns the mode change.</summary>
	public ModeChange ModeChange { get; }

	public ChannelStatusChangedEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, IrcChannelUser target, ModeChange modeChange) : base(line, matchesAsyncRequests) {
		this.Sender = sender ?? throw new ArgumentNullException(nameof(sender));
		this.Channel = channel ?? throw new ArgumentNullException(nameof(channel));
		this.Target = target ?? throw new ArgumentNullException(nameof(target));
		this.ModeChange = modeChange;
	}
}

public class ChannelTimestampEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that this message refers to.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the time when the channel was created.</summary>
	public DateTime Timestamp { get; }

	public ChannelTimestampEventArgs(IrcLine line, bool matchesAsyncRequests, IrcChannel channel, DateTime timestamp) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Timestamp = timestamp;
	}
}

public class ChannelTopicEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that this message refers to.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the channel topic, or null if there is no topic.</summary>
	public string Topic { get; }

	public ChannelTopicEventArgs(IrcLine line, bool matchesAsyncRequests, IrcChannel channel, string topic) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Topic = topic;
	}
}

public class ChannelTopicChangeEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who made the change.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that is affected.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the old channel topic.</summary>
	public string? OldTopic { get; }
	/// <summary>Returns the nickname or hostmask of the entity who set the old channel topic.</summary>
	public string? OldTopicSetter { get; }
	/// <summary>Returns the time when the old topic was set.</summary>
	public DateTime? OldTopicStamp { get; }

	public ChannelTopicChangeEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, string? oldTopic, string? oldTopicSetter, DateTime? oldTopicStamp) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Channel = channel;
		this.OldTopic = oldTopic;
		this.OldTopicSetter = oldTopicSetter;
		this.OldTopicStamp = oldTopicStamp;
	}
}

public class ChannelTopicStampEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcChannel"/> object representing the channel that this message refers to.</summary>
	public IrcChannel Channel { get; }
	/// <summary>Returns the nickname or hostmask of the entity who set the channel topic. This may be reset during a netsplit.</summary>
	public string Setter { get; }
	/// <summary>Returns the time when the topic was set.</summary>
	public DateTime Timestamp { get; }

	public ChannelTopicStampEventArgs(IrcLine line, bool matchesAsyncRequests, IrcChannel channel, string setter, DateTime timestamp) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Setter = setter;
		this.Timestamp = timestamp;
	}
}

public class DisconnectEventArgs : EventArgs {
	/// <summary>Returns a <see cref="DisconnectReason"/> value specifying the cause of the disconnection.</summary>
	public DisconnectReason Reason { get; }
	/// <summary>If the disconnection caused an exception to be thrown, returns the exception.</summary>
	public Exception? Exception { get; }

	public DisconnectEventArgs(DisconnectReason reason, Exception? exception) {
		this.Reason = reason;
		this.Exception = exception;
	}
}

public class ExceptionEventArgs : EventArgs {
	/// <summary>Returns the exception that occurred.</summary>
	public Exception Exception { get; }
	/// <summary>Returns a value indicating whether the connection cannot continue.</summary>
	public bool Fatal { get; }

	public ExceptionEventArgs(Exception exception, bool fatal) {
		this.Exception = exception;
		this.Fatal = fatal;
	}
}

public class IrcLineEventArgs : EventArgs {
	/// <summary>Returns the parsed received line as an <see cref="IrcLine"/>.</summary>
	public IrcLine Line { get; }
	/// <summary>Returns a value indicating whether the line matched any async requests.</summary>
	/// <seealso cref="AsyncRequest"/>
	public bool MatchesAsyncRequests { get; }

	public IrcLineEventArgs(IrcLine line, bool matchesAsyncRequests) : base() {
		this.Line = line;
		this.MatchesAsyncRequests = matchesAsyncRequests;
	}
}

public class IrcUserLineEventArgs : IrcLineEventArgs {
	public IrcUser User { get; }

	public IrcUserLineEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser user) : base(line, matchesAsyncRequests) => this.User = user;
}

public class IrcUserEventArgs : EventArgs {
	public IrcUser User { get; }

	public IrcUserEventArgs(IrcUser user) => this.User = user;
}

public class InviteEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who sent the message.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns the nickname that the message is addressed to.</summary>
	public string Target { get; }
	/// <summary>Returns the name of the channel that this message refers to.</summary>
	public string Channel { get; }

	public InviteEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, string target, string channel) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Target = target;
		this.Channel = channel;
	}
}

public class InviteSentEventArgs : IrcLineEventArgs {
	/// <summary>Returns the name of the channel that you are inviting to.</summary>
	public string Channel { get; }
	/// <summary>Returns the nickname that the message is addressed to.</summary>
	public string Target { get; }

	public InviteSentEventArgs(IrcLine line, bool matchesAsyncRequests, string channel, string target) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Target = target;
	}
}

public class MotdEventArgs : IrcLineEventArgs {
	/// <summary>Returns a line of the MotD.</summary>
	public string Message { get; }

	public MotdEventArgs(IrcLine line, bool matchesAsyncRequests, string message) : base(line, matchesAsyncRequests) => this.Message = message;
}

public class NicknameEventArgs : IrcLineEventArgs {
	public string Nickname { get; }
	public string Message { get; }

	public NicknameEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string message) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.Message = message;
	}
}

public class NicknameChangeEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user whose nickname is changing.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns the user's new nickname.</summary>
	public string NewNickname { get; }

	public NicknameChangeEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, string newNickname) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.NewNickname = newNickname;
	}
}

public class PingEventArgs : IrcLineEventArgs {
	public string Server { get; }

	public PingEventArgs(IrcLine line, bool matchesAsyncRequests, string server) : base(line, matchesAsyncRequests) => this.Server = server;
}

public class PrivateMessageEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who sent the message.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns the target that the message is addressed to: usually the local user's nickname, but may be something else, such as global messages.</summary>
	public string Target { get; }
	/// <summary>Returns the message text.</summary>
	public string Message { get; }

	public PrivateMessageEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, string target, string message) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Target = target;
		this.Message = message;
	}
}

public class PrivateTagMessageEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who sent the message.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns the target that the message is addressed to: usually the local user's nickname, but may be something else, such as global messages.</summary>
	public string Target { get; }

	public PrivateTagMessageEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, string target) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Target = target;
	}
}

public class QuitEventArgs : IrcLineEventArgs {
	/// <summary>Returns an <see cref="IrcUser"/> object representing the user who is quitting.</summary>
	public IrcUser Sender { get; }
	/// <summary>Returns the quit message.</summary>
	public string Message { get; }

	public QuitEventArgs(IrcLine line, bool matchesAsyncRequests, IrcUser sender, string message) : base(line, matchesAsyncRequests) {
		this.Sender = sender;
		this.Message = message;
	}
}

public class RawLineEventArgs : EventArgs {
	/// <summary>Returns the line that is being sent.</summary>
	public IrcLine Line { get; }

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
	/// <summary>Returns the raw bytes that are being sent, including the terminating CR and LF..</summary>
	/// <remarks>This property is available only in .NET Standard 2.1+ and .NET 5.0+.</remarks>
	public ReadOnlyMemory<byte> Data { get; }

	public RawLineEventArgs(IrcLine line, ReadOnlyMemory<byte> data) {
		this.Line = line ?? throw new ArgumentNullException(nameof(line));
		this.Data = data;
	}
#else
	public RawLineEventArgs(IrcLine line) => this.Line = line ?? throw new ArgumentNullException(nameof(line));
#endif
}

public class RegisteredEventArgs : IrcLineEventArgs {
	/// <summary>Returns a value indicating whether the connection is going to be continued or terminated.</summary>
	/// <remarks>The connection will be terminated if SASL authentication was required and the server doesn't support it.</remarks>
	public bool Continuing { get; }

	public RegisteredEventArgs(IrcLine line, bool matchesAsyncRequests, bool continuing) : base(line, matchesAsyncRequests) => this.Continuing = continuing;
}

public class SelectCertificateEventArgs : EventArgs {
	/// <summary>Returns the hostname of the server as specified by the client.</summary>
	public string TargetHost { get; }
	/// <summary>Returns a <see cref="X509CertificateCollection"/> containing available local certificates.</summary>
	public X509CertificateCollection LocalCertificates { get; }
	/// <summary>Returns the certificate presented by the server.</summary>
	public X509Certificate? ServerCertificate { get; }
	/// <summary>Returns a list of certificate issuers acceptable to the remote party.</summary>
	public string[] AcceptableIssuers { get; }
	/// <summary>Returns or sets the certificate that will be presented by the client.</summary>
	public X509Certificate? ClientCertificate { get; set; }

	public SelectCertificateEventArgs(string targetHost, X509CertificateCollection localCertificates, X509Certificate? serverCertificate, string[] acceptableIssuers) {
		this.TargetHost = targetHost;
		this.LocalCertificates = localCertificates;
		this.ServerCertificate = serverCertificate;
		this.AcceptableIssuers = acceptableIssuers;
	}
}

public class ServerErrorEventArgs : IrcLineEventArgs {
	/// <summary>Returns the error message text.</summary>
	public string Message { get; }

	public ServerErrorEventArgs(IrcLine line, bool matchesAsyncRequests, string message) : base(line, matchesAsyncRequests) => this.Message = message;
}

public class StateEventArgs : EventArgs {
	/// <summary>Returns the previous state of the <see cref="IrcClient"/>.</summary>
	public IrcClientState OldState { get; }
	/// <summary>Returns the new state of the <see cref="IrcClient"/>.</summary>
	public IrcClientState NewState { get; }

	public StateEventArgs(IrcClientState oldState, IrcClientState newState) {
		this.OldState = oldState;
		this.NewState = newState;
	}
}

public class StsUpgradeEventArgs : EventArgs {
	/// <summary>Returns the port used for the TLS connection.</summary>
	public int Port { get; }

	public StsUpgradeEventArgs(int port) => this.Port = port;
}

public class UserModeEventArgs : IrcLineEventArgs {
	/// <summary>Returns true if a mode was set, or false if one was removed.</summary>
	public bool Direction { get; }
	/// <summary>Returns the mode character of the mode that was changed.</summary>
	public char Mode { get; }

	public UserModeEventArgs(IrcLine line, bool matchesAsyncRequests, bool direction, char mode) : base(line, matchesAsyncRequests) {
		this.Direction = direction;
		this.Mode = mode;
	}
}

public class UserModesEventArgs : IrcLineEventArgs {
	/// <summary>Returns a string representing the local user's current user modes.</summary>
	public string Modes { get; }

	public UserModesEventArgs(IrcLine line, bool matchesAsyncRequests, string modes) : base(line, matchesAsyncRequests) => this.Modes = modes;
}

public class ValidateCertificateEventArgs : EventArgs {
	/// <summary>Returns the certificate presented by the server.</summary>
	public X509Certificate? Certificate { get; }
	/// <summary>Returns the chain of certificate authorities associated with the server's certificate.</summary>
	public X509Chain? Chain { get; }
	/// <summary>Returns a value indicating why the certificate is invalid.</summary>
	public SslPolicyErrors SslPolicyErrors { get; }
	/// <summary>Returns or sets a value specifying whether the connection will continue.</summary>
	public bool Valid { get; set; }

	public ValidateCertificateEventArgs(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors, bool valid) {
		this.Certificate = certificate;
		this.Chain = chain;
		this.SslPolicyErrors = errors;
		this.Valid = valid;
	}
}

public class WhoListEventArgs : IrcLineEventArgs {
	/// <summary>Returns one of the channels the local user shares with this user, or "*".</summary>
	public string Channel { get; }
	/// <summary>Returns the user's ident username.</summary>
	public string Ident { get; }
	/// <summary>Returns the user's hostname.</summary>
	public string Host { get; }
	/// <summary>Returns the name of the server that the user is connected to.</summary>
	public string Server { get; }
	/// <summary>Returns the user's nickname.</summary>
	public string Nickname { get; }
	/// <summary>Returns a list of flags that apply to this user. See RFC 2812 for more details.</summary>
	public char[] Flags { get; }
	/// <summary>Returns the number of 'hops' between this server and the user's server.</summary>
	public int Hops { get; }
	/// <summary>Returns the user's full name.</summary>
	public string FullName { get; }

	public WhoListEventArgs(IrcLine line, bool matchesAsyncRequests, string channel, string username, string host, string server, string nickname, char[] flags, int hops, string fullName) : base(line, matchesAsyncRequests) {
		this.Channel = channel;
		this.Ident = username;
		this.Host = host;
		this.Server = server;
		this.Nickname = nickname;
		this.Flags = flags;
		this.Hops = hops;
		this.FullName = fullName;
	}
}

public class WhoxListEventArgs : IrcLineEventArgs {
	/// <summary>Returns the parameters, excluding the recipient's nickname, in the reply.</summary>
	public IReadOnlyList<string> Parameters { get; }
	/// <summary>Set the values in this array to specify the type of the fields in the reply. This allows AnIRC to process them.</summary>
	public WhoxField[] Fields { get; }

	public WhoxListEventArgs(IrcLine line, bool matchesAsyncRequests, IReadOnlyList<string> parameters) : base(line, matchesAsyncRequests) {
		this.Parameters = parameters;
		this.Fields = new WhoxField[parameters.Count];
	}
}

public class WhoisAuthenticationEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname of the user that this message refers to.</summary>
	public string Nickname { get; }
	/// <summary>Returns the user's services account name, or null if the user is not identified with services.</summary>
	public string Account { get; }
	/// <summary>Returns the status message received from the server.</summary>
	public string Message { get; }

	public WhoisAuthenticationEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string account, string message) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.Account = account;
		this.Message = message;
	}
}

public class WhoisChannelsEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname of the user that this message refers to.</summary>
	public string Nickname { get; }
	/// <summary>Returns the raw list of channels that the user is on.</summary>
	public string Channels { get; }

	public WhoisChannelsEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string channels) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.Channels = channels;
	}
}

public class WhoisEndEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname of the user that this message refers to.</summary>
	public string Nickname { get; }
	/// <summary>Returns the status message received from the server.</summary>
	public string Message { get; }

	public WhoisEndEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string message) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.Message = message;
	}
}

public class WhoisIdleEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname of the user that this message refers to.</summary>
	public string Nickname { get; }
	/// <summary>Returns the user's idle time.</summary>
	public TimeSpan IdleTime { get; }
	/// <summary>Returns the time when the user registered.</summary>
	public DateTime LoginTime { get; }
	/// <summary>Returns the status message received from the server.</summary>
	public string Message { get; }

	public WhoisIdleEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, TimeSpan idleTime, DateTime loginTime, string message) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.IdleTime = idleTime;
		this.LoginTime = loginTime;
		this.Message = message;
	}
}

public class WhoisNameEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname of the user that this message refers to.</summary>
	public string Nickname { get; }
	/// <summary>Returns the user's ident username.</summary>
	public string Username { get; }
	/// <summary>Returns the user's hostname.</summary>
	public string Host { get; }
	/// <summary>Returns the user's full name.</summary>
	public string FullName { get; }

	public WhoisNameEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string username, string host, string fullName) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.Username = username;
		this.Host = host;
		this.FullName = fullName;
	}
}

public class WhoisOperEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname of the user that this message refers to.</summary>
	public string Nickname { get; }
	/// <summary>Returns the status message received from the server.</summary>
	public string Message { get; }

	public WhoisOperEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string message) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.Message = message;
	}
}

public class WhoisRealHostEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname of the user that this message refers to.</summary>
	public string Nickname { get; }
	/// <summary>Returns the user's real hostname.</summary>
	public string RealHost { get; }
	/// <summary>Returns the user's real IP address.</summary>
	public IPAddress RealIP { get; }
	/// <summary>Returns the status message received from the server.</summary>
	public string Message { get; }

	public WhoisRealHostEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string realHost, IPAddress realIP, string message) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.RealHost = realHost;
		this.RealIP = realIP;
		this.Message = message;
	}
}

public class WhoisServerEventArgs : IrcLineEventArgs {
	/// <summary>Returns the nickname of the user that this message refers to.</summary>
	public string Nickname { get; }
	/// <summary>Returns the name of the server that this user is connected to.</summary>
	public string Server { get; }
	/// <summary>Returns the information line of the server that this user is connected to.</summary>
	public string Info { get; }

	public WhoisServerEventArgs(IrcLine line, bool matchesAsyncRequests, string nickname, string server, string info) : base(line, matchesAsyncRequests) {
		this.Nickname = nickname;
		this.Server = server;
		this.Info = info;
	}
}
