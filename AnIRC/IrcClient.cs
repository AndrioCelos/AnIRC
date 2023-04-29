using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

using static AnIRC.Replies;

using Timer = System.Timers.Timer;

namespace AnIRC;

/// <summary>Represents a method that handles a client-bound IRC message.</summary>
/// <param name="client">The <see cref="IrcClient"/> receiving the message.</param>
/// <param name="line">The content of the message.</param>
public delegate void IrcMessageHandler(IrcClient client, IrcLine line, bool matchesAsyncRequests);

/// <summary>
/// Manages a connection to an IRC network.
/// </summary>
public class IrcClient : IDisposable {
	private const int SIZE_LIMIT_TAGS_PART = 4094;
	private const int SIZE_LIMIT_TAGS = 8191;
	private const int SIZE_LIMIT_MESSAGE = 512;
	private const int RECEIVE_BUFFER_SIZE = 16384;
	private const int SEND_BUFFER_SIZE = 8192;

	internal const string UNKNOWN_AWAY_MESSAGE = "Unknown away message";
	internal const string UNKNOWN_QUIT_MESSAGE = "Quit";
	internal const string QUIT_MESSAGE_STARTTLS_NOT_SUPPORTED = "STARTTLS is required but not supported by the server.";
	internal const string QUIT_MESSAGE_SASL_AUTHENTICATION_NOT_SUPPORTED = "SASL authentication is required but not supported by the server.";
	internal const string QUIT_MESSAGE_SASL_AUTHENTICATION_MECHANISM_NOT_SUPPORTED = "SASL authentication is required but no shared mechanisms are available.";
	internal const string QUIT_MESSAGE_SASL_AUTHENTICATION_NOT_ENABLED = "SASL authentication is required but was not enabled.";
	internal const string QUIT_MESSAGE_SASL_AUTHENTICATION_FAILED = "SASL authentication is required but failed.";
	internal const string QUIT_MESSAGE_CASEMAPPING_COLLISION = "Name collision due to CASEMAPPING change.";
	internal static readonly UTF8Encoding Utf8Encoding = new(false, false);
	private static readonly Regex RemoveCodesRegex = new(@"[\u0002\u000F\u0011\u0016\u001D\u001E\u001F]|\x03\d{0,2}(,\d{1,2})?|\x04[\da-f]{0,6}(,[\da-f]{1,2})?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	internal static readonly HashSet<string> BaseSupportedCapabilities = new() {
		"account-notify", "account-tag", "away-notify", "cap-notify", "extended-join", "message-tags", "multi-prefix", "sasl", "userhost-in-names"
	};

	#region Events
	// TODO: Remove/reorganise/merge some of these?
	/// <summary>Raised when the local user ceases to be marked as away.</summary>
	public event EventHandler? AwayCancelled;
	/// <summary>Raised when an away message for another user is received.</summary>
	public event EventHandler<AwayMessageEventArgs>? AwayMessage;
	/// <summary>Raised when the local user is marked as away.</summary>
	public event EventHandler? AwaySet;
	/// <summary>Raised when a PRIVMSG is received from a broadcast.</summary>
	public event EventHandler<PrivateMessageEventArgs>? BroadcastMessage;
	/// <summary>Raised when a NOTICE is received from a broadcast.</summary>
	public event EventHandler<PrivateMessageEventArgs>? BroadcastNotice;
	/// <summary>Raised when a TAGMSG is received from a broadcast.</summary>
	public event EventHandler<PrivateTagMessageEventArgs>? BroadcastTagMessage;
	/// <summary>Raised when new IRCv3 capabilities become available.</summary>
	public event EventHandler<CapabilitiesAddedEventArgs>? CapabilitiesAdded;
	/// <summary>Raised when IRCv3 capabilities become unavailable.</summary>
	public event EventHandler<CapabilitiesEventArgs>? CapabilitiesDeleted;
	/// <summary>Raised when a user describes an action on a channel.</summary>
	public event EventHandler<ChannelMessageEventArgs>? ChannelAction;
	/// <summary>Raised when a ban is set (+b) on a channel.</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelBan;
	/// <summary>Raised when a channel ban list entry is received.</summary>
	public event EventHandler<ChannelModeListEventArgs>? ChannelBanList;
	/// <summary>Raised when the end of a channel ban list is seen.</summary>
	public event EventHandler<ChannelModeListEndEventArgs>? ChannelBanListEnd;
	/// <summary>Raised when a ban is removed (-b) from a channel.</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelBanRemoved;
	/// <summary>Raised when a CTCP request is received to a channel.</summary>
	public event EventHandler<ChannelMessageEventArgs>? ChannelCTCP;
	/// <summary>Raised when a ban exception is set (+e) on a channel.</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelExempt;
	/// <summary>Raised when a ban exception is removed (-e) on a channel.</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelExemptRemoved;
	/// <summary>Raised when an invite exemption is set (+I) on a channel.</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelInviteExempt;
	/// <summary>Raised when a channel invite exemption list entry is received. </summary>
	public event EventHandler<ChannelModeListEventArgs>? ChannelInviteExemptList;
	/// <summary>Raised when the end of a channel invite exemption list is seen.</summary>
	public event EventHandler<ChannelModeListEndEventArgs>? ChannelInviteExemptListEnd;
	/// <summary>Raised when an invite exemption is removed (-I) on a channel.</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelInviteExemptRemoved;
	/// <summary>Raised when a user, including the local user, joins a channel.</summary>
	public event EventHandler<ChannelJoinEventArgs>? ChannelJoin;
	/// <summary>Raised when a join attempt fails.</summary>
	public event EventHandler<ChannelJoinDeniedEventArgs>? ChannelJoinDenied;
	/// <summary>Raised when a channel's key is removed (-k).</summary>
	public event EventHandler<ChannelChangeEventArgs>? ChannelKeyRemoved;
	/// <summary>Raised when a key is set (+k) on a channel.</summary>
	public event EventHandler<ChannelKeyEventArgs>? ChannelKeySet;
	/// <summary>Raised when a user, including the local user, is kicked out of a channel.</summary>
	public event EventHandler<ChannelKickEventArgs>? ChannelKick;
	/// <summary>Raised after a more specific event when a user leaves a channel by any means.</summary>
	public event EventHandler<ChannelPartEventArgs>? ChannelLeave;
	/// <summary>Raised when a channel's user limit is removed (-l).</summary>
	public event EventHandler<ChannelChangeEventArgs>? ChannelLimitRemoved;
	/// <summary>Raised when a user limit is set (+l) on a channel.</summary>
	public event EventHandler<ChannelLimitEventArgs>? ChannelLimitSet;
	/// <summary>Raised when a channel list entry is seen.</summary>
	public event EventHandler<ChannelListEventArgs>? ChannelList;
	/// <summary>Raised when a non-standard status mode has been set or removed on a channel user.</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelListChanged;
	/// <summary>Raised when the end of the channel list is seen.</summary>
	public event EventHandler<ChannelListEndEventArgs>? ChannelListEnd;
	/// <summary>Raised when a user sends a PRIVMSG to a channel.</summary>
	public event EventHandler<ChannelMessageEventArgs>? ChannelMessage;
	/// <summary>Raised when a PRIVMSG attempt fails.</summary>
	public event EventHandler<ChannelJoinDeniedEventArgs>? ChannelMessageDenied;
	/// <summary>Raised when a channel's modes are received.</summary>
	public event EventHandler<ChannelModesSetEventArgs>? ChannelModesGet;
	/// <summary>Raised when modes are set on a channel, after other channel mode events.</summary>
	public event EventHandler<ChannelModesSetEventArgs>? ChannelModesSet;
	/// <summary>Raised when a user sends a NOTICE to a channel.</summary>
	public event EventHandler<ChannelMessageEventArgs>? ChannelNotice;
	/// <summary>Raised when a user, including the local user, parts a channel.</summary>
	public event EventHandler<ChannelPartEventArgs>? ChannelPart;
	/// <summary>Raised when a quiet is set (+q) on a channel/</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelQuiet;
	/// <summary>Raised when a quiet is removed (-q) from a channel.</summary>
	public event EventHandler<ChannelListChangedEventArgs>? ChannelQuietRemoved;
	/// <summary>Raised when a user's channel status changes.</summary>
	public event EventHandler<ChannelStatusChangedEventArgs>? ChannelStatusChanged;
	/// <summary>Raised when a user sends a TAGMSG to a channel.</summary>
	public event EventHandler<ChannelTagMessageEventArgs>? ChannelTagMessage;
	/// <summary>Raised when a channel timestamp is received.</summary>
	public event EventHandler<ChannelTimestampEventArgs>? ChannelTimestamp;
	/// <summary>Raised when a channel topic is changed.</summary>
	public event EventHandler<ChannelTopicChangeEventArgs>? ChannelTopicChanged;
	/// <summary>Raised when a channel topic is received.</summary>
	public event EventHandler<ChannelTopicEventArgs>? ChannelTopicReceived;
	/// <summary>Raised when a channel topic stamp is received.</summary>
	public event EventHandler<ChannelTopicStampEventArgs>? ChannelTopicStamp;
	/// <summary>Raised when the IRC connection is lost.</summary>
	public event EventHandler<DisconnectEventArgs>? Disconnected;
	/// <summary>Raised when an exception occurs in the connection.</summary>
	public event EventHandler<ExceptionEventArgs>? Exception;
	/// <summary>Raised when a channel ban exception list entry is received.</summary>
	public event EventHandler<ChannelModeListEventArgs>? ExemptList;
	/// <summary>Raised when the end of a channel ban exception list is seen.</summary>
	public event EventHandler<ChannelModeListEndEventArgs>? ExemptListEnd;
	/// <summary>Raised when the local user is invited to a channel.</summary>
	public event EventHandler<InviteEventArgs>? Invite;
	/// <summary>Raised when a channel invite is sent.</summary>
	public event EventHandler<InviteSentEventArgs>? InviteSent;
	/// <summary>Raised when the local user is killed.</summary>
	public event EventHandler<PrivateMessageEventArgs>? Killed;
	/// <summary>Raised after the <see cref="UserQuit"/> or <see cref="NicknameChange"/> event when a user on the monitor list goes offline or changes their nickname.</summary>
	public event EventHandler<IrcUserLineEventArgs>? MonitorOffline;
	/// <summary>Raised after the <see cref="UserAppeared"/> event when a user on the monitor list appears online.</summary>
	public event EventHandler<IrcUserLineEventArgs>? MonitorOnline;
	/// <summary>Raised when part of the MOTD is seen.</summary>
	public event EventHandler<MotdEventArgs>? MOTD;
	/// <summary>Raised when part of a channel names list is seen.</summary>
	public event EventHandler<ChannelNamesEventArgs>? Names;
	/// <summary>Raised when the end of a channel names list is seen.</summary>
	public event EventHandler<ChannelModeListEndEventArgs>? NamesEnd;
	/// <summary>Raised when a user's nickname changes.</summary>
	public event EventHandler<NicknameChangeEventArgs>? NicknameChange;
	/// <summary>Raised when a nickname change attempt fails.</summary>
	public event EventHandler<NicknameEventArgs>? NicknameChangeFailed;
	/// <summary>Raised when a nickname change attempt fails because the nickname is invalid.</summary>
	public event EventHandler<NicknameEventArgs>? NicknameInvalid;
	/// <summary>Raised when a nickname change attempt fails because the nickname is taken.</summary>
	public event EventHandler<NicknameEventArgs>? NicknameTaken;
	/// <summary>Raised when a PONG is received.</summary>
	public event EventHandler<PingEventArgs>? Pong;
	/// <summary>Raised when a PING is received.</summary>
	public event EventHandler<PingEventArgs>? PingReceived;
	/// <summary>Raised when a user describes an action in private.</summary>
	public event EventHandler<PrivateMessageEventArgs>? PrivateAction;
	/// <summary>Raised when a CTCP request is received in private.</summary>
	public event EventHandler<PrivateMessageEventArgs>? PrivateCTCP;
	/// <summary>Raised when a PRIVMSG is received in private.</summary>
	public event EventHandler<PrivateMessageEventArgs>? PrivateMessage;
	/// <summary>Raised when a NOTICE is received in private.</summary>
	public event EventHandler<PrivateMessageEventArgs>? PrivateNotice;
	/// <summary>Raised when a TAGMSG is received in private.</summary>
	public event EventHandler<PrivateTagMessageEventArgs>? PrivateTagMessage;
	/// <summary>Raised when a line is received from the server, before any other processing.</summary>
	public event EventHandler<IrcLineEventArgs>? RawLineReceived;
	/// <summary>Raised when a line is sent.</summary>
	public event EventHandler<RawLineEventArgs>? RawLineSent;
	/// <summary>Raised when a line is received from the server that isn't handled.</summary>
	public event EventHandler<IrcLineEventArgs>? RawLineUnhandled;
	/// <summary>Raised when registration completes.</summary>
	public event EventHandler<RegisteredEventArgs>? Registered;
	/// <summary>Raised when the client may present a TLS certificate for authentication.</summary>
	/// <remarks>On Windows, the certificate must not have an ephemeral key; see https://github.com/dotnet/runtime/issues/23749#issuecomment-388231655</remarks>
	public event EventHandler<SelectCertificateEventArgs>? SelectCertificate;
	/// <summary>Raised when an ERROR message is received.</summary>
	public event EventHandler<ServerErrorEventArgs>? ServerError;
	/// <summary>Raised when the State property changes.</summary>
	public event EventHandler<StateEventArgs>? StateChanged;
	/// <summary>Raised when the <see cref="StsPolicy"/> property changes or the STS policy has been refreshed.</summary>
	public event EventHandler? StsPolicyChanged;
	/// <summary>Raised when the client is upgrading the connection in accordance with an STS upgrade policy.</summary>
	public event EventHandler<StsUpgradeEventArgs>? StsUpgrade;
	/// <summary>Raised when a previously unseen user appears.</summary>
	public event EventHandler<IrcUserEventArgs>? UserAppeared;
	/// <summary>Raised when we lose sight of a user, but they did not leave the network.</summary>
	public event EventHandler<IrcUserEventArgs>? UserDisappeared;
	/// <summary>Raised when user modes are received.</summary>
	public event EventHandler<UserModesEventArgs>? UserModesGet;
	/// <summary>Raised when user modes are set.</summary>
	public event EventHandler<UserModesEventArgs>? UserModesSet;
	/// <summary>Raised when a user, including the local user, quits the IRC network.</summary>
	public event EventHandler<QuitEventArgs>? UserQuit;
	/// <summary>Raised when the server presents an untrusted TLS certificate. Set e.Valid to true to allow the connection.</summary>
	public event EventHandler<ValidateCertificateEventArgs>? ValidateCertificate;
	/// <summary>Raised when a WALLOPS message is received.</summary>
	public event EventHandler<PrivateMessageEventArgs>? Wallops;
	/// <summary>Raised when a WHOIS authentication line is received.</summary>
	public event EventHandler<WhoisAuthenticationEventArgs>? WhoIsAuthenticationLine;
	/// <summary>Raised when a WHOIS channels line is received.</summary>
	public event EventHandler<WhoisChannelsEventArgs>? WhoIsChannelLine;
	/// <summary>Raised when the end of a WHOIS listing is received.</summary>
	public event EventHandler<WhoisEndEventArgs>? WhoIsEnd;
	/// <summary>Raised when a WHOIS helper line is received.</summary>
	public event EventHandler<WhoisOperEventArgs>? WhoIsHelperLine;
	/// <summary>Raised when a WHOIS idle line is received.</summary>
	public event EventHandler<WhoisIdleEventArgs>? WhoIsIdleLine;
	/// <summary>Raised when a WHOIS name line is received.</summary>
	public event EventHandler<WhoisNameEventArgs>? WhoIsNameLine;
	/// <summary>Raised when a WHOIS oper line is received.</summary>
	public event EventHandler<WhoisOperEventArgs>? WhoIsOperLine;
	/// <summary>Raised when a WHOIS real host line is received.</summary>
	public event EventHandler<WhoisRealHostEventArgs>? WhoIsRealHostLine;
	/// <summary>Raised when a WHOIS server line is received.</summary>
	public event EventHandler<WhoisServerEventArgs>? WhoIsServerLine;
	/// <summary>Raised when a WHO list entry is received.</summary>
	public event EventHandler<WhoListEventArgs>? WhoList;
	/// <summary>Raised when the end of a WHOWAS list is received.</summary>
	public event EventHandler<WhoisEndEventArgs>? WhoWasEnd;
	/// <summary>Raised when a WHOWAS name line is received.</summary>
	public event EventHandler<WhoisNameEventArgs>? WhoWasNameLine;
	/// <summary>Raised when a WHOX list entry is received.</summary>
	public event EventHandler<WhoxListEventArgs>? WhoxList;
	#endregion

