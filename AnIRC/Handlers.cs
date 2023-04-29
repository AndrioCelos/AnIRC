using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static AnIRC.Replies;

namespace AnIRC;

/// <summary>Indicates that a method is an IRC message handler.</summary>
/// <seealso cref="IrcClient.RegisterHandlers(Type)"/>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class IrcMessageHandlerAttribute : Attribute {
	/// <summary>The reply or numeric that this procedure should handle.</summary>
	public string Reply { get; }

	/// <summary>Initializes a new <see cref="IrcMessageHandlerAttribute"/> for the specified reply.</summary>
	/// <param name="reply">The reply or numeric that should be handled.</param>
	public IrcMessageHandlerAttribute(string reply) => this.Reply = reply;
}

#pragma warning disable IDE0079  // Remove unnecessary suppression
#pragma warning disable IDE0060  // Remove unused parameter
internal static class Handlers {
	[IrcMessageHandler(RPL_WELCOME)]  // 001
	public static void HandleWelcome(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		client.ServerName = line.Source;
		if (client.Me.Nickname != line.Parameters[0]) {
			client.OnNicknameChange(new(line, matchesAsyncRequests, client.Me, line.Parameters[0]));
			if (client.State != IrcClientState.Disconnected) {
				client.Users.Remove(client.Me);
				((IrcUser) client.Me).Nickname = line.Parameters[0];
				client.Users.Add(client.Me);
			}
		}

		bool continuing = !(client.SaslAuthenticationMode == SaslAuthenticationMode.Required && client.Me.Account == null);
		client.State = IrcClientState.ReceivingServerInfo;
		client.OnRegistered(new(line, matchesAsyncRequests, continuing));

		if (!continuing && client.disconnectReason == 0) {
			client.disconnectReason = DisconnectReason.SaslAuthenticationFailed;
			client.Send(QUIT, IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_NOT_SUPPORTED);
		}
	}

	[IrcMessageHandler(RPL_MYINFO)]  // 004
	public static void HandleMyInfo(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (line.Parameters.Count > 1) client.ServerName = line.Parameters[1];  // Some servers (including Twitch) don't send this.

		// Get supported modes.
		if (line.Parameters.Count > 3) {
			client.SupportedUserModes.Clear();
			foreach (char c in line.Parameters[3])
				client.SupportedUserModes.Add(c);
		}

		// We can only assume that channel modes not defined in RFC 2811 are type D at this point.
		if (line.Parameters.Count > 4) {
			List<char> modesA = new(), modesB = new(), modesC = new(), modesD = new(), modesS = new();
			foreach (char c in line.Parameters[4]) {
				switch (ChannelModes.RFC2811.ModeType(c)) {
					case 'A': modesA.Add(c); break;
					case 'B': modesB.Add(c); break;
					case 'C': modesC.Add(c); break;
					case 'D': modesD.Add(c); break;
					case 'S': modesS.Add(c); break;
					default: modesD.Add(c); break;
				}
			}
			client.Extensions.ChanModes = new ChannelModes(modesA, modesB, modesC, modesD, modesS);
		}
	}

	[IrcMessageHandler(RPL_ISUPPORT)]  // 005
	public static void HandleISupport(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (!(line.Parameters.Count != 0 && line.Parameters[0].StartsWith("Try server"))) {
			// RPL_ISUPPORT
			foreach (var parameter in line.Parameters.Skip(1)) {
				if (parameter.Contains(' ')) break;

				string[] fields; string key; string value;
				fields = parameter.Split(new char[] { '=' }, 2);
				if (fields.Length == 2) {
					key = fields[0];
					value = fields[1];
				} else {
					key = fields[0];
					value = "";
				}

				if (key.StartsWith("-"))
					client.Extensions.Remove(key[1..]);
				else
					client.Extensions[key] = IrcExtensions.UnescapeValue(value);
			}
		}
	}

	[IrcMessageHandler(RPL_UMODEIS)]  // 221
	public static void HandleUserMode(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		// TODO: Add support for non-standard parameterised user modes (e.g. +s on InspIRCd).
		client.UserModes.Clear();

		bool direction = true;
		foreach (char c in line.Parameters[1]) {
			if (c == '+') direction = true;
			else if (c == '-') direction = false;
			else {
				if (direction) client.UserModes.Add(c);
				else client.UserModes.Remove(c);
			}
		}
		client.OnUserModesGet(new(line, matchesAsyncRequests, line.Parameters[1]));
	}

