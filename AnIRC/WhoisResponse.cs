using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AnIRC {
	/// <summary>
	/// Represents the response to a WHOIS command.
	/// </summary>
	public class WhoisResponse {
		/// <summary>The user's nickname.</summary>
		public string Nickname { get; internal set; }
		/// <summary>The user's ident name.</summary>
		public string Ident { get; internal set; }
		/// <summary>The user's displayed host.</summary>
		public string Host { get; internal set; }
		/// <summary>The user's full name.</summary>
		public string FullName { get; internal set; }
		/// <summary>The name of the server the user is on.</summary>
		public string ServerName { get; internal set; }
		/// <summary>The description or tag line of the server the user is on.</summary>
		public string ServerInfo { get; internal set; }
		/// <summary>Indicates whether the user is an oper.</summary>
		public bool Oper { get; internal set; }
		/// <summary>The time the user has been idle, or null if the server didn't say.</summary>
		public TimeSpan? IdleTime { get; internal set; }
		/// <summary>The time the user logged in, or null if the server didn't say.</summary>
		public DateTime? SignonTime { get; internal set; }
		/// <summary>A dictionary listing the publicly visible channels the user is on, along with the user's status on them.</summary>
		public ReadOnlyDictionary<string, ChannelStatus> Channels { get; internal set; }
		/// <summary>The name of the server from which the response originated.</summary>
		public string ProvidingServerName { get; internal set; }
		/// <summary>Indicates whether the user is away.</summary>
		public bool Away => this.AwayMessage != null;
		/// <summary>If the user is away, returns their away message; otherwise returns null.</summary>
		public string? AwayMessage { get; internal set; }
		/// <summary>The user's account name, or null if they are not identified to an account.</summary>
		public string? Account { get; internal set; }
		/// <summary>The list of raw IRC lines that made up the response.</summary>
		public ReadOnlyCollection<IrcLine> Lines { get; internal set; }

		internal List<IrcLine> lines;
		internal Dictionary<string, ChannelStatus> channels;

		[Obsolete("Deprecated. Use the other constructor instead.")]
		internal WhoisResponse(IrcClient client) {
			this.lines = new List<IrcLine>();
			this.Lines = this.lines.AsReadOnly();
			this.channels = new Dictionary<string, ChannelStatus>(client.CaseMappingComparer);
			this.Channels = new ReadOnlyDictionary<string, ChannelStatus>(this.channels);
		}

		internal WhoisResponse(string nickname, string ident, string host, string fullName, string serverName, string serverInfo, bool oper, TimeSpan? idleTime, DateTime? signonTime, string providingServerName, string? awayMessage, string? account, List<IrcLine> lines, Dictionary<string, ChannelStatus> channels) {
			this.Nickname = nickname ?? throw new ArgumentNullException(nameof(nickname));
			this.Ident = ident ?? throw new ArgumentNullException(nameof(ident));
			this.Host = host ?? throw new ArgumentNullException(nameof(host));
			this.FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
			this.ServerName = serverName ?? throw new ArgumentNullException(nameof(serverName));
			this.ServerInfo = serverInfo ?? throw new ArgumentNullException(nameof(serverInfo));
			this.Oper = oper;
			this.IdleTime = idleTime;
			this.SignonTime = signonTime;
			this.ProvidingServerName = providingServerName ?? throw new ArgumentNullException(nameof(providingServerName));
			this.AwayMessage = awayMessage;
			this.Account = account;
			this.lines = lines ?? throw new ArgumentNullException(nameof(lines));
			this.channels = channels ?? throw new ArgumentNullException(nameof(channels));
			this.Lines = lines.AsReadOnly();
			this.Channels = new ReadOnlyDictionary<string, ChannelStatus>(channels);
		}
	}

	/// <summary>
	/// Represents the response to a WHO command.
	/// </summary>
	public class WhoResponse {
		/// <summary>Returns one of the channels the local user shares with this user, or "*".</summary>
		public string? Channel { get; internal set; }
		/// <summary>Returns the user's ident username.</summary>
		public string Ident { get; internal set; }
		/// <summary>Returns the user's hostname.</summary>
		public string Host { get; internal set; }
		/// <summary>Returns the user's IP address. This will always be null except for WHOX replies.</summary>
		[Obsolete("Will be moved to a different class.")]
		public string? IPAddress { get; internal set; }
		/// <summary>Returns the name of the server that the user is connected to.</summary>
		public string? Server { get; internal set; }
		/// <summary>Returns the user's nickname.</summary>
		public string Nickname { get; internal set; }
		/// <summary>Returns a value indicating whether the user is marked as away.</summary>
		public bool Away { get; internal set; }
		/// <summary>Returns a value indicating whether the user is a server operator.</summary>
		public bool Oper { get; internal set; }
		/// <summary>Returns a <see cref="ChannelStatus"/> object representing the status the user has on the channel specified by <see cref="Channel"/>.</summary>
		public ChannelStatus? ChannelStatus { get; internal set; }
		/// <summary>Returns the number of 'hops' between this server and the user's server.</summary>
		public int HopCount { get; internal set; }
		/// <summary>Returns the user's idle time in seconds. This will always be null except for WHOX replies.</summary>
		[Obsolete("Will be moved to a different class.")]
		public int? IdleTime { get; internal set; }
		/// <summary>Returns the user's services account name. This will always be null except for WHOX replies.</summary>
		[Obsolete("Will be moved to a different class.")]
		public string? Account { get; internal set; }
		/// <summary>Returns the user's full name.</summary>
		public string FullName { get; internal set; }

		internal WhoResponse(string? channel, string ident, string host, string server, string nickname, bool away, bool oper, ChannelStatus? channelStatus, int hopCount, string fullName) {
			this.Channel = channel;
			this.Ident = ident ?? throw new ArgumentNullException(nameof(ident));
			this.Host = host ?? throw new ArgumentNullException(nameof(host));
			this.Server = server ?? throw new ArgumentNullException(nameof(server));
			this.Nickname = nickname ?? throw new ArgumentNullException(nameof(nickname));
			this.Away = away;
			this.Oper = oper;
			this.ChannelStatus = channelStatus;
			this.HopCount = hopCount;
			this.FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
		}
	}
}