	#region Event methods
	protected internal void OnAwayCancelled(EventArgs e) => this.AwayCancelled?.Invoke(this, e);
	protected internal void OnAwayMessage(AwayMessageEventArgs e) => this.AwayMessage?.Invoke(this, e);
	protected internal void OnAwaySet(EventArgs e) => this.AwaySet?.Invoke(this, e);
	protected internal void OnCapabilitiesAdded(CapabilitiesAddedEventArgs e) => this.CapabilitiesAdded?.Invoke(this, e);
	protected internal void OnCapabilitiesDeleted(CapabilitiesEventArgs e) => this.CapabilitiesDeleted?.Invoke(this, e);
	protected internal void OnBanList(ChannelModeListEventArgs e) => this.ChannelBanList?.Invoke(this, e);
	protected internal void OnBanListEnd(ChannelModeListEndEventArgs e) => this.ChannelBanListEnd?.Invoke(this, e);
	protected internal void OnBroadcastMessage(PrivateMessageEventArgs e) => this.BroadcastMessage?.Invoke(this, e);
	protected internal void OnBroadcastNotice(PrivateMessageEventArgs e) => this.BroadcastNotice?.Invoke(this, e);
	protected internal void OnBroadcastTagMessage(PrivateTagMessageEventArgs e) => this.BroadcastTagMessage?.Invoke(this, e);
	protected internal void OnChannelAction(ChannelMessageEventArgs e) => this.ChannelAction?.Invoke(this, e);
	protected internal void OnChannelBan(ChannelListChangedEventArgs e) => this.ChannelBan?.Invoke(this, e);
	protected internal void OnChannelTimestamp(ChannelTimestampEventArgs e) => this.ChannelTimestamp?.Invoke(this, e);
	protected internal void OnChannelCTCP(ChannelMessageEventArgs e) => this.ChannelCTCP?.Invoke(this, e);
	protected internal void OnChannelExempt(ChannelListChangedEventArgs e) => this.ChannelExempt?.Invoke(this, e);
	protected internal void OnChannelInviteExempt(ChannelListChangedEventArgs e) => this.ChannelInviteExempt?.Invoke(this, e);
	protected internal void OnChannelJoin(ChannelJoinEventArgs e) => this.ChannelJoin?.Invoke(this, e);
	protected internal void OnChannelJoinDenied(ChannelJoinDeniedEventArgs e) => this.ChannelJoinDenied?.Invoke(this, e);
	protected internal void OnChannelKick(ChannelKickEventArgs e) => this.ChannelKick?.Invoke(this, e);
	protected internal void OnChannelLeave(ChannelPartEventArgs e) => this.ChannelLeave?.Invoke(this, e);
	protected internal void OnChannelList(ChannelListEventArgs e) => this.ChannelList?.Invoke(this, e);
	protected internal void OnChannelListChanged(ChannelListChangedEventArgs e) => this.ChannelListChanged?.Invoke(this, e);
	protected internal void OnChannelListEnd(ChannelListEndEventArgs e) => this.ChannelListEnd?.Invoke(this, e);
	protected internal void OnChannelMessage(ChannelMessageEventArgs e) => this.ChannelMessage?.Invoke(this, e);
	protected internal void OnChannelMessageDenied(ChannelJoinDeniedEventArgs e) => this.ChannelMessageDenied?.Invoke(this, e);
	protected internal void OnChannelModesSet(ChannelModesSetEventArgs e) => this.ChannelModesSet?.Invoke(this, e);
	protected internal void OnChannelModesGet(ChannelModesSetEventArgs e) => this.ChannelModesGet?.Invoke(this, e);
	protected internal void OnChannelNotice(ChannelMessageEventArgs e) => this.ChannelNotice?.Invoke(this, e);
	protected internal void OnChannelPart(ChannelPartEventArgs e) => this.ChannelPart?.Invoke(this, e);
	protected internal void OnChannelQuiet(ChannelListChangedEventArgs e) => this.ChannelQuiet?.Invoke(this, e);
	protected internal void OnChannelRemoveExempt(ChannelListChangedEventArgs e) => this.ChannelExemptRemoved?.Invoke(this, e);
	protected internal void OnChannelRemoveInviteExempt(ChannelListChangedEventArgs e) => this.ChannelInviteExemptRemoved?.Invoke(this, e);
	protected internal void OnChannelRemoveKey(ChannelChangeEventArgs e) => this.ChannelKeyRemoved?.Invoke(this, e);
	protected internal void OnChannelRemoveLimit(ChannelChangeEventArgs e) => this.ChannelLimitRemoved?.Invoke(this, e);
	protected internal void OnChannelSetKey(ChannelKeyEventArgs e) => this.ChannelKeySet?.Invoke(this, e);
	protected internal void OnChannelSetLimit(ChannelLimitEventArgs e) => this.ChannelLimitSet?.Invoke(this, e);
	protected internal void OnChannelStatusChanged(ChannelStatusChangedEventArgs e) => this.ChannelStatusChanged?.Invoke(this, e);
	protected internal void OnChannelTagMessage(ChannelTagMessageEventArgs e) => this.ChannelTagMessage?.Invoke(this, e);
	protected internal void OnChannelTopic(ChannelTopicEventArgs e) => this.ChannelTopicReceived?.Invoke(this, e);
	protected internal void OnChannelTopicChange(ChannelTopicChangeEventArgs e) => this.ChannelTopicChanged?.Invoke(this, e);
	protected internal void OnChannelTopicStamp(ChannelTopicStampEventArgs e) => this.ChannelTopicStamp?.Invoke(this, e);
	protected internal void OnChannelUnBan(ChannelListChangedEventArgs e) => this.ChannelBanRemoved?.Invoke(this, e);
	protected internal void OnChannelUnQuiet(ChannelListChangedEventArgs e) => this.ChannelQuietRemoved?.Invoke(this, e);
	protected internal void OnDisconnected(DisconnectEventArgs e) {
		this.disconnectReason = e.Reason;
		this.pingTimer.Stop();

		if (e.Exception is not null) this.OnException(new(e.Exception, true));
		this.Disconnected?.Invoke(this, e);

		if (this.StsPolicy is not null && this.StsPolicy.IsValid && this.SupportedCapabilities.Contains("sts"))
			this.StsPolicy.RefreshExpiry();

		this.State = IrcClientState.Disconnected;
		this.Channels.Clear();
		this.Users.Clear();
		this.UserModes.Clear();
		this.SupportedCapabilities.Clear();
		this.EnabledCapabilities.Clear();
		this.MonitorList.ClearInternal();

		this.stream?.Dispose();
		this.stream = null;
		this.tcpClient?.Dispose();
		this.tcpClient = null;
		this.SslStream?.Dispose();
		this.SslStream = null;

		// Fail async requests.
		this.asyncRequestTimer?.Stop();
		lock (this.asyncRequests) {
			if (this.readAsyncTaskSource != null) this.readAsyncTaskSource.SetException(new AsyncRequestDisconnectedException(e.Reason, e.Exception));
			foreach (var asyncRequest in this.asyncRequests) {
				asyncRequest.OnFailure(new AsyncRequestDisconnectedException(e.Reason, e.Exception));
			}
			this.asyncRequests.Clear();
		}
	}
	protected internal void OnException(ExceptionEventArgs e) => this.Exception?.Invoke(this, e);
	protected internal void OnExemptList(ChannelModeListEventArgs e) => this.ExemptList?.Invoke(this, e);
	protected internal void OnExemptListEnd(ChannelModeListEndEventArgs e) => this.ExemptListEnd?.Invoke(this, e);
	protected internal void OnInvite(InviteEventArgs e) => this.Invite?.Invoke(this, e);
	protected internal void OnInviteSent(InviteSentEventArgs e) => this.InviteSent?.Invoke(this, e);
	protected internal void OnInviteExemptList(ChannelModeListEventArgs e) => this.ChannelInviteExemptList?.Invoke(this, e);
	protected internal void OnInviteExemptListEnd(ChannelModeListEndEventArgs e) => this.ChannelInviteExemptListEnd?.Invoke(this, e);
	protected internal void OnKilled(PrivateMessageEventArgs e) => this.Killed?.Invoke(this, e);
	protected internal void OnMonitorOffline(IrcUserLineEventArgs e) => this.MonitorOffline?.Invoke(this, e);
	protected internal void OnMonitorOnline(IrcUserLineEventArgs e) => this.MonitorOnline?.Invoke(this, e);
	protected internal void OnMotd(MotdEventArgs e) => this.MOTD?.Invoke(this, e);
	protected internal void OnNames(ChannelNamesEventArgs e) => this.Names?.Invoke(this, e);
	protected internal void OnNamesEnd(ChannelModeListEndEventArgs e) => this.NamesEnd?.Invoke(this, e);
	protected internal void OnNicknameChange(NicknameChangeEventArgs e) => this.NicknameChange?.Invoke(this, e);
	protected internal void OnNicknameChangeFailed(NicknameEventArgs e) => this.NicknameChangeFailed?.Invoke(this, e);
	protected internal void OnNicknameInvalid(NicknameEventArgs e) => this.NicknameInvalid?.Invoke(this, e);
	protected internal void OnNicknameTaken(NicknameEventArgs e) => this.NicknameTaken?.Invoke(this, e);
	protected internal void OnPingReceived(PingEventArgs e) => this.PingReceived?.Invoke(this, e);
	protected internal void OnPong(PingEventArgs e) => this.Pong?.Invoke(this, e);
	protected internal void OnPrivateAction(PrivateMessageEventArgs e) => this.PrivateAction?.Invoke(this, e);
	protected internal void OnPrivateCTCP(PrivateMessageEventArgs e) => this.PrivateCTCP?.Invoke(this, e);
	protected internal void OnPrivateMessage(PrivateMessageEventArgs e) => this.PrivateMessage?.Invoke(this, e);
	protected internal void OnPrivateNotice(PrivateMessageEventArgs e) => this.PrivateNotice?.Invoke(this, e);
	protected internal void OnPrivateTagMessage(PrivateTagMessageEventArgs e) => this.PrivateTagMessage?.Invoke(this, e);
	protected internal void OnUserAppeared(IrcUserEventArgs e) => this.UserAppeared?.Invoke(this, e);
	protected internal void OnUserDisappeared(IrcUserEventArgs e) => this.UserDisappeared?.Invoke(this, e);
	protected internal void OnUserQuit(QuitEventArgs e) => this.UserQuit?.Invoke(this, e);
	protected internal void OnRawLineReceived(IrcLineEventArgs e) => this.RawLineReceived?.Invoke(this, e);
	protected internal void OnRawLineUnhandled(IrcLineEventArgs e) => this.RawLineUnhandled?.Invoke(this, e);
	protected internal void OnRawLineSent(RawLineEventArgs e) => this.RawLineSent?.Invoke(this, e);
	protected internal void OnRegistered(RegisteredEventArgs e) => this.Registered?.Invoke(this, e);
	protected internal void OnUserModesGet(UserModesEventArgs e) => this.UserModesGet?.Invoke(this, e);
	protected internal void OnUserModesSet(UserModesEventArgs e) => this.UserModesSet?.Invoke(this, e);
	protected internal void OnWallops(PrivateMessageEventArgs e) => this.Wallops?.Invoke(this, e);
	protected internal void OnSelectCertificate(SelectCertificateEventArgs e) => this.SelectCertificate?.Invoke(this, e);
	protected internal void OnServerError(ServerErrorEventArgs e) => this.ServerError?.Invoke(this, e);
	protected internal void OnStateChanged(StateEventArgs e) {
		if (e.NewState >= IrcClientState.ReceivingServerInfo && e.OldState < IrcClientState.ReceivingServerInfo && !this.Users.Contains(this.Me.Nickname))
			this.Users.Add(this.Me);

		this.StateChanged?.Invoke(this, e);
	}
	protected internal void OnStsPolicyChanged(EventArgs e) => this.StsPolicyChanged?.Invoke(this, e);
	protected internal void OnStsUpgrade(StsUpgradeEventArgs e) => this.StsUpgrade?.Invoke(this, e);
	protected internal void OnValidateCertificate(ValidateCertificateEventArgs e) => this.ValidateCertificate?.Invoke(this, e);
	protected internal void OnWhoIsAuthenticationLine(WhoisAuthenticationEventArgs e) => this.WhoIsAuthenticationLine?.Invoke(this, e);
	protected internal void OnWhoIsChannelLine(WhoisChannelsEventArgs e) => this.WhoIsChannelLine?.Invoke(this, e);
	protected internal void OnWhoIsEnd(WhoisEndEventArgs e) => this.WhoIsEnd?.Invoke(this, e);
	protected internal void OnWhoIsIdleLine(WhoisIdleEventArgs e) => this.WhoIsIdleLine?.Invoke(this, e);
	protected internal void OnWhoIsNameLine(WhoisNameEventArgs e) => this.WhoIsNameLine?.Invoke(this, e);
	protected internal void OnWhoIsOperLine(WhoisOperEventArgs e) => this.WhoIsOperLine?.Invoke(this, e);
	protected internal void OnWhoIsHelperLine(WhoisOperEventArgs e) => this.WhoIsHelperLine?.Invoke(this, e);
	protected internal void OnWhoIsRealHostLine(WhoisRealHostEventArgs e) => this.WhoIsRealHostLine?.Invoke(this, e);
	protected internal void OnWhoIsServerLine(WhoisServerEventArgs e) => this.WhoIsServerLine?.Invoke(this, e);
	protected internal void OnWhoList(WhoListEventArgs e) => this.WhoList?.Invoke(this, e);
	protected internal void OnWhoWasNameLine(WhoisNameEventArgs e) => this.WhoWasNameLine?.Invoke(this, e);
	protected internal void OnWhoWasEnd(WhoisEndEventArgs e) => this.WhoWasEnd?.Invoke(this, e);
	protected internal void OnWhoxList(WhoxListEventArgs e) => this.WhoxList?.Invoke(this, e);
	#endregion