	[IrcMessageHandler(RPL_AWAY)]
	public static void HandleAway(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Parameters[1], false);
		if (!user.Away) {
			user.AwayReason = line.Parameters[2];
			user.AwaySince = client.testUtcNow ?? DateTime.UtcNow;
		}
		client.OnAwayMessage(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));
	}

	/*
	[IRCMessageHandler(Replies.RPL_ISON)]  // 303
	public static void HandleIson(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		// TODO: This can be trapped as part of a notify feature.
	}
	*/

	[IrcMessageHandler(RPL_UNAWAY)]  // 305
	public static void HandleUnAway(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		client.Me.AwayReason = null;
		client.Me.AwaySince = null;
		client.OnAwayCancelled(EventArgs.Empty);
	}

	[IrcMessageHandler(RPL_NOWAWAY)]  // 306
	public static void HandleNowAway(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (!client.Me.Away) client.Me.AwaySince = client.testUtcNow ?? DateTime.UtcNow;
		client.Me.AwayReason = IrcClient.UNKNOWN_AWAY_MESSAGE;
		client.OnAwaySet(EventArgs.Empty);
	}

	[IrcMessageHandler(RPL_WHOISREGNICK)]  // 307
	public static void HandleWhoisRegNick(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		// This reply only says that the user is registered to services, but not the account name.
		// RPL_WHOISACCOUNT (330) gives the account name, but some servers send both.
		// If only RPL_WHOISREGNICK is sent, the account name is the user's nickname.
		if (client.accountKnown) return;

		if (client.Users.TryGetValue(line.Parameters[1], out var user))
			user.Account = line.Parameters[1];
	}

	[IrcMessageHandler(RPL_WHOISHELPOP)]
	public static void HandleWhoisHelper(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnWhoIsHelperLine(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));

	[IrcMessageHandler(RPL_WHOISUSER)]  // 311
	public static void HandleWhoisName(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Users.TryGetValue(line.Parameters[1], out var user)) {
			user.Ident = line.Parameters[2];
			user.Host = line.Parameters[3];
			user.FullName = line.Parameters[5];

			// Parse gender codes.
			var match = Regex.Match(user.FullName, @"^\x03(\d\d?)\x0F");
			if (match.Success) user.Gender = (Gender) (int.Parse(match.Groups[1].Value) & 3);
		}
		client.OnWhoIsNameLine(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2], line.Parameters[3], line.Parameters[5]));
	}

	[IrcMessageHandler(RPL_WHOISSERVER)]
	public static void HandleWhoisServer(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnWhoIsServerLine(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2], line.Parameters[3]));

	[IrcMessageHandler(RPL_WHOISOPERATOR)]  // 313
	public static void HandleWhoisOper(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Users.TryGetValue(line.Parameters[1], out var user))
			user.IsOper = true;

		client.OnWhoIsOperLine(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));
	}

	[IrcMessageHandler(RPL_WHOWASUSER)]
	public static void HandleWhowasName(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnWhoWasNameLine(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2], line.Parameters[3], line.Parameters[5]));

	/*
	[IRCMessageHandler(Replies.RPL_ENDOFWHO)]  // 315
	public static void HandleWhoEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		// TODO: respond to 315 similarly to 366.
	}
	*/

	[IrcMessageHandler(RPL_WHOISIDLE)]
	public static void HandleWhoisIdle(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnWhoIsIdleLine(new(line, matchesAsyncRequests, line.Parameters[1], TimeSpan.FromSeconds(double.Parse(line.Parameters[2])), IrcClient.DecodeUnixTime(double.Parse(line.Parameters[3])), line.Parameters[4]));

	[IrcMessageHandler(RPL_ENDOFWHOIS)]  // 318
	public static void HandleWhoisEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		client.accountKnown = false;
		client.OnWhoIsEnd(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));
	}

	[IrcMessageHandler(RPL_WHOISCHANNELS)]
	public static void HandleWhoisChannels(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (line.Parameters[2] != "") {
			foreach (var token in line.Parameters[2].Split(' ')) {
				if (client.IsChannelWithStatusPrefix(token, out var channelName, out var status)
					&& client.Channels.TryGetValue(channelName, out var channel)
					&& channel.Users.TryGetValue(line.Parameters[1], out var user)) {
					user.Status = status;
				}
			}
		}
		client.OnWhoIsChannelLine(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));
	}

	[IrcMessageHandler(RPL_LIST)]
	public static void HandleList(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnChannelList(new(line, matchesAsyncRequests, line.Parameters[1], int.Parse(line.Parameters[2]), line.Parameters[3]));

	[IrcMessageHandler(RPL_LISTEND)]
	public static void HandleListEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnChannelListEnd(new(line, matchesAsyncRequests, line.Parameters[1]));

	[IrcMessageHandler(RPL_CHANNELMODEIS)]  // 324
	public static void HandleChannelModes(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName ?? client.Address ?? "*", false);
		var channel = client.Channels.Get(line.Parameters[1]);
		channel.Modes.Clear();
		client.HandleChannelModes(line, matchesAsyncRequests, user, channel, line.Parameters[2], line.Parameters.Skip(3), false);
	}

	[IrcMessageHandler(RPL_CREATIONTIME)]  // 329
	public static void HandleChannelCreationTime(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var time = IrcClient.DecodeUnixTime(double.Parse(line.Parameters[2]));
		if (client.Channels.Contains(line.Parameters[1])) client.Channels[line.Parameters[1]].Timestamp = time;
		client.OnChannelTimestamp(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1]), time));
	}

	[IrcMessageHandler(RPL_WHOISACCOUNT)]  // 330
	public static void HandleWhoisAccount(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Users.TryGetValue(line.Parameters[1], out var user))
			user.Account = line.Parameters[2];
		client.accountKnown = true;
	}

	[IrcMessageHandler(RPL_NOTOPIC)]  // 331
	public static void HandleNoTopic(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Channels.TryGetValue(line.Parameters[1], out var channel)) {
			channel.Topic = null;
			channel.TopicSetter = null;
			channel.TopicStamp = null;
		}
	}

	[IrcMessageHandler(RPL_TOPIC)]  // 332
	public static void HandleChannelTopic(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Channels.Contains(line.Parameters[1])) client.Channels[line.Parameters[1]].Topic = line.Parameters[2];
		client.OnChannelTopic(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1]), line.Parameters[2]));
	}

	[IrcMessageHandler(RPL_TOPICWHOTIME)]  // 333
	public static void HandleTopicStamp(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var time = IrcClient.DecodeUnixTime(double.Parse(line.Parameters[3]));
		if (client.Channels.TryGetValue(line.Parameters[1], out var channel)) {
			channel.TopicSetter = line.Parameters[2];
			channel.TopicStamp = time;
		}
		client.OnChannelTopicStamp(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1]), line.Parameters[2], time));
	}

	[IrcMessageHandler(RPL_INVITING)]
	public static void HandleInviting(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnInviteSent(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));

	[IrcMessageHandler(RPL_INVITELIST)]  // 346
	public static void HandleInviteList(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var time = IrcClient.DecodeUnixTime(double.Parse(line.Parameters[4]));
		client.OnInviteExemptList(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1]), line.Parameters[2], line.Parameters[3], time));
	}

	[IrcMessageHandler(RPL_ENDOFINVITELIST)]
	public static void HandleInviteListEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnInviteExemptListEnd(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1])));

	[IrcMessageHandler(RPL_EXCEPTLIST)]  // 348
	public static void HandleExceptionList(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var time = IrcClient.DecodeUnixTime(double.Parse(line.Parameters[4]));
		client.OnExemptList(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1]), line.Parameters[2], line.Parameters[3], time));
	}

	[IrcMessageHandler(RPL_ENDOFEXCEPTLIST)]
	public static void HandleExceptionListEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnExemptListEnd(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1])));

	[IrcMessageHandler(RPL_WHOREPLY)]  // 352
	public static void HandleWhoReply(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		// TODO: populate the user list?
		string[] fields = line.Parameters[7].Split(new char[] { ' ' }, 2);

		var channelName = line.Parameters[1];
		var ident = line.Parameters[2];
		var host = line.Parameters[3];
		var server = line.Parameters[4];
		var nickname = line.Parameters[5];
		var flags = line.Parameters[6];
		var hops = int.Parse(fields[0]);
		var fullName = fields[1];
		IrcChannel? channel = null; IrcChannelUser? channelUser = null;

		if (client.IsChannel(channelName) && client.Channels.TryGetValue(channelName, out channel)) {
			// We are in a common channel with this person.
			if (!channel.Users.TryGetValue(nickname, out channelUser)) {
				channelUser = new IrcChannelUser(client, channel, nickname);
				channel.Users.Add(channelUser);
			}
		}
		if (!client.Users.TryGetValue(nickname, out var user)) {
			if (channel is not null) {
				user = new IrcUser(client, nickname, ident, host, null, fullName);
				user.Channels.Add(channel);
				client.Users.Add(user);
			}
		} else {
			if (channel is not null && !user.Channels.Contains(channelName))
				user.Channels.Add(channel);
		}

		if (user is not null) {
			user.Ident = ident;
			user.Host = host;
			user.FullName = fullName;

			var match = Regex.Match(user.FullName, @"^\x03(\d\d?)\x0F");
			if (match.Success) user.Gender = (Gender) (int.Parse(match.Groups[1].Value) & 3);

			var missingStatusModes = channelUser is not null ? new HashSet<char>(channelUser.Status) : null;
			var oper = false;
			foreach (char flag in flags) {
				if (flag == 'H') {
					user.AwayReason = null;
				} else if (flag == 'G') {
					if (!user.Away) {
						user.AwayReason = IrcClient.UNKNOWN_AWAY_MESSAGE;
						user.AwaySince = client.testUtcNow ?? DateTime.UtcNow;
					}
				} else if (flag == '*')
					oper = true;
				else if (channelUser is not null && client.Extensions.StatusPrefix.TryGetValue(flag, out char mode)) {
					channelUser.Status.Add(mode);
					missingStatusModes!.Remove(mode);
				}
			}
			if (channelUser is not null) {
				foreach (var mode in missingStatusModes!) channelUser.Status.Remove(mode);
			}
			if (user.IsOper != oper) user.IsOper = oper;
		}
		client.OnWhoList(new(line, matchesAsyncRequests, channelName, ident, host, server, nickname, flags.ToCharArray(), hops, fullName));
	}

	[IrcMessageHandler(RPL_NAMREPLY)]  // 353
	public static void HandleNamesReply(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (line.Parameters[2] != "*") {
			var knownChannel = client.Channels.TryGetValue(line.Parameters[2], out var channel);

			if (channel != null) {
				if (!client.pendingNames.TryGetValue(line.Parameters[2], out var pendingNames)) {
					// Make a set of the remembered users, so we can check for any not listed.
					pendingNames = new HashSet<string>(channel.Users.Select(user => user.Nickname));
					client.pendingNames[line.Parameters[2]] = pendingNames;
				}

				foreach (var name in line.Parameters[3].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
					// Some servers include a space after the last name.
					for (int i = 0; i < name.Length; ++i) {
						char c = name[i];
						// Some IRC servers use = to prefix +s channels to opers.
						// TODO: Find a better way to distinguish prefixes. Some networks allow wacky characters like '$' in nicknames.
						if (c != '=' && !client.Extensions.StatusPrefix.ContainsKey(c)) {
							var user = client.Users.Get(name[i..], true);
							// client.Users.Get will update the user with the hostmask from userhost-in-names, if present.
							if (knownChannel) {
								if (!user.Channels.Contains(channel)) user.Channels.Add(channel);

								if (channel.Users.TryGetValue(user.Nickname, out var channelUser)) {
									channelUser.Status = ChannelStatus.FromPrefix(client, name.Take(i));
									pendingNames.Remove(user.Nickname);
								} else
									channel.Users.Add(new IrcChannelUser(client, channel, user.Nickname, ChannelStatus.FromPrefix(client, name.Take(i))));
							}

							break;
						}
					}
				}
			}
		}

		client.OnNames(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[2]), line.Parameters[3]));
	}

	[IrcMessageHandler(RPL_WHOSPCRPL)]  // 354
	public static void HandleWhoxReply(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var parameters = line.Parameters.Skip(1).ToArray();
		var e = new WhoxListEventArgs(line, matchesAsyncRequests, parameters);

		// If an async request has been added for this reply, we can find out the field types from that.
		if (matchesAsyncRequests) {
			lock (client.asyncRequests) {
				for (int i = 0; i < client.asyncRequests.Count; ++i) {
					if (client.asyncRequests[i] is AsyncRequest.WhoxAsyncRequest asyncRequest) {
						if ((asyncRequest.QueryType == null || line.Parameters[1] == asyncRequest.QueryType) && asyncRequest.Fields.Count == line.Parameters.Count - 1) {
							asyncRequest.Fields.CopyTo(e.Fields, 0);
							break;
						}
					}
				}
			}
		}

		client.OnWhoxList(e);

		int nicknameIndex = -1, channelIndex = -1;
		for (int i = parameters.Length - 1; i >= 0; --i) {
			if (e.Fields[i] == WhoxField.Nickname) {
				nicknameIndex = i;
			} else if (e.Fields[i] == WhoxField.Channel) {
				channelIndex = i;
			}
		}
		if (nicknameIndex < 0) return;  // We can't do anything else with the reply if we don't know the nickname.

		var user = client.Users.Get(parameters[nicknameIndex], false);

		IrcChannelUser? channelUser;
		if (channelIndex >= 0 && client.Channels.TryGetValue(parameters[channelIndex], out var channel))
			channel.Users.TryGetValue(parameters[nicknameIndex], out channelUser);
		else
			channelUser = null;

		for (int i = parameters.Length - 1; i >= 0; --i) {
			switch (e.Fields[i]) {
				case WhoxField.Ident:
					user.Ident = parameters[i];
					break;
				case WhoxField.Host:
					user.Host = parameters[i];
					break;
				case WhoxField.Flags:
					var oper = false;
					foreach (char flag in parameters[i]) {
						if (flag == 'H') {
							user.AwayReason = null;
						} else if (flag == 'G') {
							if (!user.Away) {
								user.AwayReason = IrcClient.UNKNOWN_AWAY_MESSAGE;
								user.AwaySince = client.testUtcNow ?? DateTime.UtcNow;
							}
						} else if (flag == '*')
							oper = true;
						else if (channelUser != null && client.Extensions.StatusPrefix.TryGetValue(flag, out char mode))
							channelUser.Status.Add(mode);
					}
					if (user.IsOper != oper) user.IsOper = oper;
					break;
				case WhoxField.Account:
					user.Account = parameters[i] == "0" ? null : parameters[i];
					break;
				case WhoxField.FullName:
					user.FullName = parameters[i];
					break;
			}
		}
	}

	[IrcMessageHandler(RPL_ENDOFNAMES)]  // 366
	public static void HandleNamesEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		IEnumerable<IrcUser>? disappearedUsers = null;
		if (line.Parameters[1] != "*") {
			if (client.pendingNames.TryGetValue(line.Parameters[1], out var pendingNames) && client.Channels.TryGetValue(line.Parameters[1], out var channel)) {
				// Remove any users who weren't in the NAMES list.
				foreach (string name in pendingNames) {
					if (client.Users.TryGetValue(name, out var user)) {
						disappearedUsers = client.RemoveUserFromChannel(channel, user);
					}
				}
				client.pendingNames.Remove(line.Parameters[1]);
			}
		}

		client.OnNamesEnd(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1])));
		if (disappearedUsers is not null) {
			foreach (var user in disappearedUsers)
				client.OnUserDisappeared(new(user));
		}
	}

	[IrcMessageHandler(RPL_BANLIST)]  // 367
	public static void HandleBanList(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var time = IrcClient.DecodeUnixTime(double.Parse(line.Parameters[4]));
		client.OnBanList(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1]), line.Parameters[2], line.Parameters[3], time));
	}

	[IrcMessageHandler(RPL_ENDOFBANLIST)]
	public static void HandleBanListEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnBanListEnd(new(line, matchesAsyncRequests, client.Channels.Get(line.Parameters[1])));

	[IrcMessageHandler(RPL_ENDOFWHOWAS)]
	public static void HandleWhowasEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnWhoWasEnd(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));

	[IrcMessageHandler(RPL_ENDOFMOTD)]
	public static void HandleEndOfMotd(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.State = IrcClientState.Online;

	[IrcMessageHandler(ERR_CANNOTSENDTOCHAN)]
	public static void HandleCannotSendToChan(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnChannelMessageDenied(new(line, matchesAsyncRequests, line.Parameters[1], 0, line.Parameters[2]));

	[IrcMessageHandler(ERR_NOMOTD)]
	public static void HandleNoMotd(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.State = IrcClientState.Online;

	[IrcMessageHandler(ERR_ERRONEUSNICKNAME)]
	public static void HandleErroneousNickname(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnNicknameInvalid(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));

	[IrcMessageHandler(ERR_NICKNAMEINUSE)]
	public static void HandleNicknameInUse(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnNicknameTaken(new(line, matchesAsyncRequests, line.Parameters[1], line.Parameters[2]));

	[IrcMessageHandler(ERR_NOTREGISTERED)]
	public static void HandleNotRegistered(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.startTlsCapCheck == TaskStatus.Running && (line.Parameters.Count < 3 || line.Parameters[1] == "PING")) {
			// If we get a response to PING but not to CAP, we know the server does not support capability negotiation.
			// Some servers include the failed command in the reply, but the specification does not.
			// Drop the connection if this happens when STARTTLS is required.
			client.HandleUnsupportedStartTls(new NotSupportedException("The server does not support STARTTLS."));
		}
	}

	[IrcMessageHandler(ERR_CHANNELISFULL)]
	public static void HandleChannelFull(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnChannelJoinDenied(new(line, matchesAsyncRequests, line.Parameters[1], ChannelJoinDeniedReason.Limit, line.Parameters[2]));

	[IrcMessageHandler(ERR_INVITEONLYCHAN)]
	public static void HandleChannelInviteOnly(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnChannelJoinDenied(new(line, matchesAsyncRequests, line.Parameters[1], ChannelJoinDeniedReason.InviteOnly, line.Parameters[2]));

	[IrcMessageHandler(ERR_BANNEDFROMCHAN)]
	public static void HandleChannelBanned(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnChannelJoinDenied(new(line, matchesAsyncRequests, line.Parameters[1], ChannelJoinDeniedReason.Banned, line.Parameters[2]));

	[IrcMessageHandler(ERR_BADCHANNELKEY)]
	public static void HandleChannelKeyFailure(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnChannelJoinDenied(new(line, matchesAsyncRequests, line.Parameters[1], ChannelJoinDeniedReason.KeyFailure, line.Parameters[2]));

	[IrcMessageHandler(RPL_GONEAWAY)]  // 598
	public static void HandleWatchAway(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			if (client.Users.TryGetValue(line.Parameters[1], out var user)) {
				user.AwayReason = line.Parameters[5];
				user.AwaySince = IrcClient.DecodeUnixTime(double.Parse(line.Parameters[4]));
			}
		}
	}

	[IrcMessageHandler(RPL_NOTAWAY)]  // 599
	public static void HandleWatchBack(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			if (client.Users.TryGetValue(line.Parameters[1], out var user)) {
				user.AwayReason = null;
				user.AwaySince = null;
			}
		}
	}

	[IrcMessageHandler(RPL_WATCHOFF)]  // 602
	public static void HandleWatchRemoved(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			client.MonitorList.RemoveInternal(line.Parameters[1]);
		}
	}

	[IrcMessageHandler(RPL_LOGON)]  // 600
	[IrcMessageHandler(RPL_NOWON)]  // 604
	public static void HandleWatchOnline(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			client.pendingMonitor?.Remove(line.Parameters[1]);
			client.MonitorList.AddInternal(line.Parameters[1]);
			var user = client.Users.GetFromMonitor(line.Parameters[1], line.Parameters[2], line.Parameters[3], true);
			user.IsMonitored = true;
			user.AwayReason = null;
			user.AwaySince = null;
			client.OnMonitorOnline(new(line, matchesAsyncRequests, user));
		}
	}

	[IrcMessageHandler(RPL_LOGOFF)]  // 601
	[IrcMessageHandler(RPL_NOWOFF)]  // 605
	public static void HandleWatchOffline(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			client.pendingMonitor?.Remove(line.Parameters[1]);
			client.MonitorList.AddInternal(line.Parameters[1]);
			if (client.Users.TryGetValue(line.Parameters[1], out var user)) {
				client.OnMonitorOffline(new(line, matchesAsyncRequests, user));
				if (user.Channels.Count == 0) {
					user.IsMonitored = false;
					client.Users.Remove(line.Parameters[1]);
					client.OnUserQuit(new(line, matchesAsyncRequests, user, IrcClient.UNKNOWN_QUIT_MESSAGE));
				}
			} else
				client.OnMonitorOffline(new(line, matchesAsyncRequests, new(client, line.Parameters[1], line.Parameters[2], line.Parameters[3], null, line.Parameters[1])));
		}
	}

	[IrcMessageHandler(RPL_WATCHLIST)]  // 606
	public static void HandleWatchList(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			// We'll use this field to 'check off' nicknames that are still in the list.
			client.pendingMonitor ??= new HashSet<string>(client.MonitorList);
			var nicknames = from mask in line.Parameters[1].Split(' ') select Hostmask.GetNickname(mask);
			foreach (var nickname in nicknames) {
				client.pendingMonitor.Remove(nickname);
				client.MonitorList.AddInternal(nickname);
			}
		}
	}

	[IrcMessageHandler(RPL_ENDOFWATCHLIST)]  // 607
	public static void HandleWatchListEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			if (client.pendingMonitor is not null) {
				// If any nicknames remain in this list, they are missing from the monitor list.
				foreach (var nickname in client.pendingMonitor) {
					client.MonitorList.RemoveInternal(nickname);
				}
			}
			client.pendingMonitor = new(client.MonitorList);
		}
	}

	[IrcMessageHandler(RPL_NOWISAWAY)]  // 609
	public static void HandleWatchIsAway(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			client.pendingMonitor?.Remove(line.Parameters[1]);
			client.MonitorList.AddInternal(line.Parameters[1]);
			var user = client.Users.GetFromMonitor(line.Parameters[1], line.Parameters[2], line.Parameters[3], true);
			user.IsMonitored = true;
			user.AwayReason = IrcClient.UNKNOWN_AWAY_MESSAGE;
			user.AwaySince = IrcClient.DecodeUnixTime(double.Parse(line.Parameters[4]));
			client.OnMonitorOnline(new(line, matchesAsyncRequests, user));
		}
	}

	[IrcMessageHandler(RPL_STARTTLS)]  // 670
	public static void HandleStartTls(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		client.pingTimer.Stop();
		if (client.PingTimeout > 0) {
			client.pingTimer.Interval = client.PingTimeout * 1000;
			client.pingTimer.Start();
		}
		try {
			// After the TLS handshake, we restart the entire registration sequence, including the CAP LS request.
			// The server may offer different capabilities over TLS (such as sasl).
			client.TlsHandshake();
			client.SupportedCapabilities.Clear();
			client.PreRegister();
		} catch (Exception ex) {
			client.Disconnect(DisconnectReason.TlsAuthenticationFailed, ex);
			return;
		}
	}

	[IrcMessageHandler(ERR_STARTTLS)]  // 691
	public static void HandleStartTlsError(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.startTlsCapCheck != 0)
			client.HandleUnsupportedStartTls(new AuthenticationException(line.GetParameter(1, "STARTTLS failed")));
	}

	[IrcMessageHandler(RPL_MONONLINE)]  // 730
	public static void HandleMonitorOnline(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			var masks = line.Parameters[1].Split(',');
			foreach (var mask in masks) {
				var nickname = Hostmask.GetNickname(mask);
				client.MonitorList.AddInternal(nickname);
				var user = client.Users.GetFromMonitor(mask, true);
				user.IsMonitored = true;
				client.OnMonitorOnline(new(line, matchesAsyncRequests, user));
			}
		}
	}

	[IrcMessageHandler(RPL_MONOFFLINE)]  // 731
	public static void HandleMonitorOffline(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			var nicknames = line.Parameters[1].Split(',');
			foreach (var nickname in nicknames)
				client.MonitorList.AddInternal(nickname);
			foreach (var nickname in nicknames) {
				if (client.Users.TryGetValue(nickname, out var user)) {
					client.OnMonitorOffline(new(line, matchesAsyncRequests, user));
					if (user.Channels.Count == 0) {
						user.IsMonitored = false;
						client.Users.Remove(nickname);
						client.OnUserQuit(new(line, matchesAsyncRequests, user, IrcClient.UNKNOWN_QUIT_MESSAGE));
					}
				} else
					client.OnMonitorOffline(new(line, matchesAsyncRequests, new(client, nickname, "*", "*", null, nickname)));
			}
		}
	}

	[IrcMessageHandler(RPL_MONLIST)]  // 732
	public static void HandleMonitorList(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			if (client.pendingMonitor == null) {
				// We'll use this field to 'check off' nicknames that are still in the list.
				client.pendingMonitor = new HashSet<string>(client.MonitorList);
			}
			var nicknames = line.Parameters[1].Split(',');
			foreach (var nickname in nicknames) {
				var result = client.pendingMonitor.Remove(nickname);
				if (!result) {
					// This nickname was not known.
					// TODO: it may still be unknown whether or not the user is online. `MONITOR S` may be required to verify this.
					client.MonitorList.AddInternal(nickname);
				}
			}
		}
	}

	[IrcMessageHandler(RPL_ENDOFMONLIST)]  // 733
	public static void HandleMonitorListEnd(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			// If any nicknames remain in this list, they are missing from the monitor list.
			// Null `client.MonitorList` means that we just received an empty list.
			foreach (var nickname in (IEnumerable<string>?) client.pendingMonitor ?? client.MonitorList.ToArray()) {
				client.MonitorList.RemoveInternal(nickname);
			}
			client.pendingMonitor = null;
		}
	}

	[IrcMessageHandler(RPL_MONLISTFULL)]  // 734
	public static void HandleMonitorListFull(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.Extensions.SupportsMonitor) {
			client.Extensions["MONITOR"] = line.Parameters[1];
			var nicknames = line.Parameters[2].Split(',');
			foreach (var nickname in nicknames) {
				client.MonitorList.RemoveInternal(nickname);
			}
		}
	}

	[IrcMessageHandler(RPL_LOGGEDIN)]
	public static void HandleLoggedIn(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		_ = client.Users.Get(line.Parameters[1], false);
		client.Me.Account = line.Parameters[2];
	}

	[IrcMessageHandler(RPL_LOGGEDOUT)]
	public static void HandleLoggedOut(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		_ = client.Users.Get(line.Parameters[1], false);
		client.Me.Account = null;
	}

	[IrcMessageHandler(RPL_SASLSUCCESS)]
	public static void HandleSaslSuccess(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		client.saslSession = null;
		if (client.State < IrcClientState.ReceivingServerInfo) client.Send(CAP, "END");
	}

	[IrcMessageHandler(ERR_NICKLOCKED)]  // 902
	[IrcMessageHandler(ERR_SASLFAIL)]  // 904
	[IrcMessageHandler(ERR_SASLTOOLONG)]  // 905
	[IrcMessageHandler(ERR_SASLABORTED)]  // 906
	public static void HandleSaslFailure(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.saslSession is not null) {
			var result = client.SaslAuthenticateNext();
			if (!result && client.State < IrcClientState.ReceivingServerInfo) {
				if (client.SaslAuthenticationMode == SaslAuthenticationMode.Required) {
					client.disconnectReason = DisconnectReason.SaslAuthenticationFailed;
					client.Send(QUIT, IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_FAILED);
				} else
					client.Send(CAP, "END");
			}
		}
	}

	[IrcMessageHandler(ACCOUNT)]
	public static void HandleAccount(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, false);
		user.Account = line.Parameters[0] == "*" ? null : line.Parameters[0];
	}

	[IrcMessageHandler(AUTHENTICATE)]
	public static void HandleAuthenticate(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		if (client.saslSession is null) return;

		if (line.Parameters[0] != "+") {
			var bytes = Convert.FromBase64String(line.Parameters[0]);
			client.saslSession.Value.challengeStream.Write(bytes, 0, bytes.Length);
		}
		if (line.Parameters[0].Length >= 400)
			// Partial challenge
			return;

		try {
			var challenge = new ArraySegment<byte>(client.saslSession.Value.challengeStream.GetBuffer(), 0, (int) client.saslSession.Value.challengeStream.Length);
			var response = client.saslSession.Value.mechanism.Respond(client, challenge, client.saslSession.Value.state);
			if (response is ArraySegment<byte> responseBytes) {
				int offset = responseBytes.Offset, count = responseBytes.Count;
				while (true) {
					if (count >= 298) {
						// If there are at least 400 base64 characters, send in 400-character lines.
						// 298 is the smallest number of bytes that requires at least 400 characters to encode.
						// The end of the data is indicated by a line with fewer than 400 characters, possibly an empty line.
						var n = Math.Min(count, 300);
						client.Send(AUTHENTICATE, Convert.ToBase64String(responseBytes.Array!, offset, n));
						offset += n;
						count -= n;
					} else {
						if (count == 0)
							client.Send(AUTHENTICATE, "+");
						else
							client.Send(AUTHENTICATE, Convert.ToBase64String(responseBytes.Array!, offset, count));
						break;
					}
				}
			} else
				client.Send(AUTHENTICATE, "*");
		} catch (Exception ex) {
			client.OnException(new(ex, false));
			client.Send(AUTHENTICATE, "*");
		}
	}

	[IrcMessageHandler(CAP)]
	public static void HandleCap(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		string subcommand = line.Parameters[1];
		switch (subcommand.ToUpperInvariant()) {
			case "LS":
			case "NEW":
				if (line.Parameters[2] == "*") {
					// The * indicates that this is not the last message in the list.
					if (line.Parameters[3] != "")
						client.pendingCapabilities.AddRange(line.Parameters[3].Split(' '));
				} else {
					if (line.Parameters[2] != "")
						client.pendingCapabilities.AddRange(line.Parameters[2].Split(' '));

					var newCapabilities = new IrcNamedEntityCollection<IrcCapability>(StringComparer.Ordinal);
					var enableCapabilities = new HashSet<string>();

					foreach (string token in client.pendingCapabilities) {
						for (int i = 0; i < token.Length; ++i) {
							if (token[i] is not ('-' or '=' or '~')) {
								// The = and ~ prefixes for capabilities are deprecated and ignored here.
								int pos = token.IndexOf('=', i);

								var cap = pos < 0
									? new IrcCapability(token[i..])
									: new IrcCapability(token[i..pos], token[(pos + 1)..]);
								if (!client.SupportedCapabilities.Contains(cap.Name)) {
									client.SupportedCapabilities.Add(cap);
									newCapabilities.Add(cap);
									if (cap.Name == "sasl") {
										if (client.SaslAuthenticationMode != SaslAuthenticationMode.Disabled) {
											var mechanisms = string.IsNullOrEmpty(cap.Parameter) ? null : cap.Parameter!.Split(',');
											if (client.SaslMechanisms.Any(m => (mechanisms is null || mechanisms.Contains(m.Name)) && m.CanAttempt(client)))
												enableCapabilities.Add(cap.Name);
											else if (client.SaslAuthenticationMode == SaslAuthenticationMode.Required) {
												client.disconnectReason = DisconnectReason.SaslAuthenticationFailed;
												client.Send(QUIT, IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_MECHANISM_NOT_SUPPORTED);
												return;
											}
										}
									} else if (IrcClient.BaseSupportedCapabilities.Contains(cap.Name))
										enableCapabilities.Add(cap.Name);
								}

								break;
							}
						}
					}
					
					if (client.State < IrcClientState.ReceivingServerInfo && newCapabilities.TryGetValue("sts", out var stsCap) && stsCap.Parameter is not null) {
						var tokens = stsCap.Parameter.Split(',').Select(s => s.Split(new[] { '=' }, 2)).ToDictionary(a => a[0], a => a.Length > 1 ? a[1] : "");
						if (client.SslStream is null && tokens.TryGetValue("port", out var portString) && ushort.TryParse(portString, out var port) && port != 0) {
							client.pingTimer.Stop();
							client.startTlsCapCheck = 0;
							client.OnStsUpgrade(new(port));
							client.DisconnectTcpClient();
							client.Tls = TlsMode.Tls;
							client.Connect(client.Address!, port);
							return;
						} else if (client.SslStream is not null) {
							if (tokens.TryGetValue("duration", out var durationString) && long.TryParse(durationString, out var duration)) {
								if (duration <= 0) {
									if (client.StsPolicy is not null) {
										client.StsPolicy = null;
										client.OnStsPolicyChanged(EventArgs.Empty);
									}
								} else {
									client.StsPolicy = new(TimeSpan.FromSeconds(duration), tokens.ContainsKey("preload"));
									client.OnStsPolicyChanged(EventArgs.Empty);
								}
							}
						}
					}

					if (client.startTlsCapCheck != 0) {
						client.pingTimer.Stop();
						client.startTlsCapCheck = 0;
						if (newCapabilities.Contains("tls")) {
							// During the STARTTLS handshake, we got a CAP reply indicating STARTTLS support.
							// Attempt STARTTLS and repeat the entire registration sequence over TLS.
							client.Send(STARTTLS);
							return;
						} else {
							// During the STARTTLS handshake, we got a CAP reply indicating no STARTTLS support.
							// If STARTTLS is not required, send the registration sequence and do normal CAP negotiation.
							if (!client.HandleUnsupportedStartTls(new NotSupportedException("The server does not support STARTTLS.")))
								return;
						}
					}

					if (client.State < IrcClientState.ReceivingServerInfo && client.SaslAuthenticationMode == SaslAuthenticationMode.Required && !newCapabilities.Contains("sasl")) {
						client.disconnectReason = DisconnectReason.SaslAuthenticationFailed;
						client.Send(QUIT, IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_NOT_SUPPORTED);
						return;
					}
					if (newCapabilities.Count > 0) {
						client.OnCapabilitiesAdded(new(line, matchesAsyncRequests, newCapabilities, enableCapabilities));
						if (client.disconnectReason != 0) return;
					}
					if (client.State < IrcClientState.ReceivingServerInfo && client.SaslAuthenticationMode == SaslAuthenticationMode.Required && !enableCapabilities.Contains("sasl")) {
						client.disconnectReason = DisconnectReason.SaslAuthenticationFailed;
						client.Send(QUIT, IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_NOT_ENABLED);
					} else if (enableCapabilities.Count > 0) {
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
						client.Send(CAP, "REQ", string.Join(' ', enableCapabilities));
#else
						client.Send(CAP, "REQ", string.Join(" ", enableCapabilities));
#endif
					} else if (client.State < IrcClientState.ReceivingServerInfo)
						client.Send(CAP, "END");
				}

				break;
			case "ACK":
				var sasl = false;
				var fields = line.Parameters[2].Split(' ');
				foreach (string field in fields) {
					bool remove = false;
					for (int i = 0; i < field.Length; ++i) {
						if (field[i] == '-') {
							remove = true;
						} else if (field[i] == '=') {
						} else if (field[i] == '~') {
						} else {
							var capName = field[i..];
							if (remove) {
								client.EnabledCapabilities.Remove(capName);
							}
							if (!client.SupportedCapabilities.TryGetValue(capName, out var cap)) {
								cap = new IrcCapability(capName);
								client.SupportedCapabilities.Add(cap);
							}
							client.EnabledCapabilities.AddOrUpdate(cap);
							if (capName == "sasl") sasl = true;

							break;
						}
					}
				}

				if (sasl && client.SaslAuthenticationMode != SaslAuthenticationMode.Disabled) {
					var result = client.SaslAuthenticate();
					if (!result) {
						if (client.SaslAuthenticationMode == SaslAuthenticationMode.Required && client.State < IrcClientState.ReceivingServerInfo) {
							client.disconnectReason = DisconnectReason.SaslAuthenticationFailed;
							client.Send(QUIT, IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_MECHANISM_NOT_SUPPORTED);
						} else
							client.Send(CAP, "END");
					}
				} else if (client.State < IrcClientState.ReceivingServerInfo)
					client.Send(CAP, "END");

				break;
			case "NAK":
				if (client.State < IrcClientState.ReceivingServerInfo)
					client.Send(CAP, "END");
				break;
			case "DEL":
				var capabilities = new IrcNamedEntityCollection<IrcCapability>(StringComparer.Ordinal);
				fields = line.Parameters[2].Split(' ');
				foreach (string field in fields) {
					if (field != "sts") {
						if (client.SupportedCapabilities.TryGetValue(field, out var cap)) {
							client.SupportedCapabilities.Remove(field);
							client.EnabledCapabilities.Remove(field);
							capabilities.Add(cap);
						}
					}
				}
				if (capabilities.Count != 0)
					client.OnCapabilitiesDeleted(new(line, matchesAsyncRequests, capabilities));
				break;
		}
	}

	[IrcMessageHandler(CHGHOST)]
	public static void HandleChgHost(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		string nickname = Hostmask.GetNickname(line.Source ?? client.ServerName!);
		if (client.Users.TryGetValue(nickname, out var user)) {
			user.Ident = line.Parameters[0];
			user.Host = line.Parameters[1];
		}
	}

	[IrcMessageHandler(ERROR)]
	public static void HandleError(IrcClient client, IrcLine line, bool matchesAsyncRequests) => client.OnServerError(new(line, matchesAsyncRequests, line.Parameters[0]));

	[IrcMessageHandler(INVITE)]
	public static void HandleInvite(IrcClient client, IrcLine line, bool matchesAsyncRequests)
		=> client.OnInvite(new(line, matchesAsyncRequests, client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false), line.Parameters[0], line.Parameters[1]));

	[IrcMessageHandler(JOIN)]
	public static void HandleJoin(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		IrcUser user; Task? namesTask;
		bool onChannel = client.Channels.TryGetValue(line.Parameters[0], out var channel);

		if (line.Parameters.Count == 3) {
			// Extended join
			user = client.Users.GetFromExtendedJoin(line.Source ?? client.ServerName!, line.Parameters[1], line.Parameters[2], line.Tags, onChannel);
		} else
			user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, onChannel);

		if (!onChannel && user.IsMe) {
			if (client.Users.Count == 0) client.Users.Add(client.Me);

			channel = new IrcChannel(client, line.Parameters[0]);
			channel.Users.Add(new IrcChannelUser(client, channel, user.Nickname) { JoinTime = client.testUtcNow ?? DateTime.UtcNow });
			client.Channels.Add(channel);
		} else {
			channel ??= new IrcChannel(client, line.Parameters[0]);
			if (!user.Channels.Contains(line.Parameters[0])) {
				channel.Users.Add(new IrcChannelUser(client, channel, user.Nickname) { JoinTime = client.testUtcNow ?? DateTime.UtcNow });
				user.Channels.Add(channel);
			}
		}
		if (user.IsMe) {
			var asyncRequest = new AsyncRequest.VoidAsyncRequest(client, null, RPL_ENDOFNAMES, new[] { null, line.Parameters[0] });
			client.AddAsyncRequest(asyncRequest);
			namesTask = asyncRequest.Task;
		} else
			namesTask = null;
		client.OnChannelJoin(new(line, matchesAsyncRequests, user, channel, namesTask));
	}

	[IrcMessageHandler(KICK)]
	public static void HandleKick(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);
		var channel = client.Channels.Get(line.Parameters[0]);
		if (!channel.Users.TryGetValue(line.Parameters[1], out var target)) target = new IrcChannelUser(client, channel, line.Parameters[1]);

		var targetUser = target.User;
		var disappearedUsers = client.RemoveUserFromChannel(channel, targetUser);

		client.OnChannelKick(new(line, matchesAsyncRequests, user, channel, target, line.Parameters.Count >= 3 ? line.Parameters[2] : ""));
		client.OnChannelLeave(new(line, matchesAsyncRequests, targetUser, channel, "Kicked out by " + user.Nickname + ": " + (line.Parameters.Count >= 3 ? line.Parameters[2] : null)));

		if (disappearedUsers != null) {
			foreach (var disappearedUser in disappearedUsers) {
				client.Users.Remove(disappearedUser);
				client.OnUserDisappeared(new(disappearedUser));
			}
		}
	}

	[IrcMessageHandler(KILL)]
	public static void HandleKill(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);
		if (client.CaseMappingComparer.Equals(line.Parameters[0], client.Me.Nickname)) {
			client.OnKilled(new(line, matchesAsyncRequests, user, client.Me.Nickname, line.Parameters[1]));
		}
	}

	[IrcMessageHandler(MODE)]
	public static void HandleMode(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);
		if (client.IsChannel(line.Parameters[0])) {
			var channel = client.Channels.Get(line.Parameters[0]);
			client.HandleChannelModes(line, matchesAsyncRequests, user, channel, line.Parameters[1], line.Parameters.Skip(2), true);
		} else if (client.CaseMappingComparer.Equals(line.Parameters[0], client.Me.Nickname)) {
			bool direction = true;
			foreach (char c in line.Parameters[1]) {
				if (c == '+') direction = true;
				else if (c == '-') direction = false;
				else {
					if (direction) client.UserModes.Add(c);
					else client.UserModes.Remove(c);
				}
			}
			client.OnUserModesSet(new(line, matchesAsyncRequests, line.Parameters[1]));
		}
	}

	[IrcMessageHandler(NICK)]
	public static void HandleNick(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);
		var oldNickname = user.Nickname;

		client.OnNicknameChange(new(line, matchesAsyncRequests, user, line.Parameters[0]));

		if (client.Users.TryGetValue(user.Nickname, out user)) {
			client.Users.Remove(user);
			user.Nickname = line.Parameters[0];
			if (client.Users.TryGetValue(user.Nickname, out var existingUser)) {
				client.OnUserDisappeared(new(existingUser));
				client.Users.Remove(user.Nickname);
			}
			client.Users.Add(user);

			foreach (var channel in user.Channels) {
				var channelUser = channel.Users[oldNickname];
				channel.Users.Remove(channelUser);
				channel.Users.Remove(user.Nickname);
				channelUser.Nickname = line.Parameters[0];
				channel.Users.Add(channelUser);
			}
		}
	}

	[IrcMessageHandler(NOTICE)]
	public static void HandleNotice(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var sender = client.Users.Get(line.Source ?? client.ServerName ?? client.Address!, line.Tags, false);
		if (line.Parameters[0][0] == '$')
			client.OnBroadcastNotice(new(line, matchesAsyncRequests, sender, line.Parameters[0], line.Parameters[1]));
		else if (client.IsChannelWithStatusPrefix(line.Parameters[0], out var channelName, out var status)) {
			client.OnChannelNotice(new(line, matchesAsyncRequests, sender, client.Channels.Get(channelName), line.Parameters[1], status));
		} else {
			client.OnPrivateNotice(new(line, matchesAsyncRequests, sender, line.Parameters[0], line.Parameters[1]));
		}
	}

	[IrcMessageHandler(PART)]
	public static void HandlePart(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);
		var channel = client.Channels.Get(line.Parameters[0]);
		var disappearedUsers = client.RemoveUserFromChannel(channel, user);

		client.OnChannelPart(new(line, matchesAsyncRequests, user, channel, line.Parameters.Count == 1 ? null : line.Parameters[1]));
		client.OnChannelLeave(new(line, matchesAsyncRequests, user, channel, line.Parameters.Count == 1 ? null : line.Parameters[1]));

		if (disappearedUsers != null) {
			foreach (var disappearedUser in disappearedUsers) {
				client.Users.Remove(disappearedUser);
				client.OnUserDisappeared(new(disappearedUser));
			}
		}
	}

	[IrcMessageHandler(PING)]
	public static void HandlePing(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		client.OnPingReceived(new(line, matchesAsyncRequests, line.Parameters.Count == 0 ? "" : line.Parameters[0]));
		if (client.disconnectReason == 0) {
			if (line.Parameters.Count == 0) client.Send(PONG, Array.Empty<string>());
			else client.Send(PONG, line.Parameters[0]);
		}
	}

	[IrcMessageHandler(PONG)]
	public static void HandlePong(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		client.pinged = false;
		client.OnPong(new(line, matchesAsyncRequests, line.Source ?? client.ServerName!));

		if (client.startTlsCapCheck == TaskStatus.Running) {
			// If we get a response to PING but not to CAP, we know the server does not support capability negotiation.
			// Drop the connection if this happens when STARTTLS is required.
			client.HandleUnsupportedStartTls(new NotSupportedException("The server does not support STARTTLS."));
		}
	}

	[IrcMessageHandler(PRIVMSG)]
	public static void HandlePrivmsg(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);

		if (line.Parameters[0][0] == '$')
			client.OnBroadcastMessage(new(line, matchesAsyncRequests, user, line.Parameters[0], line.Parameters[1]));
		else if (client.IsChannelWithStatusPrefix(line.Parameters[0], out var channelName, out var status)) {
			// It's a channel message.
			if (line.Parameters[1].Length > 1 && line.Parameters[1][0] == '\u0001') {
				string ctcpMessage = line.Parameters[1].Trim(new[] { '\u0001' });
				string[] fields = ctcpMessage.Split(new char[] { ' ' }, 2);
				if (fields[0].Equals("ACTION", StringComparison.OrdinalIgnoreCase)) {
					client.OnChannelAction(new(line, matchesAsyncRequests, user, client.Channels.Get(channelName), fields.ElementAtOrDefault(1) ?? "", status));
				} else {
					client.OnChannelCTCP(new(line, matchesAsyncRequests, user, client.Channels.Get(channelName), ctcpMessage, status));
				}
			} else {
				client.OnChannelMessage(new(line, matchesAsyncRequests, user, client.Channels.Get(channelName), line.Parameters[1], status));
			}
		} else {
			// It's a private message.
			if (line.Parameters[1].Length > 1 && line.Parameters[1][0] == '\u0001') {
				string CTCPMessage = line.Parameters[1].Trim(new[] { '\u0001' });
				string[] fields = CTCPMessage.Split(new char[] { ' ' }, 2);
				if (fields[0].Equals("ACTION", StringComparison.OrdinalIgnoreCase)) {
					client.OnPrivateAction(new(line, matchesAsyncRequests, user, line.Parameters[0], fields.ElementAtOrDefault(1) ?? ""));
				} else {
					client.OnPrivateCTCP(new(line, matchesAsyncRequests, user, line.Parameters[0], CTCPMessage));
				}
			} else {
				client.OnPrivateMessage(new(line, matchesAsyncRequests, user, line.Parameters[0], line.Parameters[1]));
			}
		}
	}

	[IrcMessageHandler(QUIT)]
	public static void HandleQuit(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);
		var message = line.Parameters.Count >= 1 ? line.Parameters[0] : null;
		if (user.IsMe) {
			client.OnUserQuit(new(line, matchesAsyncRequests, user, message ?? IrcClient.UNKNOWN_QUIT_MESSAGE));
		} else {
			client.Users.Remove(user);
			foreach (var channel in user.Channels)
				channel.Users.Remove(user.Nickname);

			client.OnUserQuit(new(line, matchesAsyncRequests, user, message ?? IrcClient.UNKNOWN_QUIT_MESSAGE));
			foreach (var channel in user.Channels)
				client.OnChannelLeave(new(line, matchesAsyncRequests, user, channel, (message != null && message.StartsWith("Quit:") ? "" : "Disconnected: ") + message));

			user.IsMonitored = false;
			user.Channels.Clear();
		}
	}

	[IrcMessageHandler(TAGMSG)]
	public static void HandleTagmsg(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);

		if (line.Parameters[0][0] == '$')
			client.OnBroadcastTagMessage(new(line, matchesAsyncRequests, user, line.Parameters[0]));
		else if (client.IsChannelWithStatusPrefix(line.Parameters[0], out var channelName, out var status)) {
			client.OnChannelTagMessage(new(line, matchesAsyncRequests, user, client.Channels.Get(channelName), status));
		} else {
			client.OnPrivateTagMessage(new(line, matchesAsyncRequests, user, line.Parameters[0]));
		}
	}

	[IrcMessageHandler(TOPIC)]
	public static void HandleTopic(IrcClient client, IrcLine line, bool matchesAsyncRequests) {
		var user = client.Users.Get(line.Source ?? client.ServerName!, line.Tags, false);
		var channel = client.Channels.Get(line.Parameters[0]);

		var oldTopic = channel.Topic;
		var oldTopicSetter = channel.TopicSetter;
		var oldTopicStamp = channel.TopicStamp;

		channel.Topic = line.Parameters.Count > 1 ? line.Parameters[1] : null;

		channel.TopicSetter = user.ToString();
		channel.TopicStamp = client.testUtcNow ?? DateTime.UtcNow;

		client.OnChannelTopicChange(new(line, matchesAsyncRequests, user, channel, oldTopic, oldTopicSetter, oldTopicStamp));
	}
}