	// Server information
	/// <summary>The common name (address) of the server, to be checked against the the server's TLS certificate if TLS is used.</summary>
	public string? Address { get; set; }
	/// <summary>The password to use when logging in, or null if no password is needed.</summary>
	public string? Password { get; set; }
	/// <summary>The server's self-proclaimed name or address.</summary>
	public string? ServerName { get; protected internal set; }
	/// <summary>The name of the IRC network, if known.</summary>
	public string NetworkName => this.Extensions.NetworkName;

	/// <summary>A list of all user modes the server supports.</summary>
	public ModeSet SupportedUserModes { get; } = new ModeSet();
	/// <summary>A list of all channel modes the server supports.</summary>
	public ChannelModes SupportedChannelModes => this.Extensions.ChanModes;

	/// <summary>A list of all users we can see on the network.</summary>
	public IrcUserCollection Users { get; protected set; }
	/// <summary>A User object representing the local user.</summary>
	public IrcLocalUser Me { get; protected internal set; }

	/// <summary>Returns or sets a value specifying whether SASL authentication should be used or required.</summary>
	public SaslAuthenticationMode SaslAuthenticationMode { get; set; } = SaslAuthenticationMode.UseIfAvailable;
	/// <summary>A list of SASL mechanisms to attempt for authentication, in order.</summary>
	public List<SaslMechanism> SaslMechanisms { get; } = new() { SaslMechanism.ExternalTlsOnly, SaslMechanism.PlainTlsOnly };
	/// <summary>The username to use with SASL authentication mechanisms that require one.</summary>
	public string? SaslUsername { get; set; }
	/// <summary>The password to use with SASL authentication mechanisms that require one.</summary>
	public string? SaslPassword { get; set; }
	internal readonly HashSet<SaslMechanism> saslMechanismsTried = new();
	internal (SaslMechanism mechanism, MemoryStream challengeStream, object? state)? saslSession;

	/// <summary>Provides access to RPL_ISUPPORT extensions supported by the server.</summary>
	public IrcExtensions Extensions { get; protected internal set; }
	/// <summary>Returns the set of IRCv3 capabilities supported by the server.</summary>
	public IrcNamedEntityCollection<IrcCapability> SupportedCapabilities { get; } = new(StringComparer.Ordinal);
	/// <summary>Returns the set of IRCv3 capabilities currently enabled.</summary>
	public IrcNamedEntityCollection<IrcCapability> EnabledCapabilities { get; } = new(StringComparer.Ordinal);

	/// <summary>A <see cref="StringComparer"/> that emulates the comparison the server uses, as specified in the RPL_ISUPPORT message.</summary>
	public IrcStringComparer CaseMappingComparer { get; protected internal set; } = IrcStringComparer.RFC1459;

	/// <summary>The time we last sent a PRIVMSG in UTC.</summary>
	public DateTime? LastSpoke { get; protected internal set; }
	/// <summary>Our current user modes.</summary>
	public ModeSet UserModes { get; protected internal set; } = new ModeSet();
	/// <summary>The list of channels we are on.</summary>
	public IrcChannelCollection Channels => this.Me.Channels;
	/// <summary>The current state of the IRC client.</summary>
	public IrcClientState State {
		get => this.state;
		protected internal set {
			var oldState = this.state;
			this.state = value;
			this.OnStateChanged(new(oldState, value));
		}
	}

	public IrcLine? CurrentLine { get; private set; }

	/// <summary>Contains SHA-256 hashes of TLS certificates that should be accepted.</summary>
	public List<string> TrustedCertificates { get; private set; } = new List<string>();

	/// <summary>Returns or sets a value indicating whether the connection will continue by default if the server's TLS certificate is invalid.</summary>
	/// <remarks>This property can be overridden by handling the <see cref="ValidateCertificate"/> event.</remarks>
	public bool AllowInvalidCertificate { get; set; }

	internal Encoding encoding;
	/// <summary>Returns or sets the text encoding used to encode and decode messages.</summary>
	public Encoding Encoding {
		get => this.encoding;
		set {
			if (this.Extensions.Utf8Only && value is not UTF8Encoding) throw new InvalidOperationException($"Cannot set {nameof(this.Encoding)} when the server uses UTF8ONLY.");
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
			if (!value.Preamble.IsEmpty)
#else
			if (value.GetPreamble().Length > 0)
#endif
				throw new ArgumentException("Preamble must be empty.");
			this.encoding = value;
		}
	}

	/// <summary>If the MONITOR or WATCH command is supported, returns a <see cref="AnIRC.MonitorList"/> instance that can be used to manipulate the monitor list.</summary>
	public MonitorList MonitorList { get; }

	private TlsMode tls = TlsMode.StartTlsOptional;
	/// <summary>Returns or sets a value specifying whether to use TLS or STARTTLS to connect.</summary>
	/// <exception cref="InvalidOperationException">An attempt was made to set this property while the client is connected.</exception>
	public TlsMode Tls {
		get => this.tls;
		set {
			if (this.State >= IrcClientState.TlsHandshaking)
				throw new InvalidOperationException("This property cannot be set while the client is connected.");
			if (value < TlsMode.Tls && this.StsPolicy is not null && this.StsPolicy.IsValid)
				throw new InvalidOperationException("This operation is not allowed while an STS policy is in effect.");
			this.tls = value;
		}
	}
	public bool UseTlsClientCertificate { get; set; }

	/// <summary>Returns or sets the ping timeout, in seconds.</summary>
	public int PingTimeout {
		get => this.pingTimeout;
		set {
			this.pingTimeout = value;
			if (value == 0)
				this.pingTimer.Enabled = false;
			else {
				this.pingTimer.Interval = value * 1000;
				if (this.State >= IrcClientState.Connecting) this.pingTimer.Enabled = true;
			}
		}
	}

	/// <summary>Returns or sets the quit message that will be sent in the event of a ping timeout.</summary>
	public string PingTimeoutMessage { get; set; } = "Ping timeout";

	internal List<AsyncRequest> asyncRequests = new();

	/// <summary>Returns the list of pending async requests for this <see cref="IrcClient"/>.</summary>
	public ReadOnlyCollection<AsyncRequest> AsyncRequests { get; }
	private readonly Timer asyncRequestTimer = new(30e+3) { AutoReset = false };
	private TaskCompletionSource<IrcLine>? readAsyncTaskSource;

	private readonly byte[] buffer = new byte[RECEIVE_BUFFER_SIZE];
	internal Stream? stream;
	private TcpClient? tcpClient;

	private readonly byte[] SendBuffer = new byte[SEND_BUFFER_SIZE];
	private readonly MemoryStream SendBufferStream;
	private readonly StreamWriter utf8StreamWriter;
	private readonly StreamWriter encodingStreamWriter;

	private Thread? readThread;
	private int pingTimeout = 60;
	internal bool pinged;
	internal readonly Timer pingTimer = new(60000);
	private readonly object receiveLock = new();
	private readonly object Lock = new();

	private IrcClientState state;
	/// <summary>Stores <see cref="DisconnectReason"/> values that don't cause the connection to be closed immediately.</summary>
	protected internal DisconnectReason disconnectReason;
	internal bool accountKnown;  // Some servers send both 330 and 307 in WHOIS replies. We need to ignore the 307 in that case.
	internal List<string> pendingCapabilities = new();
	internal Dictionary<string, HashSet<string>> pendingNames = new();
	internal HashSet<string>? pendingMonitor;
	internal TaskStatus startTlsCapCheck;

	protected internal DateTime? testUtcNow;

	public SslStream? SslStream { get; private set; }

	/// <summary>Returns or sets the Strict Transport Security policy in effect for this client.</summary>
	public StsPolicy? StsPolicy { get; set; }

	/// <summary>Contains functions used to handle replies received from the server.</summary>
	protected internal Dictionary<string, IrcMessageHandler> MessageHandlers = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Creates a new IRCClient object with no network name and the default encoding and ping timeout.</summary>
	/// <param name="localUser">An IRCLocalUser instance to represent the local user.</param>
	public IrcClient(IrcLocalUser localUser) : this(localUser, "IRC Network", Utf8Encoding) { }
	/// <summary>Creates a new IRCClient object with no network name and the default encoding and ping timeout.</summary>
	/// <param name="localUser">An IRCLocalUser instance to represent the local user.</param>
	/// <param name="networkName">The name of the IRC network.</param>
	public IrcClient(IrcLocalUser localUser, string networkName) : this(localUser, networkName, Utf8Encoding) { }
	/// <summary>Creates a new IRCClient object with no name and the default encoding and ping timeout.</summary>
	/// <param name="localUser">An IRCLocalUser instance to represent the local user.</param>
	/// <param name="encoding">The encoding to use to send and receive data.</param>
	public IrcClient(IrcLocalUser localUser, Encoding? encoding) : this(localUser, "IRC Network", encoding) { }
	/// <summary>Creates a new <see cref="IrcClient"/> object with no name and the default encoding and ping timeout.</summary>
	/// <param name="localUser">An <see cref="IrcLocalUser"/> instance to represent the local user.</param>
	/// <param name="networkName">The name of the IRC network.</param>
	/// <param name="encoding">The encoding to use to send and receive data.</param>
	public IrcClient(IrcLocalUser localUser, string networkName, Encoding? encoding) {
		if (localUser is null) throw new ArgumentNullException(nameof(localUser));
		if (localUser.client != null && localUser.client != this) throw new ArgumentException($"The {nameof(IrcLocalUser)} object is already bound to another {nameof(IrcClient)}.", nameof(localUser));

		this.pingTimer.Elapsed += this.PingTimer_Elapsed;
		this.asyncRequestTimer.Elapsed += this.AsyncRequestTimer_Elapsed;

		this.Extensions = new IrcExtensions(this, networkName);
		this.Users = new IrcUserCollection(this);
		this.encoding = encoding ?? new UTF8Encoding(false, false);
		this.MonitorList = new MonitorList(this);
		this.AsyncRequests = this.asyncRequests.AsReadOnly();

		this.SendBufferStream = new(this.SendBuffer);
		this.utf8StreamWriter = new(this.SendBufferStream, Utf8Encoding) { AutoFlush = true };
		this.encodingStreamWriter = new(this.SendBufferStream, this.Encoding) { AutoFlush = true };

		this.Me = localUser;
		localUser.client = this;
		localUser.Channels = new IrcChannelCollection(this);

		this.SetDefaultUserModes();

		this.RegisterHandlers(typeof(Handlers));
	}

	protected virtual void Dispose(bool disposing) => this.Disconnect();

	public void Dispose() {
		this.Disconnect();
		GC.SuppressFinalize(this);
	}

	/// <summary>Adds handlers marked by <see cref="IrcMessageHandlerAttribute"/>s from the given type to this <see cref="IrcClient"/>.</summary>
	protected internal void RegisterHandlers(Type type) {
		foreach (var method in type.GetMethods(BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static)) {
			foreach (var attribute in method.GetCustomAttributes<IrcMessageHandlerAttribute>()) {
				this.MessageHandlers.Add(attribute.Reply, (IrcMessageHandler) method.CreateDelegate(typeof(IrcMessageHandler)));
			}
		}
	}

	private void PingTimer_Elapsed(object? sender, ElapsedEventArgs e) {
		lock (this.pingTimer) {
			if (this.startTlsCapCheck == TaskStatus.WaitingForActivation) {
				this.Send(PING, "CAP check");
				this.pinged = true;
				this.startTlsCapCheck = TaskStatus.Running;
			} else if (this.pinged) {
				this.disconnectReason = DisconnectReason.PingTimeout;
				this.Send(QUIT, this.PingTimeoutMessage);
				this.OnDisconnected(new(DisconnectReason.PingTimeout, null));  // Don't wait for acknowledgement here.
			} else {
				this.pinged = true;
				this.Send(PING, "Keep-alive");
			}
		}
	}

	/// <summary>Connects and logs in to an IRC network.</summary>
	public virtual void Connect(string address, int port) {
		this.disconnectReason = 0;
		this.accountKnown = false;

		this.Address ??= address;
		this.State = IrcClientState.Connecting;

		this.pinged = false;
		if (this.pingTimeout != 0) this.pingTimer.Start();

		this.InitialiseTcpClient(address, port);
	}
	/// <summary>Connects and logs in to an IRC network.</summary>
	public virtual void Connect(IPAddress ip, int port) {
		this.disconnectReason = 0;
		this.accountKnown = false;

		this.Address ??= ip.ToString();
		this.State = IrcClientState.Connecting;

		this.pinged = false;
		if (this.pingTimeout != 0) this.pingTimer.Start();

		this.InitialiseTcpClient(ip, port);
	}

	protected virtual void InitialiseTcpClient(string address, int port) {
		this.tcpClient = new() { ReceiveBufferSize = RECEIVE_BUFFER_SIZE, SendBufferSize = SEND_BUFFER_SIZE };
		this.tcpClient.BeginConnect(address, port, this.OnConnected, address);
	}
	protected virtual void InitialiseTcpClient(IPAddress address, int port) {
		this.tcpClient = new() { ReceiveBufferSize = RECEIVE_BUFFER_SIZE, SendBufferSize = SEND_BUFFER_SIZE };
		this.tcpClient.BeginConnect(address, port, this.OnConnected, address.ToString());
	}

	protected virtual Stream GetBaseStream(TcpClient? tcpClient, IAsyncResult? result) {
		if (tcpClient is null || result is null) throw new InvalidOperationException($"Cannot initialise stream when {nameof(InitialiseTcpClient)} has been overridden.");
		tcpClient.EndConnect(result);
		return tcpClient.GetStream();
	}

	/// <summary>Called when the TCP connection attempt has completed.</summary>
	protected virtual void OnConnected(IAsyncResult result) {
		try {
			this.stream = this.GetBaseStream(this.tcpClient, result);
		} catch (Exception ex) {
			this.OnDisconnected(new(DisconnectReason.Exception, ex));
			return;
		}
		if (this.Tls == TlsMode.Tls) {
			try {
				this.TlsHandshake();
			} catch (Exception ex) {
				this.Disconnect(DisconnectReason.TlsAuthenticationFailed, ex);
				return;
			}
		}
		this.State = IrcClientState.Registering;
		this.SetDefaultChannelModes();
		this.SetDefaultUserModes();
		this.StartRead();
		this.PreRegister();
	}

	protected internal virtual void TlsHandshake() {
		if (this.stream is null) throw new InvalidOperationException("Cannot initiate a TLS handshake at this time.");
		if (this.stream is SslStream) throw new InvalidOperationException("A TLS handshake has already been completed.");
		this.State = IrcClientState.TlsHandshaking;
		this.stream = this.SslStream = new SslStream(this.stream, false, this.ValidateCertificateInternal, this.SelectCertificateInternal!);
		this.SslStream.AuthenticateAsClient(this.Address!, null, SslProtocols.None, true);
		this.State = IrcClientState.Registering;
	}

	protected X509Certificate? SelectCertificateInternal(object? sender, string targetHost, X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers) {
		var e = new SelectCertificateEventArgs(targetHost, localCertificates, remoteCertificate, acceptableIssuers);
		this.OnSelectCertificate(e);
		return e.ClientCertificate;
	}

	/// <summary>Decides whether to accept an invalid TLS certificate.</summary>
	/// <param name="certificate">The certificate presented by the server.</param>
	/// <param name="chain">The chain of certificate authorities associated with the server's certificate.</param>
	/// <param name="sslPolicyErrors">A value indicating why the certificate is invalid.</param>
	/// <returns>True if the connection should continue; false if it should be terminated.</returns>
	protected bool ValidateCertificateInternal(object? sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) {
		bool valid = false;
		if (this.AllowInvalidCertificate)
			valid = true;
		// If the certificate is valid, continue.
		else if (sslPolicyErrors == SslPolicyErrors.None)
			valid = true;
		else {
			// If the certificate is trusted, continue.
			if (certificate is not null && this.TrustedCertificates.Count != 0) {
				var sha256Hash = string.Join(null, SHA256.Create().ComputeHash(certificate.GetRawCertData()).Select(b => b.ToString("X2")));
				if (this.TrustedCertificates.Contains(sha256Hash, StringComparer.OrdinalIgnoreCase))
					valid = true;
			}
		}

		// Raise the event.
		var e = new ValidateCertificateEventArgs(certificate, chain, sslPolicyErrors, valid);
		this.OnValidateCertificate(e);
		return e.Valid;
	}

	protected internal virtual void PreRegister() {
		this.startTlsCapCheck = 0;
		this.Send(CAP, "LS", "302");
		if (this.Tls is TlsMode.StartTlsOptional or TlsMode.StartTlsRequired && this.SslStream is null) {
			// If we're using STARTTLS, we don't want to send anything else until the TLS handshake.
			this.startTlsCapCheck = TaskStatus.WaitingForActivation;
			this.pingTimer.Interval = this.PingTimeout > 0 ? Math.Min(3000, this.PingTimeout * 1000) : 3000;
			this.pingTimer.Start();
		} else {
			this.Register();
		}
	}
	protected internal virtual void Register() {
		if (this.Password is not null) this.Send(PASS, this.Password);
		this.Send(NICK, this.Me.Nickname);
		this.Send(USER, this.Me.Ident, "0", "*", this.Me.FullName);
	}

	internal bool HandleUnsupportedStartTls(Exception ex) {
		this.pingTimer.Stop();
		if (this.PingTimeout > 0) this.pingTimer.Interval = this.PingTimeout * 1000;
		this.startTlsCapCheck = 0;
		if (this.Tls == TlsMode.StartTlsRequired) {
			this.Send(QUIT, QUIT_MESSAGE_STARTTLS_NOT_SUPPORTED);
			this.Disconnect(DisconnectReason.TlsAuthenticationFailed, ex);
			return false;
		} else {
			if (this.PingTimeout > 0) this.pingTimer.Start();
			this.Register();
			return true;
		}
	}

	protected internal bool SaslAuthenticate() {
		this.saslMechanismsTried.Clear();
		return this.SaslAuthenticateNext();
	}
	protected internal bool SaslAuthenticateNext() {
		this.SupportedCapabilities.TryGetValue("sasl", out var saslCap);
		var mechanism = this.SaslMechanisms.FirstOrDefault(m => (saslCap?.Parameter is not string s || s.Split(',').Contains(m.Name)) && m.CanAttempt(this) && this.saslMechanismsTried.Add(m));
		if (mechanism is not null) {
			try {
				this.saslSession = (mechanism, new MemoryStream(300), mechanism.Initialise(this));
				this.Send(AUTHENTICATE, mechanism.Name);
				return true;
			} catch (Exception ex) {
				this.OnException(new(ex, false));
				return this.SaslAuthenticateNext();
			}
		}
		this.saslSession = null;
		return false;
	}

	/// <summary>Ungracefully closes the connection to the IRC network.</summary>
	public void Disconnect() => this.Disconnect(DisconnectReason.ClientDisconnected, null);
	protected internal void Disconnect(DisconnectReason reason, Exception? exception) {
		if (this.State == IrcClientState.Disconnected) return;
		this.DisconnectTcpClient();
		this.OnDisconnected(new(reason, exception));
	}
	public virtual void DisconnectTcpClient() {
		if (this.tcpClient is null) return;
		this.tcpClient.Close();
		this.tcpClient = null;
	}

	/// <summary>Starts the specified <see cref="AsyncRequest"/> on this client.</summary>
	public void AddAsyncRequest(AsyncRequest request) {
		lock (this.asyncRequests) {
			this.asyncRequests.Add(request);
			if (request.CanTimeout) {
				this.asyncRequestTimer.Stop();
				this.asyncRequestTimer.Start();
			}
		}
	}

	private void AsyncRequestTimer_Elapsed(object? sender, ElapsedEventArgs e) {
		// Time out async requests.
		lock (this.asyncRequests) {
			foreach (var asyncRequest in this.asyncRequests) {
				if (asyncRequest.CanTimeout) asyncRequest.OnFailure(new TimeoutException());
			}
			this.asyncRequests.Clear();
		}
	}

	protected virtual void StartRead() {
		this.readThread = new Thread(this.ReadLoop) { Name = $"{nameof(IrcClient)} read thread: {this.NetworkName ?? this.Address}" };
		this.readThread.Start();
	}

	/// <summary>Reads and processes messages from the server.</summary>
	protected virtual void ReadLoop() {
		int posLineStart = 0, posReadStart = 0; var tooLong = false;
		try {
			while (this.State >= IrcClientState.Registering && this.stream is not null) {
				if (this.pingTimeout != 0) this.pingTimer.Start();
				var trueReadStart = posReadStart;
				var trueReadLength = this.buffer.Length - posReadStart;
				var n = this.stream.Read(this.buffer, posReadStart, this.buffer.Length - posReadStart);
				if (n == 0) {
					// Server disconnected.
					if (this.State != IrcClientState.Disconnected) {
						if (this.disconnectReason == 0) this.disconnectReason = DisconnectReason.ServerDisconnected;
						this.OnDisconnected(new(this.disconnectReason, null));
					}
					return;
				}
				var posReadEnd = posReadStart + n;

				while (true) {
					if (this.State == IrcClientState.Disconnected) return;
					var posLF = Array.IndexOf(this.buffer, (byte) '\n', posReadStart, posReadEnd - posReadStart);
					if (posLF >= 0) {
						if (tooLong)
							tooLong = false;
						else {
							var posLineEnd = posLF > 0 && this.buffer[posLF - 1] == '\r' ? posLF - 1 : posLF;
							try {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
								this.ReceivedLine(this.buffer.AsSpan(posLineStart..posLineEnd));
#else
								this.ReceivedLine(this.buffer, posLineStart..posLineEnd);
#endif
							} catch (Exception ex) {
								this.OnException(new(ex, false));
							}
						}
						posLineStart = posLF + 1;
						posReadStart = posLineStart;
					} else {
						if (posLineStart > 0) {
							if (posLineStart < posReadEnd) {
								// We've read an incomplete line after one or more complete lines.
								// Copy the start of that line to the start of the buffer and continue reading it.
								Array.Copy(this.buffer, posLineStart, this.buffer, 0, posReadEnd - posLineStart);
								posReadStart = posReadEnd - posLineStart;
							} else {
								posReadStart = 0;
							}
						} else if (posReadEnd >= RECEIVE_BUFFER_SIZE) {
							// Ignore an invalid line that doesn't fit within the buffer.
							tooLong = true;
							posReadStart = 0;
						} else {
							// We've read part of an incomplete line at the start of the buffer, but there is space left.
							posReadStart = posReadEnd;
						}
						break;
					}
				}
				posLineStart = 0;
			}
		} catch (Exception ex) {
			if (this.State != IrcClientState.Disconnected)
				this.OnDisconnected(new(DisconnectReason.Exception, ex));
		}
	}

	/// <summary>The UNIX epoch, used for timestamps on IRC, which is midnight UTC of 1 January 1970.</summary>
	public static DateTime Epoch => new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	/// <summary>Decodes a UNIX timestamp into a DateTime value.</summary>
	/// <param name="unixTime">The UNIX timestamp to decode.</param>
	/// <returns>The DateTime represented by the specified UNIX timestamp.</returns>
	public static DateTime DecodeUnixTime(double unixTime) => Epoch.AddSeconds(unixTime);
	/// <summary>Encodes a DateTime value into a UNIX timestamp.</summary>
	/// <param name="time">The DateTime value to encode.</param>
	/// <returns>The UNIX timestamp representation of the specified DateTime value.</returns>
	public static double EncodeUnixTime(DateTime time) => (time.ToUniversalTime() - Epoch).TotalSeconds;

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
	private void ReceivedLine(ReadOnlySpan<byte> bytes)
		=> this.ReceivedLine(IrcLine.FromBinary(bytes, this.Encoding));
#else
	private void ReceivedLine(byte[] buffer, Range range)
		=> this.ReceivedLine(IrcLine.FromBinary(buffer, range, this.Encoding));
#endif

	public void ReceivedLine(string line) => this.ReceivedLine(IrcLine.Parse(line));

	/// <summary>Handles or simulates a message received from the IRC server.</summary>
	/// <param name="line">The message received or to simulate.</param>
	public virtual void ReceivedLine(IrcLine line) {
		lock (this.receiveLock) {
			if (this.State < IrcClientState.Registering) throw new InvalidOperationException("The client is not connected.");

			this.CurrentLine = line;

			int i; bool matchesAsyncRequests = false;
			lock (this.asyncRequests) {
				for (i = 0; i < this.asyncRequests.Count; ++i) {
					if (this.AsyncRequestCheck(line, this.asyncRequests[i])) {
						matchesAsyncRequests = true;
						break;
					}
				}
			}

			var e = new IrcLineEventArgs(line, matchesAsyncRequests);
			this.OnRawLineReceived(e);
			if (this.readAsyncTaskSource != null) this.readAsyncTaskSource.SetResult(line);
			if (this.State != IrcClientState.Disconnected) {
				if (this.MessageHandlers.TryGetValue(line.Message, out var handler))
					handler?.Invoke(this, line, matchesAsyncRequests);
				else
					this.OnRawLineUnhandled(e);
			}

			if (matchesAsyncRequests) {
				lock (this.asyncRequests) {
					var skipTypes = new HashSet<Type>();
					for (; i < this.asyncRequests.Count; ++i) {
						var asyncRequest = this.asyncRequests[i];
						if (!skipTypes.Contains(asyncRequest.GetType()) && this.AsyncRequestCheck(line, asyncRequest, out bool final)) {
							var result = asyncRequest.OnReply(line, ref final);
							if (result) skipTypes.Add(asyncRequest.GetType());

							if (final) {
								this.asyncRequests.RemoveAt(i);
								--i;
								if (this.asyncRequests.Count == 0)
									this.asyncRequestTimer.Stop();
							}
						}
					}
				}
			}

			this.CurrentLine = null;
		}
	}

	private bool AsyncRequestCheck(IrcLine line, AsyncRequest asyncRequest) => this.AsyncRequestCheck(line, asyncRequest, out bool _);
	private bool AsyncRequestCheck(IrcLine line, AsyncRequest asyncRequest, out bool final) {
		if (asyncRequest.Replies.TryGetValue(line.Message, out final)) {
			if (asyncRequest.Parameters == null) return true;
			for (int i = asyncRequest.Parameters.Count - 1; i >= 0; --i) {
				if (asyncRequest.Parameters[i] != null && !this.CaseMappingComparer.Equals(asyncRequest.Parameters[i]!, line.Parameters[i]))
					return false;
			}
			return true;
		}
		return false;
	}

	public virtual void Send(IrcLine line) {
		lock (this.Lock) {
			if (this.stream is null || this.State < IrcClientState.Registering)
				throw new InvalidOperationException("The client is not connected.");

			this.SendBufferStream.Position = 0;
			if (line.Tags.Count > 0) {
				var anyTags = false;
				foreach (var tag in line.Tags) {
					if (anyTags)
						this.SendBufferStream.WriteByte((byte) ';');
					else {
						anyTags = true;
						this.SendBufferStream.WriteByte((byte) '@');
					}
					this.utf8StreamWriter.Write(tag.Key);
					if (!string.IsNullOrEmpty(tag.Value)) {
						this.SendBufferStream.WriteByte((byte) '=');
						this.utf8StreamWriter.Write(IrcLine.EscapeTag(tag.Value));
					}
				}
				this.SendBufferStream.WriteByte((byte) ' ');
			}
			// Source will be skipped because server-bound messages should not have it.
			this.utf8StreamWriter.Write(line.Message);
			foreach (var parameter in line.Parameters) {
				this.SendBufferStream.WriteByte((byte) ' ');
				if (parameter == "" || parameter[0] == ':' || parameter.Contains(' '))
					this.SendBufferStream.WriteByte((byte) ':');
				this.encodingStreamWriter.Write(parameter);
			}
			this.SendBufferStream.WriteByte((byte) '\r');
			this.SendBufferStream.WriteByte((byte) '\n');

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
			var e = new RawLineEventArgs(line, new(this.SendBuffer, 0, (int) this.SendBufferStream.Position));
#else
			var e = new RawLineEventArgs(line);
#endif
			this.OnRawLineSent(e);

			this.stream.Write(this.SendBuffer, 0, (int) this.SendBufferStream.Position);
			this.stream.Flush();

			if (this.disconnectReason == 0 && QUIT.Equals(line.Message, StringComparison.OrdinalIgnoreCase))
				this.disconnectReason = DisconnectReason.Quit;
			else if (PRIVMSG.Equals(line.Message, StringComparison.OrdinalIgnoreCase))
				this.LastSpoke = this.testUtcNow ?? DateTime.UtcNow;
		}
	}

	public void Send(string command, params string[]? parameters) => this.Send(new IrcLine(command, parameters));
	public void Send(string command, IEnumerable<string>? parameters) => this.Send(new IrcLine(command, parameters));
	public void Send(IEnumerable<KeyValuePair<string, string>>? tags, string command, params string[]? parameters) => this.Send(new IrcLine(tags, command, parameters));
	public void Send(IEnumerable<KeyValuePair<string, string>>? tags, string command, IEnumerable<string>? parameters) => this.Send(new IrcLine(tags, command, parameters));
	public void Send(string line) => this.Send(IrcLine.Parse(line));

	/// <summary>Removes mIRC formatting codes from a string.</summary>
	/// <param name="message">The string to strip.</param>
	/// <returns>A copy of the string with mIRC formatting codes removed.</returns>
	public static string RemoveCodes(string message) => RemoveCodesRegex.Replace(message, "");

	/// <summary>Sets the <see cref="IrcExtensions.ChanModes"/> property of <see cref="Extensions"/> to the default value.</summary>
	protected virtual void SetDefaultChannelModes() => this.Extensions.ChanModes = ChannelModes.RFC1459;

	/// <summary>Sets the contents of <see cref="SupportedUserModes"/> to the defaults.</summary>
	protected virtual void SetDefaultUserModes() {
		this.SupportedUserModes.Clear();
		this.SupportedUserModes.Add('i');
		this.SupportedUserModes.Add('o');
		this.SupportedUserModes.Add('s');
		this.SupportedUserModes.Add('w');
	}

	/// <summary>Handles a channel mode message.</summary>
	/// <param name="sender">If this is a mode change, the user who made the change.</param>
	/// <param name="channel">The channel whose modes are affected.</param>
	/// <param name="modes">The list of mode characters in the message.</param>
	/// <param name="parameters">The list of parameters in the message.</param>
	/// <param name="modeMessage">True if this is a mode change; false if this is a notification of current modes.</param>
	protected internal void HandleChannelModes(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, string modes, IEnumerable<string> parameters, bool modeMessage) {
		var enumerator = parameters.GetEnumerator();
		bool direction = true;
		string? parameter;

		var changes = new List<ModeChange>();
		HashSet<char>? oldModes = null;
		if (!modeMessage) oldModes = new HashSet<char>(channel.Modes);

		foreach (char c in modes) {
			if (c == '+')
				direction = true;
			else if (c == '-')
				direction = false;
			else {
				string? oldParameter = null;

				switch (this.Extensions.ChanModes.ModeType(c)) {
					case 'S':
						parameter = enumerator.MoveNext() ? enumerator.Current : "";
						this.HandleChannelModeStatus(line, matchesAsyncRequests, sender, channel, direction, c, parameter);
						break;
					case 'A':
						parameter = enumerator.MoveNext() ? enumerator.Current : "";
						this.HandleChannelModeList(line, matchesAsyncRequests, sender, channel, direction, c, parameter);
						break;
					case 'D':
						parameter = null;
						HandleChannelMode(channel, direction, c, null);
						break;
					case 'B':
					case 'C':
						parameter = direction ? (enumerator.MoveNext() ? enumerator.Current : "") : null;
						if (direction && !modeMessage && channel.Modes.Contains(c)) oldParameter = channel.Modes.GetParameter(c);
						HandleChannelMode(channel, direction, c, parameter);
						break;
					default:
						parameter = null;
						HandleChannelMode(channel, direction, c, null);
						break;
				}

				if (direction) {
					if (oldModes is null || !oldModes.Remove(c)) {
						// A mode is set.
						changes.Add(new ModeChange(direction, c, parameter));
					} else if (oldParameter is not null && parameter != oldParameter) {
						// The parameter has changed.
						changes.Add(new ModeChange(direction, c, parameter));
						if (parameter is not null) channel.Modes.SetParameter(c, parameter);
					}
				} else if (modeMessage)
					changes.Add(new ModeChange(direction, c, parameter));
			}
		}

		// Check for modes missing from RPL_CHANNELMODEIS.
		if (oldModes is not null)
			foreach (char c in oldModes)
				changes.Add(new ModeChange(false, c));

		if (modeMessage) this.OnChannelModesSet(new(line, matchesAsyncRequests, sender, channel, changes));
		else this.OnChannelModesGet(new(line, matchesAsyncRequests, sender, channel, changes));
	}

	/// <summary>Handles a list mode change.</summary>
	internal protected void HandleChannelModeList(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, bool direction, char mode, string parameter)
		=> this.OnChannelListChanged(new(line, matchesAsyncRequests, sender, channel, new(direction, mode, parameter)));
	/// <summary>Handles a status mode change.</summary>
	internal protected void HandleChannelModeStatus(IrcLine line, bool matchesAsyncRequests, IrcUser sender, IrcChannel channel, bool direction, char mode, string parameter) {
		if (channel != null && channel.Users.TryGetValue(parameter, out var user)) {
			if (direction) {
				if (user.Status.Add(mode))
					this.OnChannelStatusChanged(new(line, matchesAsyncRequests, sender, channel, user, new(direction, mode, parameter)));
			} else {
				if (user.Status.Remove(mode))
					this.OnChannelStatusChanged(new(line, matchesAsyncRequests, sender, channel, user, new(direction, mode, parameter)));
			}
		}
	}
	private static void HandleChannelMode(IrcChannel channel, bool direction, char mode, string? parameter) {
		if (direction) {
			if (parameter != null) channel.Modes.Add(mode, parameter);
			else channel.Modes.Add(mode);
		} else channel.Modes.Remove(mode);
	}

	private void NotifyChannelModes(IrcLine line, bool matchesAsyncRequests, ChannelModesSetEventArgs e) {
		foreach (var change in e.Modes) {
			switch (this.Extensions.ChanModes.ModeType(change.Mode)) {
				case 'A':
					var e2 = new ChannelListChangedEventArgs(line, matchesAsyncRequests, e.Sender, e.Channel, change);
					if (change.Mode == 'b') {
						if (change.Direction) this.OnChannelBan(e2);
						else this.OnChannelUnBan(e2);
					} else if (change.Mode == 'q') {
						if (change.Direction) this.OnChannelQuiet(e2);
						else this.OnChannelUnQuiet(e2);
					} else if (change.Mode == this.Extensions.BanExceptionsMode) {
						if (change.Direction) this.OnChannelExempt(e2);
						else this.OnChannelRemoveExempt(e2);
					} else if (change.Mode == this.Extensions.InviteExceptionsMode) {
						if (change.Direction) this.OnChannelInviteExempt(e2);
						else this.OnChannelRemoveInviteExempt(e2);
					} else
						this.OnChannelListChanged(e2);
					break;
				case 'B':
					if (change.Mode == 'k') {
						if (change.Direction) this.OnChannelSetKey(new(line, matchesAsyncRequests, e.Sender, e.Channel, change.Parameter));
						else this.OnChannelRemoveKey(new(line, matchesAsyncRequests, e.Sender, e.Channel));
					}
					break;
				case 'C':
					if (change.Mode == 'l') {
						if (change.Direction) this.OnChannelSetLimit(new(line, matchesAsyncRequests, e.Sender, e.Channel, int.Parse(change.Parameter!)));
						else this.OnChannelRemoveLimit(new(line, matchesAsyncRequests, e.Sender, e.Channel));
					}
					break;
				case 'S':
					if (!e.Channel.Users.TryGetValue(change.Parameter!, out var user)) user = new IrcChannelUser(this, e.Channel, change.Parameter!);
					this.OnChannelStatusChanged(new(line, matchesAsyncRequests, e.Sender, e.Channel, user, change));
					break;
			}
		}
	}

	internal IrcUser[]? RemoveUserFromChannel(IrcChannel channel, IrcUser user) {
		channel.Users.Remove(user.Nickname);

		user.Channels.Remove(channel);
		if (user == this.Me) {
			var disappearedUsers = new List<IrcUser>();
			foreach (var channelUser in channel.Users) {
				channelUser.User.Channels.Remove(channel);
				if (!channelUser.User.IsSeen) disappearedUsers.Add(channelUser.User);
			}
			channel.Users.Clear();
			return disappearedUsers.ToArray();
		} else {
			return user.IsSeen ? null : new[] { user };
		}
	}

	internal void SetCaseMappingComparer() {
		this.CaseMappingComparer = this.Extensions.CaseMapping switch {
			"ascii" => new IrcStringComparer(CaseMappingMode.ASCII),
			"strict-rfc1459" => new IrcStringComparer(CaseMappingMode.StrictRFC1459),
			_ => new IrcStringComparer(CaseMappingMode.RFC1459),
		};

		// We need to rebuild the hash tables after setting this.
		try {
			foreach (var channel in this.Channels) {
				channel.Users.UpdateCaseMapping();
			}
			this.Channels.UpdateCaseMapping();
			foreach (var user in this.Users) {
				user.Channels.UpdateCaseMapping();
			}
			this.Users.UpdateCaseMapping();
			this.MonitorList.UpdateCaseMapping();
		} catch (InvalidOperationException) {
			// This exception occurs when the CASEMAPPING change causes a key collision in existing hash table entries.
			// Servers should resolve this before sending RPL_ISUPPORT; otherwise we cannot be sure how the server is resolving the situation.
			// We'll be safe and treat this as a fatal connection error.
			this.disconnectReason = DisconnectReason.CaseMappingCollision;
			this.Send(QUIT, QUIT_MESSAGE_CASEMAPPING_COLLISION);
			this.Disconnect();
		}
	}

	/// <summary>Searches the users on a channel for those matching a specified hostmask.</summary>
	/// <param name="channel">The channel to search.</param>
	/// <param name="hostmask">The hostmask to search for.</param>
	/// <returns>A list of <see cref="IrcChannelUser"/> objects representing the matching users.</returns>
	public IEnumerable<IrcChannelUser> FindMatchingUsers(string channel, string hostmask)
		=> this.Channels[channel].Users.Matching(hostmask);

	private static readonly HashSet<char> breakingCharacters = new() {
		'\t', ' ', '\u1680', '\u180E', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005',
		'\u2006', '\u2008', '\u2009', '\u200A', '\u200B', '\u200C', '\u200D', '\u205F', '\u3000'
	};

	/// <summary>Splits a message that is too long to fit in one line into multiple lines, using this <see cref="IrcClient"/>'s encoding.</summary>
	/// <param name="message">The message to split.</param>
	/// <param name="maxLength">The maximum size, in bytes, of each part.</param>
	/// <returns>
	/// An enumerable that yields substrings of the message that fit within the specified limit.
	/// If the message is already small enough to fit into one line, only <paramref name="message"/> itself is yielded.
	/// </returns>
	public IEnumerable<string> SplitMessage(string message, int maxLength) => SplitMessage(message, maxLength, this.Encoding);
	/// <summary>Splits a message that is too long to fit in one line into multiple lines using the specified encoding.</summary>
	/// <param name="message">The message to split.</param>
	/// <param name="maxLength">The maximum size, in bytes, of each part.</param>
	/// <param name="encoding">The encoding to use to calculate lengths.</param>
	/// <returns>
	/// An enumerable that yields substrings of the message that fit within the specified limit.
	/// If the message is already small enough to fit into one line, only <paramref name="message"/> itself is yielded.
	/// </returns>
	public static IEnumerable<string> SplitMessage(string message, int maxLength, Encoding encoding) {
		if (message == null) throw new ArgumentNullException(nameof(message));
		if (encoding == null) throw new ArgumentNullException(nameof(encoding));
		if (maxLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxLength), nameof(maxLength) + " must be positive.");

		if (encoding.GetByteCount(message) <= maxLength) {
			yield return message;
			yield break;
		}

		int messageStart = 0, pos, pos2 = 0;
		while (messageStart < message.Length) {
			string? part = null;
			pos = messageStart + 1;
			do {
				// Find the next breaking character.
				for (; pos < message.Length; ++pos) {
					if (breakingCharacters.Contains(message[pos])) break;
				}

				string part2 = message[messageStart..pos];

				// Skip repeated breaking characters.
				for (++pos; pos < message.Length; ++pos) {
					if (!breakingCharacters.Contains(message[pos])) break;
				}

				// Are we over the limit?
				if (encoding.GetByteCount(part2) > maxLength) {
					if (part == null) {
						// If a single word exceeds the limit, we must break it up.
						for (pos = messageStart + 1; pos < message.Length; ++pos) {
							part2 = message[messageStart..pos];
							if (encoding.GetByteCount(part2) > maxLength) break;
							part = part2;
						}
						if (part == null) throw new InvalidOperationException("Can't even fit a single character in a message?!");
						pos2 = pos - 1;
					}
					break;
				}

				// No.
				part = part2;
				pos2 = pos;
			} while (pos < message.Length);

			yield return part;
			messageStart = pos2;
		}
	}

	/// <summary>Determines whether the specified string is a valid channel name.</summary>
	/// <param name="target">The string to check.</param>
	/// <returns>True if the specified string is a valid channel name; false if it is not.</returns>
	public bool IsChannel(string target) => target != null && target != "" && this.Extensions.ChannelTypes.Contains(target[0]);
	/// <summary>Determines whether the specified string is a valid channel name with optional status prefixes.</summary>
	/// <param name="target">The target string to check.</param>
	/// <param name="status">Returns a <see cref="ChannelStatus"/> representing the status prefixes, or <see cref="ChannelStatus.Empty"/> if there is no prefix.</param>
	/// <returns>True if the specified string is a valid channel name; false if it is not.</returns>
	public bool IsChannelWithStatusPrefix(string target, out string channelName, out ChannelStatus status) {
		channelName = target;
		status = ChannelStatus.Empty;
		if (string.IsNullOrEmpty(target)) return false;
		if (this.Extensions.ChannelTypes.Contains(target[0])) return true;
		if (this.Extensions.StatusPrefix.ContainsKey(target[0])) {
			for (var i = 1; i < target.Length; i++) {
				if (this.Extensions.ChannelTypes.Contains(target[i])) {
					channelName = target[i..];
					status = new(this, target.Take(i).Select(c => this.Extensions.StatusPrefix[c]));
					return true;
				} else if (!this.Extensions.StatusPrefix.ContainsKey(target[i]))
					break;
			}
		}
		return false;
	}

#region Async methods
	/// <summary>Waits for the next line from the server.</summary>
	public Task<IrcLine> ReadAsync() {
		if (this.readAsyncTaskSource == null)
			this.readAsyncTaskSource = new TaskCompletionSource<IrcLine>();
		return this.readAsyncTaskSource.Task;
	}

	/// <summary>Sends a PING message to the server and measures the ping time.</summary>
	public async Task<TimeSpan> PingAsync() {
		var request = new AsyncRequest.VoidAsyncRequest(this, null, PONG, null);
		this.AddAsyncRequest(request);

		var stopwatch = Stopwatch.StartNew();
		this.Send(PING, this.ServerName ?? this.Address ?? "*");
		await request.Task;
		return stopwatch.Elapsed;
	}

	/// <summary>Attempts to oper up the local user. The returned Task object completes only if the command is accepted.</summary>
	public Task OperAsync(string name, string password) {
		if (this.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to oper up.");

		var request = new AsyncRequest.VoidAsyncRequest(this, this.Me.Nickname, RPL_YOUREOPER, null, ERR_NEEDMOREPARAMS, ERR_NOOPERHOST, ERR_PASSWDMISMATCH);
		this.AddAsyncRequest(request);
		this.Send(OPER, name, password);
		return request.Task;
	}

	/// <summary>Attempts to join the specified channel. The returned Task object completes only if the join is successful.</summary>
	public Task JoinAsync(string channel) => this.JoinAsync(channel, null);
	/// <summary>Attempts to join the specified channel. The returned Task object completes only if the join is successful.</summary>
	public Task JoinAsync(string channel, string? key) {
		if (channel == null) throw new ArgumentNullException(nameof(channel));
		if (this.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to join channels.");

		var request = new AsyncRequest.VoidAsyncRequest(this, this.Me.Nickname, JOIN, new[] { channel }, ERR_NEEDMOREPARAMS, ERR_BANNEDFROMCHAN, ERR_INVITEONLYCHAN, ERR_BADCHANNELKEY, ERR_CHANNELISFULL, ERR_BADCHANMASK, ERR_NOSUCHCHANNEL, ERR_TOOMANYCHANNELS, ERR_TOOMANYTARGETS, ERR_UNAVAILRESOURCE);
		this.AddAsyncRequest(request);

		if (key != null) this.Send(JOIN, channel, key);
		else this.Send(JOIN, channel);

		return request.Task;
	}

	/// <summary>Performs a WHO request.</summary>
	public Task<ReadOnlyCollection<WhoResponse>> WhoAsync(string query) {
		if (this.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to perform a WHO request.");

		var request = new AsyncRequest.WhoAsyncRequest(this, query);
		this.AddAsyncRequest(request);
		this.Send(WHO, query);
		return (Task<ReadOnlyCollection<WhoResponse>>) request.Task;
	}

	/// <summary>Performs a WHOX request if supported.</summary>
	public Task<ReadOnlyCollection<WhoxResponse>> WhoxAsync(string query, string queryType, params WhoxField[] fields)
		=> this.WhoxAsync(query, queryType, (IEnumerable<WhoxField>) fields);
	/// <summary>Performs a WHOX request if supported.</summary>
	public Task<ReadOnlyCollection<WhoxResponse>> WhoxAsync(string query, string queryType, IEnumerable<WhoxField> fields) {
		if (this.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to perform a WHO request.");
		if (!this.Extensions.SupportsWhox) throw new NotSupportedException("The server does not support the WHOX extension.");

		var sortedFields = new SortedSet<WhoxField>(fields.Select(v => v is > 0 and <= WhoxField.FullName
				? v
				: throw new ArgumentException(nameof(fields) + " contains invalid values.", nameof(fields)))).ToArray();

		if (sortedFields.Length == 0) throw new ArgumentException("Cannot request no fields with WHOX.", nameof(fields));
		if (sortedFields[0] == WhoxField.QueryType && queryType == null)
			throw new ArgumentException("A query type string must be provided with a WHOX request that includes the query type.", nameof(queryType));

		var request = new AsyncRequest.WhoxAsyncRequest(this, query, queryType, sortedFields);
		this.AddAsyncRequest(request);

		var requestBuilder = new StringBuilder();
		requestBuilder.Append($"{WHO} ");
		requestBuilder.Append(query);
		requestBuilder.Append(" %");
		foreach (var t in sortedFields) {
			switch (t) {
				case WhoxField.QueryType: requestBuilder.Append('t'); break;
				case WhoxField.Channel: requestBuilder.Append('c'); break;
				case WhoxField.Ident: requestBuilder.Append('u'); break;
				case WhoxField.IPAddress: requestBuilder.Append('i'); break;
				case WhoxField.Host: requestBuilder.Append('h'); break;
				case WhoxField.ServerName: requestBuilder.Append('s'); break;
				case WhoxField.Nickname: requestBuilder.Append('n'); break;
				case WhoxField.Flags: requestBuilder.Append('f'); break;
				case WhoxField.HopCount: requestBuilder.Append('d'); break;
				case WhoxField.IdleTime: requestBuilder.Append('l'); break;
				case WhoxField.Account: requestBuilder.Append('a'); break;
				case WhoxField.FullName: requestBuilder.Append('r'); break;
			}
		}
		if (queryType != null) {
			requestBuilder.Append(',');
			requestBuilder.Append(queryType);
		}
		this.Send(requestBuilder.ToString());

		return (Task<ReadOnlyCollection<WhoxResponse>>) request.Task;
	}

	/// <summary>Performs a WHOIS request on a nickname.</summary>
	/// <param name="nickname">The nickname to check.</param>
	/// <returns>A <see cref="Task"/> representing the status of the request. The <see cref="Task{TResult}.Result"/> represents the response to the request.</returns>
	public Task<WhoisResponse> WhoisAsync(string nickname) => this.WhoisAsync(null, nickname);
	/// <summary>Performs a WHOIS request on a nickname.</summary>
	/// <param name="nickname">The nickname to check.</param>
	/// <param name="requestIdleTime">If true, the request will be addressed to the server that the target user is on.</param>
	/// <returns>A <see cref="Task"/> representing the status of the request. The <see cref="Task{TResult}.Result"/> represents the response to the request.</returns>
	public Task<WhoisResponse> WhoisAsync(string nickname, bool requestIdleTime) => this.WhoisAsync(requestIdleTime ? nickname : null, nickname);
	/// <summary>Performs a WHOIS request on a nickname.</summary>
	/// <param name="server">May be a server name to address that server, a nickname to address the server they are on, or null to address the server we are on.</param>
	/// <param name="nickname">The nickname to check.</param>
	/// <returns>A <see cref="Task"/> representing the status of the request. The <see cref="Task{TResult}.Result"/> represents the response to the request.</returns>
	public Task<WhoisResponse> WhoisAsync(string? server, string nickname) {
		if (nickname == null) throw new ArgumentNullException(nameof(nickname));
		if (this.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to perform a WHOIS request.");

		var request = this.AsyncRequests.FirstOrDefault(r => r is AsyncRequest.WhoisAsyncRequest r1 && this.CaseMappingComparer.Equals(r1.Target, nickname));
		if (request == null) {
			request = new AsyncRequest.WhoisAsyncRequest(this, nickname);
			this.AddAsyncRequest(request);
			if (server is not null) this.Send(WHOIS, server, nickname);
			else this.Send(WHOIS, nickname);
		}
		return (Task<WhoisResponse>) request.Task;
	}
	#endregion
}
