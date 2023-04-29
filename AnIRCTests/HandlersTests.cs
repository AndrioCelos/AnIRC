using NUnit.Framework;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading.Tasks;

using static AnIRC.Handlers;
using static AnIRC.Replies;

namespace AnIRC.Tests;

[TestFixture]
public class HandlersTests {
	// The tests in this class must directly call the handler that is being tested.
	// This ensures that Visual Studio will associate the tests with the correct method.

	[Test]
	public void WelcomeWithNicknameChange() {
		// Some servers (ZNC, Twitch) assign the client a nickname regardless of the one they specified.
		// This should raise a NicknameChange event at RPL_WELCOME.
		using var client = new TestIrcClient(autoConnect: true);
		client.NicknameChange += client.HandleEvent<NicknameChangeEventArgs>(nameof(client.NicknameChange));
		HandleWelcome(client, new(RPL_WELCOME, "Alice1"), false);
		Assert.AreEqual(IrcClientState.ReceivingServerInfo, client.State);
		var e = client.AssertEvent<NicknameChangeEventArgs>(nameof(client.NicknameChange));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreEqual("Alice1", e.NewNickname);
		Assert.AreEqual("Alice1", client.Me.Nickname);
	}
	[Test]
	public void WelcomeWithSaslFailure() {
		// Should abort if SASL authentication is required but CAP negotiation did not occur.
		using var client = new TestIrcClient(autoConnect: true) { SaslAuthenticationMode = SaslAuthenticationMode.Required };
		HandleWelcome(client, new(RPL_WELCOME, "Alice"), false);
		client.AssertLine($"{QUIT} :{IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_NOT_SUPPORTED}");
		Assert.AreEqual(DisconnectReason.SaslAuthenticationFailed, client.disconnectReason);
	}

	[Test]
	public void MyInfo() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleMyInfo(client, new(RPL_MYINFO, "Alice", "test.irc.example.com", "1.0", "Oaiorsw", "Iabehiklmnopqrstv"), false);
		Assert.AreEqual("test.irc.example.com", client.ServerName);
		Assert.AreEqual("Oaiorsw", string.Join("", client.SupportedUserModes.OrderBy(c => c)));
		// Channel modes not in RFC 2812 (h) are assumed to be type D until specified otherwise.
		Assert.AreEqual("Ibe,k,l,ahimnpqrst,ov", client.SupportedChannelModes.ToString());
	}

	[Test]
	public void ISupport() {
		// This test only covers parsing of the RPL_ISUPPORT line.
		// Behaviour of individual ISUPPORT tokens is covered by IrcExtensionsTests.
		using var client = new TestIrcClient(autoConnect: true);
		HandleISupport(client, new(RPL_ISUPPORT, "Alice", "TEST1=FOO", "TEST2", "TEST3=", "-TEST4", "are supported by this server"), false);
		Assert.AreEqual("TEST1:FOO,TEST2:,TEST3:", Extensions.JoinDictionary(client.Extensions));
	}

	[Test]
	public void ISupportRemoval() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleISupport(client, new(RPL_ISUPPORT, "Alice", "TEST1=FOO", "TEST2", "are supported by this server"), false);
		Assert.AreEqual("TEST1:FOO,TEST2:", Extensions.JoinDictionary(client.Extensions));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "-TEST1", "are supported by this server"));
		Assert.AreEqual("TEST2:", Extensions.JoinDictionary(client.Extensions));
	}

	[Test]
	public void ISupportWithEscapedValue() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleISupport(client, new(RPL_ISUPPORT, "Alice", @"TEST1=A\x20=\x3D\x5CF", "are supported by this server"), false);
		Assert.AreEqual(@"TEST1:A ==\F", Extensions.JoinDictionary(client.Extensions));
	}

	[Test]
	public void ISupportWithNetworkName() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleISupport(client, new(RPL_ISUPPORT, "Alice", @"NETWORK=Test\x20Network", "are supported by this server"), false);
		Assert.AreEqual("Test Network", client.NetworkName);
	}

	[Test]
	public void UserMode() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleUserMode(client, new(RPL_UMODEIS, "Alice", "+iw"), false);
		Assert.AreEqual("iw", string.Join("", client.UserModes.OrderBy(c => c)));
	}

	[Test]
	public void UserModeIsOverwrite() {
		// RPL_UMODEIS contains the complete set of modes; it is not additive. (It should not contain -)
		using var client = new TestIrcClient(autoConnect: true);
		HandleUserMode(client, new(RPL_UMODEIS, "Alice", "+iw"), false);
		HandleUserMode(client, new(RPL_UMODEIS, "Alice", "+o"), false);
		Assert.AreEqual("o", string.Join("", client.UserModes.OrderBy(c => c)));
	}

	[Test]
	public void AwayEvent() {
		const string AWAY_MESSAGE = "Auto away";
		using var client = new TestIrcClient(autoConnect: true);
		client.AwayMessage += client.HandleEvent<AwayMessageEventArgs>(nameof(client.AwayMessage));
		HandleAway(client, new(RPL_AWAY, "Alice", "Bob", AWAY_MESSAGE), false);
		var e = client.AssertEvent<AwayMessageEventArgs>(nameof(client.AwayMessage));
		Assert.AreEqual(AWAY_MESSAGE, e.Reason);
	}

	[Test]
	public void AwaySetsProperties() {
		const string AWAY_MESSAGE = "Auto away";
		using var client = new TestIrcClient(autoConnect: true) { testUtcNow = new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc) };
		client.GetTestUser(out var bob);
		HandleAway(client, new(RPL_AWAY, "Alice", "Bob", AWAY_MESSAGE), false);
		Assert.IsTrue(bob.Away);
		Assert.AreEqual(AWAY_MESSAGE, bob.AwayReason);
		Assert.AreEqual(new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc), bob.AwaySince);
	}

	[Test]
	public void NowAway() {
		using var client = new TestIrcClient(autoConnect: true) { testUtcNow = new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc) };
		client.AwaySet += client.HandleEvent(nameof(client.AwaySet));
		HandleNowAway(client, new(RPL_NOWAWAY, "Alice"), false);
		client.AssertEvent(nameof(client.AwaySet));
		Assert.IsTrue(client.Me.Away);
		Assert.AreEqual(IrcClient.UNKNOWN_AWAY_MESSAGE, client.Me.AwayReason);
		Assert.AreEqual(new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc), client.Me.AwaySince);
	}

	[Test]
	public void UnAway() {
		using var client = new TestIrcClient(autoConnect: true);
		client.AwayCancelled += client.HandleEvent(nameof(client.AwayCancelled));
		client.ReceivedLine(new IrcLine(RPL_NOWAWAY, "Alice"));
		if (!client.Me.Away) throw new InconclusiveException($"{nameof(RPL_NOWAWAY)} failure");

		HandleUnAway(client, new(RPL_UNAWAY, "Alice"), false);
		client.AssertEvent(nameof(client.AwayCancelled));
		Assert.IsFalse(client.Me.Away);
		Assert.IsNull(client.Me.AwayReason);
		Assert.IsNull(client.Me.AwaySince);
	}

	[Test]
	public void WhoisUser() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestUser(out var bob);

		HandleWhoisName(client, new(RPL_WHOISUSER, "Alice", "Bob", "bob", "192.168.0.2", "*", "\u00031\u000FBob"), false);
		Assert.AreEqual("bob", bob.Ident);
		Assert.AreEqual("192.168.0.2", bob.Host);
		Assert.AreEqual("\u00031\u000FBob", bob.FullName);
		Assert.AreEqual(Gender.Male, bob.Gender);
	}

	[Test]
	public void WhoisRegNick() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestUser(out var bob);

		HandleWhoisRegNick(client, new(RPL_WHOISREGNICK, "Alice", "Bob"), false);
		Assert.AreEqual("Bob", bob.Account);
	}

	[Test]
	public void WhoisAccountBeforeRegNick() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestUser(out var bob);

		HandleWhoisAccount(client, new(RPL_WHOISACCOUNT, "Alice", "Bob", "BobX"), false);
		Assert.AreEqual("BobX", bob.Account);
		HandleWhoisRegNick(client, new(RPL_WHOISREGNICK, "Alice", "Bob"), false);
		Assert.AreEqual("BobX", bob.Account);
	}

	[Test]
	public void WhoisAccountAfterRegNick() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestUser(out var bob);

		HandleWhoisRegNick(client, new(RPL_WHOISREGNICK, "Alice", "Bob"), false);
		HandleWhoisAccount(client, new(RPL_WHOISACCOUNT, "Alice", "Bob", "BobX"), false);
		Assert.AreEqual("BobX", bob.Account);
	}

	[Test]
	public void WhoisChannelsUpdatesStatus() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out _, out _, "Alice +Bob", "#alice");
		client.GetTestChannel(out _, out _, "Alice Bob", "#wonderland");

		HandleWhoisChannels(client, new(RPL_WHOISCHANNELS, "Alice", "Bob", "@#bob +#wonderland #alice"), false);
		Assert.AreEqual("", client.Channels["#alice"].Users["Bob"].Status.ToString());
		Assert.AreEqual("v", client.Channels["#wonderland"].Users["Bob"].Status.ToString());
		Assert.IsFalse(client.Channels.Contains("#bob"));
	}

	[Test]
	public void ChanModes() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		HandleChannelModes(client, new(RPL_CHANNELMODEIS, "Alice", "#wonderland", "+lntk", "8", "hunter2"), false);
		Assert.AreEqual("klnt", string.Join("", wonderland.Modes.OrderBy(c => c)));
		Assert.AreEqual("8", wonderland.Modes.GetParameter('l'));
		Assert.AreEqual("hunter2", wonderland.Modes.GetParameter('k'));
	}

	[Test]
	public void ChannelCreationTimeTest() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		HandleChannelCreationTime(client, new(RPL_CREATIONTIME, "Alice", "#wonderland", "1609459200"), false);
		Assert.AreEqual(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), wonderland.Timestamp);
	}

	[Test]
	public void ChannelTopic() {
		const string SAMPLE_TOPIC = "We're all mad here.";
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		HandleChannelTopic(client, new(RPL_TOPIC, "Alice", "#wonderland", SAMPLE_TOPIC), false);
		HandleTopicStamp(client, new(RPL_TOPICWHOTIME, "Alice", "#wonderland", "Cheshire", "1609459200"), false);
		Assert.AreEqual(SAMPLE_TOPIC, wonderland.Topic);
		Assert.AreEqual("Cheshire", wonderland.TopicSetter);
		Assert.AreEqual(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), wonderland.TopicStamp);
	}

	[Test]
	public void ChannelNoTopic() {
		const string SAMPLE_TOPIC = "We're all mad here.";
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		client.ReceivedLine(new IrcLine(RPL_TOPIC, "Alice", "#wonderland", SAMPLE_TOPIC));
		Assert.AreEqual(SAMPLE_TOPIC, wonderland.Topic);

		HandleNoTopic(client, new(RPL_NOTOPIC, "Alice", "#wonderland"), false);
		Assert.IsNull(wonderland.Topic);
		Assert.IsNull(wonderland.TopicSetter);
	}

	[Test]
	public void WhoReply() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(null, "Alice", JOIN, new[] { "#wonderland" }));
		var wonderland = client.Channels["#wonderland"];

		HandleWhoReply(client, new(RPL_WHOREPLY, "Alice", "#wonderland", "bob", "192.168.0.2", "test.irc.example.com", "Bob", "G*@+", "0 \u00031\u000FBob"), false);
		var bob = client.Channels["#wonderland"].Users["Bob"];
		Assert.AreEqual("bob", bob.User.Ident);
		Assert.AreEqual("192.168.0.2", bob.User.Host);
		Assert.AreEqual("\u00031\u000FBob", bob.User.FullName);
		Assert.AreEqual(Gender.Male, bob.User.Gender);
		Assert.IsTrue(bob.User.IsOper);
		Assert.IsTrue(bob.User.Away);
		Assert.AreEqual("ov", bob.Status.ToString());

		HandleWhoReply(client, new(RPL_WHOREPLY, "Alice", "#wonderland", "bob", "192.168.0.2", "test.irc.example.com", "Bob", "H", "0 \u00031\u000FBob"), false);
		Assert.IsFalse(bob.User.IsOper);
		Assert.IsFalse(bob.User.Away);
		Assert.AreEqual("", bob.Status.ToString());
	}

	[Test]
	public void NamesReplyTriggersTask() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ChannelJoin += client.HandleEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		client.ReceivedLine(new IrcLine(null, "Alice", JOIN, new[] { "#wonderland" }));
		var wonderland = client.Channels["#wonderland"];
		var e = client.AssertEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		Assert.IsNotNull(e.NamesTask);
		Assert.AreEqual(TaskStatus.WaitingForActivation, e.NamesTask!.Status);
		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "Alice Bob"), false);
		Assert.AreEqual(TaskStatus.WaitingForActivation, e.NamesTask!.Status);
		client.ReceivedLine(new IrcLine(RPL_ENDOFNAMES, "Alice", "#wonderland"));  // Must trigger async requests.
		Assert.AreEqual(TaskStatus.RanToCompletion, e.NamesTask!.Status);
	}

	[Test]
	public void NamesReplyWithStatusAndUserHost() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(null, "Alice", JOIN, new[] { "#wonderland" }));
		var wonderland = client.Channels["#wonderland"];

		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "@+Alice!alice@192.168.0.1 Bob"), false);
		Assert.AreSame(client.Me, wonderland.Users["Alice"].User);
		Assert.AreEqual("alice", client.Me.Ident);
		Assert.AreEqual("192.168.0.1", client.Me.Host);
		Assert.AreEqual("ov", wonderland.Users["Alice"].Status.ToString());

		var bob = client.Channels["#wonderland"].Users["Bob"];
		Assert.AreEqual("*", bob.User.Ident);
		Assert.AreEqual("*", bob.User.Host);
		Assert.AreEqual("", bob.Status.ToString());
	}

	[Test]
	public void NamesReplyWithMultipleLines() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(null, "Alice", JOIN, new[] { "#wonderland" }));
		var wonderland = client.Channels["#wonderland"];

		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "Alice Bob"), false);
		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "Carol"), false);
		Assert.AreEqual("Alice Bob Carol", string.Join(' ', wonderland.Users.Select(u => u.Nickname).OrderBy(s => s)));
	}

	[Test]
	public void NamesReplyOverridesPreviousReply() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(null, "Alice", JOIN, new[] { "#wonderland" }));
		var wonderland = client.Channels["#wonderland"];

		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "Alice Bob"), false);
		HandleNamesEnd(client, new(RPL_ENDOFNAMES, "Alice", "#wonderland"), false);
		Assert.AreEqual("Alice Bob", string.Join(' ', wonderland.Users.Select(u => u.Nickname).OrderBy(s => s)));
		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "Alice Carol"), false);
		HandleNamesEnd(client, new(RPL_ENDOFNAMES, "Alice", "#wonderland"), false);
		Assert.AreEqual("Alice Carol", string.Join(' ', wonderland.Users.Select(u => u.Nickname).OrderBy(s => s)));
	}

	[Test]
	public void NamesReplyCallsUserAppeared() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserAppeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		client.ReceivedLine(new IrcLine(null, "Alice", JOIN, new[] { "#wonderland" }));
		var wonderland = client.Channels["#wonderland"];

		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "Alice Bob"), false);
		HandleNamesEnd(client, new(RPL_ENDOFNAMES, "Alice", "#wonderland"), false);
		var e = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		Assert.AreEqual("Bob", e.User.Nickname);
		Assert.AreEqual("#wonderland", string.Join(' ', e.User.Channels.Select(c => c.Name)));
	}

	[Test]
	public void NamesReplyCallsUserDisappeared() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(null, "Alice", JOIN, new[] { "#wonderland" }));
		var wonderland = client.Channels["#wonderland"];

		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "Alice Bob"), false);
		HandleNamesEnd(client, new(RPL_ENDOFNAMES, "Alice", "#wonderland"), false);
		var bob = client.Users["Bob"];
		HandleNamesReply(client, new(RPL_NAMREPLY, "Alice", "=", "#wonderland", "Alice"), false);
		HandleNamesEnd(client, new(RPL_ENDOFNAMES, "Alice", "#wonderland"), false);
		var e = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		Assert.AreSame(bob, e.User);
	}

	[Test]
	public void MotdEnd() {
		// RPL_ENDOFMOTD and RPL_NOMOTD mark the transition from ReceivingServerInfo to Online.
		var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.ReceivingServerInfo };
		client.StateChanged += client.HandleEvent<StateEventArgs>(nameof(client.StateChanged));
		HandleEndOfMotd(client, new(RPL_ENDOFMOTD, "Alice"), false);
		var e = client.AssertEvent<StateEventArgs>(nameof(client.StateChanged));
		Assert.AreEqual(IrcClientState.Online, e.NewState);
	}

	[Test]
	public void NoMotd() {
		var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.ReceivingServerInfo };
		client.StateChanged += client.HandleEvent<StateEventArgs>(nameof(client.StateChanged));
		HandleNoMotd(client, new(ERR_NOMOTD, "Alice"), false);
		var e = client.AssertEvent<StateEventArgs>(nameof(client.StateChanged));
		Assert.AreEqual(IrcClientState.Online, e.NewState);
	}

	#region Watch tests
	[TestCase(RPL_LOGON), TestCase(RPL_NOWON)]
	public void WatchLogOn(string numeric) {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserAppeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		client.MonitorOnline += client.HandleEvent<IrcUserLineEventArgs>(nameof(client.MonitorOnline));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		HandleWatchOnline(client, new(numeric, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		var bob = client.Users["Bob"];
		var e = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		var e2 = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOnline));
		Assert.AreSame(bob, e.User);
		Assert.AreSame(bob, e2.User);
		Assert.IsTrue(bob.IsSeen);
		Assert.IsTrue(bob.IsMonitored);
		Assert.AreEqual("bob", bob.Ident);
		Assert.AreEqual("192.168.0.2", bob.Host);
	}

	[Test]
	public void WatchIsOnWithAwayUser() {
		// This test assumes the user was seen as away before being added to the watch list, and no IRCv3 away-notify.
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);
		client.ReceivedLine(new IrcLine(RPL_WHOREPLY, "Alice", "#wonderland", "bob", "192.168.0.2", "test.irc.example.com", "Bob", "G", "0 \u00031\u000FBob"));
		if (!bob.Away) Assert.Inconclusive("WHO failure in test arrangement");

		HandleWatchOnline(client, new(RPL_NOWON, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		Assert.IsFalse(bob.Away);
		Assert.IsNull(bob.AwayReason);
		Assert.IsNull(bob.AwaySince);
	}

	[TestCase(RPL_LOGOFF), TestCase(RPL_NOWOFF)]
	public void WatchLogOff(string numeric) {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += client.HandleEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserQuit += client.HandleEvent<QuitEventArgs>(nameof(client.UserQuit));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.ReceivedLine(new IrcLine(RPL_NOWON, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"));
		if (!client.Users.TryGetValue("Bob", out var bob) || !bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Watch failure in test arrangement");

		HandleWatchOffline(client, new(numeric, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		var e = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		Assert.AreSame(bob, e.User);
		var e2 = client.AssertEvent<QuitEventArgs>(nameof(client.UserQuit));
		Assert.AreSame(bob, e2.Sender);
		Assert.IsFalse(bob.IsSeen);
		Assert.IsFalse(bob.IsMonitored);
	}

	[TestCase(RPL_LOGOFF), TestCase(RPL_NOWOFF)]
	public void WatchLogOffWhileOnCommonChannel(string numeric) {
		// If the user mentioned in RPL_LOGOFF is on a common channel, we shouldn't register them as offline until they leave those channels.
		// Their quit message should also still appear.
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += client.HandleEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserQuit += client.HandleEvent<QuitEventArgs>(nameof(client.UserQuit));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleWatchOffline(client, new(numeric, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));

		var e = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		Assert.AreSame(bob, e.User);
		Assert.IsTrue(bob.IsSeen);
		Assert.IsTrue(bob.IsMonitored);
		client.AssertEndOfEvents();

		HandleQuit(client, new(null, "Bob", QUIT, new[] { "Connection closed" }), false);
		var e2 = client.AssertEvent<QuitEventArgs>(nameof(client.UserQuit));
		Assert.AreSame(bob, e2.Sender);
		Assert.IsFalse(bob.IsSeen);
	}

	[Test]
	public void WatchAway() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.ReceivedLine(new IrcLine(RPL_NOWON, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"));
		if (!client.Users.TryGetValue("Bob", out var bob) || !bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Watch failure in test arrangement");

		HandleWatchAway(client, new(RPL_GONEAWAY, "Alice", "Bob", "bob", "192.168.0.2", "1609462800", "Auto away"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		Assert.IsTrue(bob.Away);
		Assert.AreEqual("Auto away", bob.AwayReason);
		Assert.AreEqual(new DateTime(2021, 1, 1, 1, 0, 0, DateTimeKind.Utc), bob.AwaySince);
	}

	[Test]
	public void WatchIsAway() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		HandleWatchIsAway(client, new(RPL_NOWISAWAY, "Alice", "Bob", "bob", "192.168.0.2", "1609462800"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		var bob = client.Users["Bob"];
		Assert.IsTrue(bob.IsSeen);
		Assert.IsTrue(bob.IsMonitored);
		Assert.IsTrue(bob.Away);
		Assert.AreEqual(IrcClient.UNKNOWN_AWAY_MESSAGE, bob.AwayReason);
		Assert.AreEqual(new DateTime(2021, 1, 1, 1, 0, 0, DateTimeKind.Utc), bob.AwaySince);
	}

	[Test]
	public void WatchBack() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.ReceivedLine(new IrcLine(RPL_NOWISAWAY, "Alice", "Bob", "bob", "192.168.0.2", "1609462800", "Auto away"));
		if (!client.Users.TryGetValue("Bob", out var bob) || !bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob") || !bob.Away)
			throw new InconclusiveException("Watch failure in test arrangement");

		HandleWatchBack(client, new(RPL_NOTAWAY, "Alice", "Bob", "bob", "192.168.0.2"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		Assert.IsFalse(bob.Away);
		Assert.IsNull(bob.AwayReason);
		Assert.IsNull(bob.AwaySince);
	}

	[Test]
	public void WatchRemovedWhileOffline() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserDisappeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.ReceivedLine(new IrcLine(RPL_NOWOFF, "Alice", "Bob", "*", "*", "0"));
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		if (client.Users.Contains("Bob") || !client.MonitorList.Contains("Bob"))
			Assert.Inconclusive("Watch failure in test arrangement");

		HandleWatchRemoved(client, new(RPL_WATCHOFF, "Alice", "Bob", "*", "*", "0"), false);
		Assert.IsFalse(client.MonitorList.Contains("Bob"));
	}

	[Test]
	public void WatchRemovedWhileOnline() {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.ReceivedLine(new IrcLine(RPL_NOWON, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"));
		if (!client.Users.TryGetValue("Bob", out var bob) || !bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Watch failure in test arrangement");

		HandleWatchRemoved(client, new(RPL_WATCHOFF, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"), false);
		Assert.IsFalse(client.MonitorList.Contains("Bob"));
		var e = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		Assert.AreSame(bob, e.User);
		Assert.IsFalse(bob.IsMonitored);
		Assert.IsFalse(bob.IsSeen);
	}

	[Test]
	public void WatchRemovedWhileOnCommonChannel() {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserDisappeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);
		client.ReceivedLine(new IrcLine(RPL_NOWON, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"));
		if (!bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Watch failure in test arrangement");

		HandleWatchRemoved(client, new(RPL_WATCHOFF, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"), false);
		Assert.IsFalse(client.MonitorList.Contains("Bob"));
		Assert.IsFalse(bob.IsMonitored);
		Assert.IsTrue(bob.IsSeen);
	}

	[Test]
	public void WatchListRetainsWatchedUserWithCommonChannel() {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserDisappeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);
		HandleWatchOnline(client, new(RPL_NOWON, "Alice", "Bob", "bob", "192.168.0.2"), false);
		if (!bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Watch failure in test arrangement");

		HandleWatchListEnd(client, new(RPL_ENDOFWATCHLIST, "Alice"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		Assert.IsTrue(bob.IsMonitored);
	}

	[Test]
	public void WatchListWithMissingUserWithCommonChannel() {
		// If the user not seen in the WATCH list is seen, they must no longer be watched.
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserDisappeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "WATCH=8", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);
		HandleWatchOnline(client, new(RPL_LOGON, "Alice", "Bob", "bob", "192.168.0.2"), false);
		if (!bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Watch failure in test arrangement");

		HandleWatchListEnd(client, new(RPL_ENDOFWATCHLIST, "Alice"), false);
		HandleWatchListEnd(client, new(RPL_ENDOFWATCHLIST, "Alice"), false);  // Empty watch list
		Assert.IsFalse(client.MonitorList.Contains("Bob"));
		Assert.IsFalse(bob.IsMonitored);
		Assert.IsTrue(bob.IsSeen);
	}
	#endregion

	#region Monitor tests
	[Test]
	public void MonitorOnlineWithMultipleNicknames() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserAppeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		client.MonitorOnline += client.HandleEvent<IrcUserLineEventArgs>(nameof(client.MonitorOnline));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		HandleMonitorOnline(client, new(RPL_MONONLINE, "Alice", "Bob,Carol"), false);

		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		var bob = client.Users["Bob"];
		var e = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		var e2 = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOnline));
		Assert.AreSame(bob, e.User);
		Assert.AreSame(bob, e2.User);
		Assert.IsTrue(bob.IsSeen);
		Assert.IsTrue(bob.IsMonitored);
		Assert.AreEqual("*", bob.Ident);
		Assert.AreEqual("*", bob.Host);

		Assert.IsTrue(client.MonitorList.Contains("Carol"));
		var carol = client.Users["Carol"];
		var e3 = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		var e4 = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOnline));
		Assert.AreSame(carol, e3.User);
		Assert.AreSame(carol, e4.User);
		Assert.IsTrue(carol.IsSeen);
		Assert.IsTrue(carol.IsMonitored);
		Assert.AreEqual("*", carol.Ident);
		Assert.AreEqual("*", carol.Host);
	}

	[Test]
	public void MonitorOnlineWithUserHost() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserAppeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		client.MonitorOnline += client.HandleEvent<IrcUserLineEventArgs>(nameof(client.MonitorOnline));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		HandleMonitorOnline(client, new(RPL_MONONLINE, "Alice", "Bob!bob@192.168.0.2"), false);

		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		var bob = client.Users["Bob"];
		var e = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		var e2 = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOnline));
		Assert.AreSame(bob, e.User);
		Assert.AreSame(bob, e2.User);
		Assert.IsTrue(bob.IsSeen);
		Assert.IsTrue(bob.IsMonitored);
		Assert.AreEqual("bob", bob.Ident);
		Assert.AreEqual("192.168.0.2", bob.Host);
	}

	[Test]
	public void MonitorOfflineWithMultipleNicknames() {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += client.HandleEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserQuit += client.HandleEvent<QuitEventArgs>(nameof(client.UserQuit));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		HandleMonitorOnline(client, new(RPL_MONONLINE, "Alice", "Bob,Carol"), false);
		if (!client.Users.TryGetValue("Bob", out var bob) || !bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob") ||
			!client.Users.TryGetValue("Carol", out var carol) || !carol.IsSeen || !carol.IsMonitored || !client.MonitorList.Contains("Carol"))
			throw new InconclusiveException("Monitor failure in test arrangement");

		HandleMonitorOffline(client, new(RPL_MONOFFLINE, "Alice", "Bob,Carol"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		var e = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		Assert.AreSame(bob, e.User);
		var e2 = client.AssertEvent<QuitEventArgs>(nameof(client.UserQuit));
		Assert.AreSame(bob, e2.Sender);
		Assert.IsFalse(bob.IsSeen);
		Assert.IsFalse(bob.IsMonitored);
		var e3 = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		Assert.AreSame(carol, e3.User);
		var e4 = client.AssertEvent<QuitEventArgs>(nameof(client.UserQuit));
		Assert.AreSame(carol, e4.Sender);
		Assert.IsFalse(carol.IsSeen);
		Assert.IsFalse(carol.IsMonitored);
	}

	[Test]
	public void MonitorOfflineWhileOnCommonChannel() {
		// If the user mentioned in RPL_MONOFFLINE is on a common channel, we shouldn't register them as offline until they leave those channels.
		// Their quit message should also still appear.
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += client.HandleEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserQuit += client.HandleEvent<QuitEventArgs>(nameof(client.UserQuit));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleMonitorOffline(client, new(RPL_MONOFFLINE, "Alice", "Bob", "bob", "192.168.0.2", "1609459200"), false);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));

		var e = client.AssertEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		Assert.AreSame(bob, e.User);
		Assert.IsTrue(bob.IsSeen);
		Assert.IsTrue(bob.IsMonitored);
		client.AssertEndOfEvents();

		client.ReceivedLine(new IrcLine(null, "Bob", QUIT, new[] { "Connection closed" }));
		var e2 = client.AssertEvent<QuitEventArgs>(nameof(client.UserQuit));
		Assert.AreSame(bob, e2.Sender);
		Assert.IsFalse(bob.IsSeen);
	}

	[Test]
	public void MonitorListWithNewUsers() {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserDisappeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);
		if (!bob.IsSeen || bob.IsMonitored || client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("NAMES failure in test arrangement");

		HandleMonitorList(client, new(RPL_MONLIST, "Alice", "Bob,Carol"), false);
		HandleMonitorListEnd(client, new(RPL_ENDOFMONLIST, "Alice"), false);
		Assert.IsTrue(bob.IsSeen);
		Assert.IsTrue(bob.IsMonitored);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
		Assert.IsFalse(client.Users.Contains("Carol"));
		Assert.IsTrue(client.MonitorList.Contains("Carol"));
	}

	[Test]
	public void MonitorListRetainsKnownUser() {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserDisappeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		HandleMonitorOnline(client, new(RPL_MONONLINE, "Alice", "Bob"), false);
		if (!client.Users.TryGetValue("Bob", out var bob) || !bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Monitor failure in test arrangement");

		HandleMonitorList(client, new(RPL_MONLIST, "Alice", "Bob"), false);
		HandleMonitorListEnd(client, new(RPL_ENDOFMONLIST, "Alice"), false);
		Assert.IsTrue(bob.IsSeen);
		Assert.IsTrue(bob.IsMonitored);
		Assert.IsTrue(client.MonitorList.Contains("Bob"));
	}

	[Test]
	public void MonitorListWithMissingUserWithCommonChannel() {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserDisappeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);
		HandleMonitorOnline(client, new(RPL_MONONLINE, "Alice", "Bob"), false);
		if (!bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Monitor failure in test arrangement");

		HandleMonitorListEnd(client, new(RPL_ENDOFMONLIST, "Alice"), false);
		Assert.IsTrue(bob.IsSeen);
		Assert.IsFalse(bob.IsMonitored);
		Assert.IsFalse(client.MonitorList.Contains("Bob"));
	}

	[Test]
	public void MonitorListWithMissingUserWithoutCommonChannel() {
		using var client = new TestIrcClient(autoConnect: true);
		client.MonitorOffline += TestIrcClient.HandleInvalidEvent<IrcUserLineEventArgs>(nameof(client.MonitorOffline));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		HandleMonitorOnline(client, new(RPL_MONONLINE, "Alice", "Bob"), false);
		if (!client.Users.TryGetValue("Bob", out var bob) || !bob.IsSeen || !bob.IsMonitored || !client.MonitorList.Contains("Bob"))
			throw new InconclusiveException("Monitor failure in test arrangement");

		HandleMonitorListEnd(client, new(RPL_ENDOFMONLIST, "Alice"), false);
		Assert.IsFalse(bob.IsSeen);
		Assert.IsFalse(bob.IsMonitored);
		Assert.IsFalse(client.MonitorList.Contains("Bob"));

		var e = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		Assert.AreSame(bob, e.User);
	}
	#endregion

	[Test]
	public void LoggedIn() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleLoggedIn(client, new(RPL_LOGGEDIN, "Alice", "Alice!alice@192.168.0.1", "AliceInWonderland"), false);
		Assert.AreEqual("AliceInWonderland", client.Me.Account);
		Assert.AreEqual("192.168.0.1", client.Me.Host);
	}

	[Test]
	public void LoggedOut() {
		using var client = new TestIrcClient(autoConnect: true);
		client.ReceivedLine(new IrcLine(RPL_LOGGEDIN, "Alice", "Alice!alice@192.168.0.1", "AliceInWonderland"));
		if (client.Me.Account is null) throw new InconclusiveException($"{nameof(RPL_LOGGEDIN)} failure in test arrangement");

		IrcMessageHandler handler = Handlers.HandleLoggedOut;
		HandleLoggedOut(client, new(RPL_LOGGEDOUT, "Alice", "Alice!alice@in.wonderland"), false);
		Assert.IsNull(client.Me.Account);
		Assert.AreEqual("in.wonderland", client.Me.Host);
	}

	[Test]
	public void AccountWithAccountTag() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleAccount(client, new(new KeyValuePair<string, string>[] { new("account", "Alice") }, "Alice!alice@192.168.0.1", ACCOUNT, new[] { "AliceInWonderland" }), false);
		Assert.AreEqual("AliceInWonderland", client.Me.Account);
	}

	[Test]
	public void AuthenticateWithEmptyChallenge() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		client.SaslMechanisms.Clear();
		client.SaslMechanisms.Add(new TestSaslMechanism("", ArraySegment<byte>.Empty));
		client.ReceivedLine(new IrcLine(CAP, "*", "ACK", "sasl"));
		client.AssertLine($"{AUTHENTICATE} TEST");
		HandleAuthenticate(client, new(AUTHENTICATE, "+"), false);
		client.AssertLine($"{AUTHENTICATE} +");
	}
	[Test]
	public void AuthenticateWithSingleLineChallenge() {
		const string challengeUtf8 = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Proin et elit rhoncus, feugiat leo at, aliquet dui. Sed egestas libero ut massa dictum, quis dapibus purus porttitor. "
			+ "Aenean auctor erat eget ipsum vulputate mattis. Morbi rutrum iaculis orci. Nunc commodo magna tempor nulla gravida, ac id.";
		const string challengeBase64 = "TG9yZW0gaXBzdW0gZG9sb3Igc2l0IGFtZXQsIGNvbnNlY3RldHVyIGFkaXBpc2NpbmcgZWxpdC4gUHJvaW4gZXQgZWxpdCByaG9uY3VzLCBmZXVnaWF0IGxlbyBhdCwgYWxpcXVldCBkdWkuIFNlZCBlZ2VzdGFzIGxpYmVybyB1dCBtYXNzYSBkaWN0dW0sIHF1aXMg"
			+ "ZGFwaWJ1cyBwdXJ1cyBwb3J0dGl0b3IuIEFlbmVhbiBhdWN0b3IgZXJhdCBlZ2V0IGlwc3VtIHZ1bHB1dGF0ZSBtYXR0aXMuIE1vcmJpIHJ1dHJ1bSBpYWN1bGlzIG9yY2kuIE51bmMgY29tbW9kbyBtYWduYSB0ZW1wb3IgbnVsbGEgZ3JhdmlkYSwgYWMgaWQu";
		// This is 297 bytes long, the most that can fit in a single AUTHENTICATE line.
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		client.SaslMechanisms.Clear();
		client.SaslMechanisms.Add(new TestSaslMechanism(challengeUtf8, ArraySegment<byte>.Empty));
		client.ReceivedLine(new IrcLine(CAP, "*", "ACK", "sasl"));
		client.AssertLine($"{AUTHENTICATE} TEST");
		HandleAuthenticate(client, new(AUTHENTICATE, challengeBase64), false);
		client.AssertLine($"{AUTHENTICATE} +");
	}
	[Test]
	public void AuthenticateWithMultiLineChallenge() {
		const string challengeUtf8 = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Quisque ligula augue, viverra pretium aliquet posuere, sodales sed quam. Nullam tempus pulvinar sem, "
			+ "auctor scelerisque odio mollis lobortis. Donec vitae pharetra velit. Nam iaculis est lorem, et commodo ante faucibus a. Suspendisse eget ac.";
		const string challengeBase64 = "TG9yZW0gaXBzdW0gZG9sb3Igc2l0IGFtZXQsIGNvbnNlY3RldHVyIGFkaXBpc2NpbmcgZWxpdC4gUXVpc3F1ZSBsaWd1bGEgYXVndWUsIHZpdmVycmEgcHJldGl1bSBhbGlxdWV0IHBvc3VlcmUsIHNvZGFsZXMgc2VkIHF1YW0uIE51bGxhbSB0ZW1wdXMgcHVsdmlu"
			+ "YXIgc2VtLCBhdWN0b3Igc2NlbGVyaXNxdWUgb2RpbyBtb2xsaXMgbG9ib3J0aXMuIERvbmVjIHZpdGFlIHBoYXJldHJhIHZlbGl0LiBOYW0gaWFjdWxpcyBlc3QgbG9yZW0sIGV0IGNvbW1vZG8gYW50ZSBmYXVjaWJ1cyBhLiBTdXNwZW5kaXNzZSBlZ2V0IGFjLg==";
		// This is 298 bytes long, the least that requires two AUTHENTICATE lines.
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		client.SaslMechanisms.Clear();
		client.SaslMechanisms.Add(new TestSaslMechanism(challengeUtf8, ArraySegment<byte>.Empty));
		client.ReceivedLine(new IrcLine(CAP, "*", "ACK", "sasl"));
		client.AssertLine($"{AUTHENTICATE} TEST");
		HandleAuthenticate(client, new(AUTHENTICATE, challengeBase64), false);
		HandleAuthenticate(client, new(AUTHENTICATE, "+"), false);
		client.AssertLine($"{AUTHENTICATE} +");
	}
	[Test]
	public void AuthenticateWithEmptyResponse() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		client.SaslMechanisms.Clear();
		client.SaslMechanisms.Add(new TestSaslMechanism(null, ArraySegment<byte>.Empty));
		client.ReceivedLine(new IrcLine(CAP, "*", "ACK", "sasl"));
		client.AssertLine($"{AUTHENTICATE} TEST");
		HandleAuthenticate(client, new(AUTHENTICATE, "+"), false);
		client.AssertLine($"{AUTHENTICATE} +");
	}
	[Test]
	public void AuthenticateWithSingleLineResponse() {
		const string response = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5fYGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn+AgYKDhIWGh4iJiouMjY6PkJGSk5SV"
			+ "lpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq+wsbKztLW2t7i5uru8vb6/wMHCw8TFxsfIycrLzM3Oz9DR0tPU1dbX2Nna29zd3t/g4eLj5OXm5+jp6uvs7e7v8PHy8/T19vf4+fr7/P3+/wABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4fICEiIyQlJico";
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		client.SaslMechanisms.Clear();
		client.SaslMechanisms.Add(new TestSaslMechanism(null, Enumerable.Range(0, 297).Select(i => (byte) i).ToArray()));
		client.ReceivedLine(new IrcLine(CAP, "*", "ACK", "sasl"));
		client.AssertLine($"{AUTHENTICATE} TEST");
		HandleAuthenticate(client, new(AUTHENTICATE, "+"), false);
		client.AssertLine($"{AUTHENTICATE} {response}");
	}
	[Test]
	public void AuthenticateWithMultiLineResponse() {
		const string response = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gISIjJCUmJygpKissLS4vMDEyMzQ1Njc4OTo7PD0+P0BBQkNERUZHSElKS0xNTk9QUVJTVFVWV1hZWltcXV5fYGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn+AgYKDhIWGh4iJiouMjY6PkJGSk5SV"
			+ "lpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq+wsbKztLW2t7i5uru8vb6/wMHCw8TFxsfIycrLzM3Oz9DR0tPU1dbX2Nna29zd3t/g4eLj5OXm5+jp6uvs7e7v8PHy8/T19vf4+fr7/P3+/wABAgMEBQYHCAkKCwwNDg8QERITFBUWFxgZGhscHR4fICEiIyQlJicoKQ==";
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		client.SaslMechanisms.Clear();
		client.SaslMechanisms.Add(new TestSaslMechanism(null, Enumerable.Range(0, 298).Select(i => (byte) i).ToArray()));
		client.ReceivedLine(new IrcLine(CAP, "*", "ACK", "sasl"));
		client.AssertLine($"{AUTHENTICATE} TEST");
		HandleAuthenticate(client, new(AUTHENTICATE, "+"), false);
		client.AssertLine($"{AUTHENTICATE} {response}");
		client.AssertLine($"{AUTHENTICATE} +");
	}

	[Test]
	public void CapLsIgnoresEmptyList() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		client.CapabilitiesAdded += TestIrcClient.HandleInvalidEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		HandleCap(client, new(CAP, "*", "LS", ""), false);
		client.AssertLine($"{CAP} END");
	}
	[Test]
	public void CapLsSingleLine() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		client.CapabilitiesAdded += client.HandleEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		HandleCap(client, new(CAP, "*", "LS", "example.org/dummy-a=value-a example.org/dummy-b=value-b cap-notify"), false);
		var e = client.AssertEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		Assert.AreEqual("cap-notify example.org/dummy-a=value-a example.org/dummy-b=value-b", string.Join(' ', e.Capabilities.OrderBy(c => c.Name).Select(c => c.Parameter is not null ? $"{c.Name}={c.Parameter}" : c.Name)));
		client.AssertLine($"{CAP} REQ cap-notify");
	}
	[Test]
	public void CapLsMultiLine() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		client.CapabilitiesAdded += client.HandleEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		HandleCap(client, new(CAP, "*", "LS", "*", "example.org/dummy-a=value-a"), false);
		client.AssertEndOfEvents();
		HandleCap(client, new(CAP, "*", "LS", "example.org/dummy-b=value-b cap-notify"), false);
		var e = client.AssertEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		Assert.AreEqual("cap-notify example.org/dummy-a=value-a example.org/dummy-b=value-b", string.Join(' ', e.Capabilities.OrderBy(c => c.Name).Select(c => c.Parameter is not null ? $"{c.Name}={c.Parameter}" : c.Name)));
		client.AssertLine($"{CAP} REQ cap-notify");
	}
	[Test]
	public void CapLsIgnoresExistingCaps() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "LS", "example.org/dummy-a example.org/dummy-b cap-notify"), false);
		client.AssertLine($"{CAP} REQ cap-notify");
		client.CapabilitiesAdded += client.HandleEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		HandleCap(client, new(CAP, "*", "LS", "example.org/dummy-a example.org/dummy-b example.org/dummy-c cap-notify"), false);
		var e = client.AssertEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		Assert.AreEqual("example.org/dummy-c", string.Join(' ', e.Capabilities.OrderBy(c => c.Name).Select(c => c.Name)));
		client.AssertLine($"{CAP} END");
	}
	[Test]
	public void CapLsDisconnectsOnTlsUnsupportedWithStartTlsRequired() {
		using var client = new TestIrcClient(autoConnect: true, testRegistration: true, tls: TlsMode.StartTlsRequired);
		client.AssertLine($"{CAP} LS 302");
		HandleCap(client, new(CAP, "*", "LS", ""), false);
		client.AssertLine($"{QUIT} :{IrcClient.QUIT_MESSAGE_STARTTLS_NOT_SUPPORTED}");
	}
	[Test]
	public void CapLsDisconnectsOnSaslUnsupportedWithSaslRequired() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		HandleCap(client, new(CAP, "*", "LS", ""), false);
		client.AssertLine($"{QUIT} :{IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_NOT_SUPPORTED}");
	}
	[Test]
	public void CapLsContinuesOnTlsUnsupportedWithStartTlsOptional() {
		using var client = new TestIrcClient(autoConnect: true, tls: TlsMode.StartTlsOptional) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "LS", ""), false);
		client.AssertLine($"{CAP} END");
	}
	[Test]
	public void CapLsWithStartTls() {
		using var client = new TestIrcClient(autoConnect: true, testRegistration: true, tls: TlsMode.StartTlsOptional);
		client.AssertLine($"{CAP} LS 302");
		HandleCap(client, new(CAP, "*", "LS", "tls"), false);
		client.AssertLine(STARTTLS);
	}
	[Test]
	public void CapLsRespectsChangesInEventHandler() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		client.CapabilitiesAdded += client.HandleEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		client.CapabilitiesAdded += (sender, e) => { e.EnableCapabilities.Remove("cap-notify"); e.EnableIfSupported("example.org/dummy-a"); };
		HandleCap(client, new(CAP, "*", "LS", "example.org/dummy-a=value-a example.org/dummy-b=value-b cap-notify"), false);
		var e = client.AssertEvent<CapabilitiesAddedEventArgs>(nameof(client.CapabilitiesAdded));
		Assert.AreEqual("cap-notify example.org/dummy-a=value-a example.org/dummy-b=value-b", string.Join(' ', e.Capabilities.OrderBy(c => c.Name).Select(c => c.Parameter is not null ? $"{c.Name}={c.Parameter}" : c.Name)));
		client.AssertLine($"{CAP} REQ example.org/dummy-a");
	}
	[Test]
	public void CapLsRequestsSaslWithMechanism() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "LS", "sasl"), false);
		client.AssertLine($"{CAP} REQ sasl");
	}
	[Test]
	public void CapLsDoesNotRequestSaslWithNoMechanism() {
		using var client = new TestIrcClient(autoConnect: true, enableSaslInPlaintext: false) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "LS", "sasl"), false);
		client.AssertLine($"{CAP} END");
	}
	[Test]
	public void CapLs302RequestsSaslWithSharedMechanism() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "LS", "sasl=EXTERNAL,PLAIN"), false);
		client.AssertLine($"{CAP} REQ sasl");
	}
	[Test]
	public void CapLs302DoesNotRequestSaslWithNoSharedMechanism() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "LS", "sasl=PLAIN"), false);
		client.AssertLine($"{CAP} END");
	}
	[Test]
	public void CapLs302DisconnectsIfSaslRequiredWithNoSharedMechanism() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		HandleCap(client, new(CAP, "*", "LS", "sasl=PLAIN"), false);
		client.AssertLine($"{QUIT} :{IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_MECHANISM_NOT_SUPPORTED}");
	}

	[Test]
	public void CapAckEndsNegotiationDuringRegistration() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "ACK", "account-notify away-notify"), false);
		Assert.AreEqual("account-notify away-notify", string.Join(' ', client.EnabledCapabilities.OrderBy(c => c.Name).Select(c => c.Name)));
		client.AssertLine($"{CAP} END");
	}
	[Test]
	public void CapAckDoesNotEndNegotiationAfterRegistration() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.Online };
		HandleCap(client, new(CAP, "*", "ACK", "account-notify away-notify"), false);
		Assert.AreEqual("account-notify away-notify", string.Join(' ', client.EnabledCapabilities.OrderBy(c => c.Name).Select(c => c.Name)));
	}
	[Test]
	public void CapAckStartsSaslAuthentication() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "LS", "sasl"), false);
		client.AssertLine($"{CAP} REQ sasl");
		HandleCap(client, new(CAP, "*", "ACK", "sasl"), false);
		client.AssertLine($"{AUTHENTICATE} EXTERNAL");
	}
	[Test]
	public void CapAckDoesNotStartSaslAuthenticationWithNoMechanisms() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "LS", "sasl"), false);
		client.AssertLine($"{CAP} REQ sasl");
		client.SaslMechanisms.RemoveAll(m => m.Name != "PLAIN");
		HandleCap(client, new(CAP, "*", "ACK", "sasl"), false);
		client.AssertLine($"{CAP} END");
	}
	[Test]
	public void CapAckDisconnectsIfSaslRequiredWithNoMechanisms() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslAuthenticationMode = SaslAuthenticationMode.Required };
		HandleCap(client, new(CAP, "*", "LS", "sasl"), false);
		client.AssertLine($"{CAP} REQ sasl");
		client.SaslMechanisms.RemoveAll(m => m.Name != "PLAIN");
		HandleCap(client, new(CAP, "*", "ACK", "sasl"), false);
		client.AssertLine($"{QUIT} :{IrcClient.QUIT_MESSAGE_SASL_AUTHENTICATION_MECHANISM_NOT_SUPPORTED}");
	}
	[Test]
	public void CapAck302StartsSaslAuthenticationWithSharedMechanism() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslUsername = "AliceInWonderland", SaslPassword = "hunter2" };
		HandleCap(client, new(CAP, "*", "LS", "sasl=PLAIN"), false);
		client.AssertLine($"{CAP} REQ sasl");
		HandleCap(client, new(CAP, "*", "ACK", "sasl"), false);
		client.AssertLine($"{AUTHENTICATE} PLAIN");
	}
	[Test]
	public void CapAck302DoesNotStartSaslAuthenticationWithNoSharedMechanism() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating, SaslUsername = "AliceInWonderland", SaslPassword = "hunter2" };
		HandleCap(client, new(CAP, "*", "LS", "sasl=PLAIN"), false);
		client.AssertLine($"{CAP} REQ sasl");
		client.SaslMechanisms.RemoveAll(m => m.Name != "EXTERNAL");
		HandleCap(client, new(CAP, "*", "ACK", "sasl"), false);
		client.AssertLine($"{CAP} END");
	}

	[Test]
	public void CapNakEndsNegotiationDuringRegistration() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.CapabilityNegotiating };
		HandleCap(client, new(CAP, "*", "NAK", "account-notify away-notify"), false);
		Assert.AreEqual("", string.Join(' ', client.EnabledCapabilities.OrderBy(c => c.Name).Select(c => c.Name)));
		client.AssertLine($"{CAP} END");
	}
	[Test]
	public void CapNakDoesNotEndNegotiationAfterRegistration() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.Online };
		HandleCap(client, new(CAP, "*", "NAK", "account-notify away-notify"), false);
		Assert.AreEqual("", string.Join(' ', client.EnabledCapabilities.OrderBy(c => c.Name).Select(c => c.Name)));
	}

	[Test]
	public void CapDel() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.Online };
		client.CapabilitiesDeleted += client.HandleEvent<CapabilitiesEventArgs>(nameof(client.CapabilitiesDeleted));
		HandleCap(client, new(CAP, "*", "ACK", "account-notify cap-notify"), false);
		if ("account-notify cap-notify" != string.Join(' ', client.SupportedCapabilities.OrderBy(c => c.Name).Select(c => c.Parameter is not null ? $"{c.Name}={c.Parameter}" : c.Name))
			|| "account-notify cap-notify" != string.Join(' ', client.EnabledCapabilities.OrderBy(c => c.Name).Select(c => c.Parameter is not null ? $"{c.Name}={c.Parameter}" : c.Name)))
			Assert.Inconclusive("CAP ACK failure in test arrangement");

		HandleCap(client, new(CAP, "*", "DEL", "account-notify"), false);
		var e = client.AssertEvent<CapabilitiesEventArgs>(nameof(client.CapabilitiesDeleted));
		Assert.AreEqual("account-notify", string.Join(' ', e.Capabilities.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.AreEqual("cap-notify", string.Join(' ', client.SupportedCapabilities.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.AreEqual("cap-notify", string.Join(' ', client.EnabledCapabilities.OrderBy(c => c.Name).Select(c => c.Name)));
	}

	[Test]
	public void ChgHost() {
		using var client = new TestIrcClient(autoConnect: true);
		var alice = client.Me;
		HandleChgHost(client, new(null, "Alice!~alice@192.168.0.1", CHGHOST, new[] { "alice", "in.wonderland" }), false);
		Assert.AreEqual("alice", alice.Ident);
		Assert.AreEqual("in.wonderland", alice.Host);
	}

	[Test]
	public void JoinSelfNewChannel() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserAppeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		client.ChannelJoin += client.HandleEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));

		HandleJoin(client, new(null, "Alice!alice@in.wonderland", JOIN, new[] { "#wonderland", "AliceInWonderland", "\u00032\u000FAlice" }), false);
		var e = client.AssertEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreSame(e.Channel, client.Channels["#wonderland"]);
		Assert.AreEqual("#wonderland", string.Join(' ', client.Me.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.AreSame(e.Channel, client.Me.Channels["#wonderland"]);
		Assert.AreEqual(TaskStatus.WaitingForActivation, e.NamesTask!.Status);
	}
	[Test]
	public void JoinSelfWithExtendedJoin() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserAppeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		client.ChannelJoin += client.HandleEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));

		HandleJoin(client, new(null, "Alice!alice@in.wonderland", JOIN, new[] { "#wonderland", "AliceInWonderland", "\u00032\u000FAlice" }), false);
		var e = client.AssertEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreEqual("AliceInWonderland", e.Sender.Account);
		Assert.AreEqual("\u00032\u000FAlice", e.Sender.FullName);
	}
	[Test]
	public void JoinSelfExistingChannel() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserAppeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		HandleJoin(client, new(null, "Alice!alice@in.wonderland", JOIN, new[] { "#wonderland" }), false);
		var wonderland = client.Channels["#wonderland"];

		client.ChannelJoin += client.HandleEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		HandleJoin(client, new(null, "Alice!alice@in.wonderland", JOIN, new[] { "#wonderland" }), false);
		var e = client.AssertEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("#wonderland", string.Join(' ', client.Me.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.AreSame(wonderland, client.Me.Channels["#wonderland"]);
		Assert.AreEqual(TaskStatus.WaitingForActivation, e.NamesTask!.Status);
	}
	[Test]
	public void JoinOtherUnseen() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleJoin(client, new(null, "Alice!alice@in.wonderland", JOIN, new[] { "#wonderland" }), false);
		var wonderland = client.Channels["#wonderland"];

		client.UserAppeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		client.ChannelJoin += client.HandleEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		HandleJoin(client, new(null, "Bob!bob@192.168.0.2", JOIN, new[] { "#wonderland" }), false);
		var e = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		Assert.AreEqual("Bob", e.User.Nickname);
		var e2 = client.AssertEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		Assert.AreSame(e.User, e2.Sender);
		Assert.IsNull(e2.NamesTask);
		Assert.AreEqual("Alice Bob", string.Join(' ', wonderland.Users.OrderBy(u => u.Nickname).Select(u => u.Nickname)));
		Assert.AreEqual("#wonderland", string.Join(' ', e.User.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
	}
	[Test]
	public void JoinWithAccountTag() {
		using var client = new TestIrcClient(autoConnect: true);
		HandleJoin(client, new(null, "Alice!alice@in.wonderland", JOIN, new[] { "#wonderland" }), false);
		var wonderland = client.Channels["#wonderland"];

		client.ChannelJoin += client.HandleEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		HandleJoin(client, new(new KeyValuePair<string, string>[] { new("account", "BobX") }, "Bob!bob@192.168.0.2", JOIN, new[] { "#wonderland" }), false);
		var e = client.AssertEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		Assert.AreEqual("BobX", e.Sender.Account);
	}
	[Test]
	public void JoinOtherSeen() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestUser(out var bob);
		HandleJoin(client, new(null, "Alice!alice@in.wonderland", JOIN, new[] { "#wonderland" }), false);
		var wonderland = client.Channels["#wonderland"];

		client.UserAppeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserAppeared));
		client.ChannelJoin += client.HandleEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		HandleJoin(client, new(null, "Bob!bob@192.168.0.2", JOIN, new[] { "#wonderland" }), false);
		var e = client.AssertEvent<ChannelJoinEventArgs>(nameof(client.ChannelJoin));
		Assert.AreSame(bob, e.Sender);
		Assert.IsNull(e.NamesTask);
		Assert.AreEqual("Alice Bob", string.Join(' ', wonderland.Users.OrderBy(u => u.Nickname).Select(u => u.Nickname)));
		Assert.AreEqual("#wonderland", string.Join(' ', bob.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
	}

	[Test]
	public void KickOtherSeen() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestUser(out var bob);
		client.GetTestChannel(out var wonderland, out _);

		client.UserDisappeared += TestIrcClient.HandleInvalidEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		client.ChannelKick += client.HandleEvent<ChannelKickEventArgs>(nameof(client.ChannelKick));
		client.ChannelLeave += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		HandleKick(client, new(null, "Alice!alice@192.168.0.1", KICK, new[] { "#wonderland", "Bob", "Get out." }), false);
		var e = client.AssertEvent<ChannelKickEventArgs>(nameof(client.ChannelKick));
		var e2 = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual(bob.Nickname, e.Target.Nickname);
		Assert.AreEqual("Get out.", e.Reason);
		Assert.AreSame(bob, e2.Sender);
		Assert.AreSame(wonderland, e2.Channel);
		Assert.AreEqual("Alice", string.Join(' ', wonderland.Users.OrderBy(u => u.Nickname).Select(u => u.Nickname)));
		Assert.AreEqual("", string.Join(' ', bob.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.IsTrue(bob.IsSeen);
	}
	[Test]
	public void KickOtherUnseen() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		client.ChannelKick += client.HandleEvent<ChannelKickEventArgs>(nameof(client.ChannelKick));
		client.ChannelLeave += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		HandleKick(client, new(null, "Alice!alice@192.168.0.1", KICK, new[] { "#wonderland", "Bob", "Get out." }), false);
		var e = client.AssertEvent<ChannelKickEventArgs>(nameof(client.ChannelKick));
		var e2 = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		var e3 = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual(bob.Nickname, e.Target.Nickname);
		Assert.AreEqual("Get out.", e.Reason);
		Assert.AreSame(bob, e2.Sender);
		Assert.AreSame(wonderland, e2.Channel);
		Assert.AreSame(bob, e3.User);
		Assert.AreEqual("Alice", string.Join(' ', wonderland.Users.OrderBy(u => u.Nickname).Select(u => u.Nickname)));
		Assert.AreEqual("", string.Join(' ', bob.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.IsFalse(bob.IsSeen);
	}
	[Test]
	public void KickSelf() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		client.ChannelKick += client.HandleEvent<ChannelKickEventArgs>(nameof(client.ChannelKick));
		client.ChannelLeave += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		HandleKick(client, new(null, "Bob!bob@192.168.0.2", KICK, new[] { "#wonderland", "Alice", "Get out." }), false);
		var e = client.AssertEvent<ChannelKickEventArgs>(nameof(client.ChannelKick));
		var e2 = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		var e3 = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(client.Me, e.Target.User);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("Get out.", e.Reason);
		Assert.AreSame(client.Me, e2.Sender);
		Assert.AreSame(wonderland, e2.Channel);
		Assert.AreSame(bob, e3.User);
		Assert.AreEqual("", string.Join(' ', wonderland.Users.OrderBy(u => u.Nickname).Select(u => u.Nickname)));
		Assert.AreEqual("", string.Join(' ', bob.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.AreEqual("", string.Join(' ', client.Me.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.IsFalse(bob.IsSeen);
	}

	[Test]
	public void ModeWithUserModes() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserModesSet += client.HandleEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		client.ChannelModesSet += client.HandleEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		client.ReceivedLine(new IrcLine(RPL_MYINFO, "Alice", "test.irc.example.com", "1.0", "Oaiorsw", "Iabeiklmnopqrstv"));
		Assert.AreEqual("", client.UserModes.ToString());

		HandleMode(client, new(MODE, "Alice", "+iw"), false);
		var e = client.AssertEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		Assert.AreEqual("+iw", e.Modes);
		Assert.AreEqual("iw", client.UserModes.ToString());

		HandleMode(client, new(MODE, "Alice", "+o"), false);
		e = client.AssertEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		Assert.AreEqual("+o", e.Modes);
		Assert.AreEqual("iow", client.UserModes.ToString());

		HandleMode(client, new(MODE, "Alice", "-i"), false);
		e = client.AssertEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		Assert.AreEqual("-i", e.Modes);
		Assert.AreEqual("ow", client.UserModes.ToString());
	}

	[Test]
	public void ModeWithChannelModes() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserModesSet += client.HandleEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		client.ChannelModesSet += client.HandleEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		client.ReceivedLine(new IrcLine(RPL_MYINFO, "Alice", "test.irc.example.com", "1.0", "Oaiorsw", "Iabeiklmnopqrstv"));
		client.GetTestChannel(out var wonderland, out var bob);
		client.ReceivedLine(new IrcLine(RPL_CHANNELMODEIS, "Alice", "#wonderland", "+nt"));

		HandleMode(client, new(null, "Bob!bob@192.168.0.2", MODE, new[] { "#wonderland", "+lm-t+k", "8", "hunter2" }), false);
		var e = client.AssertEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreSame(bob, e.Sender);
		Assert.AreEqual("+l 8,+m,-t,+k hunter2", string.Join(',', e.Modes.Select(m => m.ToString())));
		Assert.AreEqual("mn k:hunter2 l:8", wonderland.Modes.ToString());
	}

	[Test]
	public void ModeWithChannelStatusModes() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserModesSet += client.HandleEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		client.ChannelModesSet += client.HandleEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		client.ReceivedLine(new IrcLine(RPL_MYINFO, "Alice", "test.irc.example.com", "1.0", "Oaiorsw", "Iabeiklmnopqrstv"));
		client.GetTestChannel(out var wonderland, out var bob, "+Alice @Bob +Eve");

		HandleMode(client, new(null, "Bob!bob@192.168.0.2", MODE, new[] { "#wonderland", "+oo-v", "Alice", "Bob", "Eve" }), false);
		var e = client.AssertEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		Assert.AreEqual("+o Alice,+o Bob,-v Eve", string.Join(',', e.Modes.Select(m => m.ToString())));
		Assert.AreEqual("", wonderland.Modes.ToString());
		Assert.AreEqual("ov", wonderland.Users["Alice"].Status.ToString());
		Assert.AreEqual("o", wonderland.Users["Bob"].Status.ToString());
		Assert.AreEqual("", wonderland.Users["Eve"].Status.ToString());
	}

	[Test]
	public void ModeWithChannelModeTypeBUnset() {
		// Type B modes don't have a parameter when removed in the client-bound MODE message, only in the server-bound one.
		using var client = new TestIrcClient(autoConnect: true);
		client.UserModesSet += client.HandleEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		client.ChannelModesSet += client.HandleEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		client.ReceivedLine(new IrcLine(RPL_MYINFO, "Alice", "test.irc.example.com", "1.0", "Oaiorsw", "Iabeiklmnopqrstv"));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleMode(client, new(null, "Bob!bob@192.168.0.2", MODE, new[] { "#wonderland", "-k+l", "8" }), false);
		var e = client.AssertEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		Assert.AreEqual("-k,+l 8", string.Join(',', e.Modes.Select(m => m.ToString())));
	}

	[Test]
	public void ModeWithChannelModeTypeA() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserModesSet += client.HandleEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		client.ChannelModesSet += client.HandleEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		client.ReceivedLine(new IrcLine(RPL_MYINFO, "Alice", "test.irc.example.com", "1.0", "Oaiorsw", "Iabeiklmnopqrstv"));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleMode(client, new(null, "Bob!bob@192.168.0.2", MODE, new[] { "#wonderland", "+bl", "*!*mallory@192.168.6.66", "8" }), false);
		var e = client.AssertEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		Assert.AreEqual("+b *!*mallory@192.168.6.66,+l 8", string.Join(',', e.Modes.Select(m => m.ToString())));
		Assert.AreEqual("l:8", wonderland.Modes.ToString());
	}

	[Test]
	public void ModeWithNonStandardChannelModes() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserModesSet += client.HandleEvent<UserModesEventArgs>(nameof(client.UserModesSet));
		client.ChannelModesSet += client.HandleEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		client.ReceivedLine(new IrcLine(RPL_MYINFO, "Alice", "test.irc.example.com", "1.0", "Oaiorsw", "Iabeiklmnopqrstv"));
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "CHANMODES=Ibew,kp,jl,aimnpqrst", "are supported by this server"));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleMode(client, new(null, "Bob!bob@192.168.0.2", MODE, new[] { "#wonderland", "+w-p+jl", "o:R:Alice", "5:10", "8" }), false);
		var e2 = client.AssertEvent<ChannelModesSetEventArgs>(nameof(client.ChannelModesSet));
		Assert.AreEqual("+w o:R:Alice,-p,+j 5:10,+l 8", string.Join(',', e2.Modes.Select(m => m.ToString())));
		Assert.AreEqual("j:5:10 l:8", wonderland.Modes.ToString());
	}

	[Test]
	public void NickSelf() {
		using var client = new TestIrcClient(autoConnect: true);
		client.NicknameChange += client.HandleEvent<NicknameChangeEventArgs>(nameof(client.NicknameChange));
		client.NicknameChange += (sender, e) => Assert.AreEqual("Alice", e.Sender.Nickname);  // This assertion needs to happen during the event handler.
		client.GetTestChannel(out var wonderland, out var bob, "+Alice @Bob");

		HandleNick(client, new(null, "Alice!alice@in.wonderland", NICK, new[] { "Alicia" }), false);
		var e = client.AssertEvent<NicknameChangeEventArgs>(nameof(client.NicknameChange));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreSame("Alicia", e.NewNickname);
		Assert.AreSame("Alicia", client.Me.Nickname);
		Assert.AreSame(client.Me, client.Users["Alicia"]);
		Assert.IsFalse(client.Users.Contains("Alice"));
		Assert.AreEqual("v", wonderland.Users["Alicia"].Status.ToString());
		Assert.IsFalse(wonderland.Users.Contains("Alice"));
	}
	[Test]
	public void NickOther() {
		using var client = new TestIrcClient(autoConnect: true);
		client.NicknameChange += client.HandleEvent<NicknameChangeEventArgs>(nameof(client.NicknameChange));
		client.NicknameChange += (sender, e) => Assert.AreEqual("Bob", e.Sender.Nickname);  // This assertion needs to happen during the event handler.
		client.GetTestChannel(out var wonderland, out var bob);

		HandleNick(client, new(null, "Bob!bob@192.168.0.2", NICK, new[] { "Robert" }), false);
		var e = client.AssertEvent<NicknameChangeEventArgs>(nameof(client.NicknameChange));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame("Robert", e.NewNickname);
		Assert.AreSame("Robert", bob.Nickname);
		Assert.AreSame(bob, client.Users["Robert"]);
		Assert.IsFalse(client.Users.Contains("Bob"));
		Assert.AreEqual("o", wonderland.Users["Robert"].Status.ToString());
		Assert.IsFalse(wonderland.Users.Contains("Bob"));
	}

	private static TestIrcClient SetupMessageTest(out IrcUser bob, out IrcChannel wonderland) {
		var client = new TestIrcClient(autoConnect: true);
		client.BroadcastMessage += client.HandleEvent<PrivateMessageEventArgs>(nameof(client.BroadcastMessage));
		client.ChannelMessage += client.HandleEvent<ChannelMessageEventArgs>(nameof(client.ChannelMessage));
		client.ChannelCTCP += client.HandleEvent<ChannelMessageEventArgs>(nameof(client.ChannelCTCP));
		client.PrivateMessage += client.HandleEvent<PrivateMessageEventArgs>(nameof(client.PrivateMessage));
		client.PrivateCTCP += client.HandleEvent<PrivateMessageEventArgs>(nameof(client.PrivateCTCP));
		client.BroadcastNotice += client.HandleEvent<PrivateMessageEventArgs>(nameof(client.BroadcastNotice));
		client.ChannelNotice += client.HandleEvent<ChannelMessageEventArgs>(nameof(client.ChannelNotice));
		client.PrivateNotice += client.HandleEvent<PrivateMessageEventArgs>(nameof(client.PrivateNotice));
		client.GetTestChannel(out wonderland, out bob);
		return client;
	}
	[Test]
	public void NoticeToChannel() {
		using var client = SetupMessageTest(out var bob, out var wonderland);
		HandleNotice(client, new(null, "Bob!bob@192.168.0.2", NOTICE, new[] { "#wonderland", "Hello world!" }), false);
		var e = client.AssertEvent<ChannelMessageEventArgs>(nameof(client.ChannelNotice));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("Hello world!", e.Message);
		Assert.IsEmpty(e.Status);
	}
	[Test]
	public void NoticeToChannelStatus() {
		using var client = SetupMessageTest(out var bob, out var wonderland);
		HandleNotice(client, new(null, "Bob!bob@192.168.0.2", NOTICE, new[] { "@#wonderland", "Hello world!" }), false);
		var e = client.AssertEvent<ChannelMessageEventArgs>(nameof(client.ChannelNotice));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("Hello world!", e.Message);
		Assert.AreEqual("o", e.Status.ToString());
	}
	[Test]
	public void NoticePrivate() {
		using var client = SetupMessageTest(out var bob, out _);
		HandleNotice(client, new(null, "Bob!bob@192.168.0.2", NOTICE, new[] { "Alice", "Hello Alice!" }), false);
		var e = client.AssertEvent<PrivateMessageEventArgs>(nameof(client.PrivateNotice));
		Assert.AreSame(bob, e.Sender);
		Assert.AreEqual("Alice", e.Target);
		Assert.AreEqual("Hello Alice!", e.Message);
	}
	[Test]
	public void NoticeGlobal() {
		using var client = SetupMessageTest(out var bob, out _);
		HandleNotice(client, new(null, "Bob!bob@192.168.0.2", NOTICE, new[] { "$*", "Hello world!" }), false);
		var e = client.AssertEvent<PrivateMessageEventArgs>(nameof(client.BroadcastNotice));
		Assert.AreSame(bob, e.Sender);
		Assert.AreEqual("$*", e.Target);
		Assert.AreEqual("Hello world!", e.Message);
	}

	[Test]
	public void PartOtherSeen() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestUser(out var bob);
		client.GetTestChannel(out var wonderland, out _);

		client.ChannelPart += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelPart));
		client.ChannelLeave += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		HandlePart(client, new(null, "Bob!bob@192.168.0.2", PART, new[] { "#wonderland", "See you later." }), false);
		var e = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelPart));
		var e2 = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("See you later.", e.Message);
		Assert.AreSame(bob, e2.Sender);
		Assert.AreSame(wonderland, e2.Channel);
		Assert.AreEqual("Alice", string.Join(' ', wonderland.Users.OrderBy(u => u.Nickname).Select(u => u.Nickname)));
		Assert.AreEqual("", string.Join(' ', bob.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.IsTrue(bob.IsSeen);
	}
	[Test]
	public void PartOtherUnseen() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		client.ChannelPart += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelPart));
		client.ChannelLeave += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		HandlePart(client, new(null, "Bob!bob@192.168.0.2", PART, new[] { "#wonderland", "See you later." }), false);
		var e = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelPart));
		var e2 = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		var e3 = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("See you later.", e.Message);
		Assert.AreSame(bob, e2.Sender);
		Assert.AreSame(wonderland, e2.Channel);
		Assert.AreSame(bob, e3.User);
		Assert.AreEqual("Alice", string.Join(' ', wonderland.Users.OrderBy(u => u.Nickname).Select(u => u.Nickname)));
		Assert.AreEqual("", string.Join(' ', bob.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.IsFalse(bob.IsSeen);
	}
	[Test]
	public void PartSelf() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		client.ChannelPart += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelPart));
		client.ChannelLeave += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		client.UserDisappeared += client.HandleEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		HandlePart(client, new(null, "Alice!alice@192.168.0.1", PART, new[] { "#wonderland", "See you later." }), false);
		var e = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelPart));
		var e2 = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		var e3 = client.AssertEvent<IrcUserEventArgs>(nameof(client.UserDisappeared));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreSame(client.Me, e2.Sender);
		Assert.AreSame(wonderland, e2.Channel);
		Assert.AreSame(bob, e3.User);
		Assert.AreEqual("", string.Join(' ', wonderland.Users.OrderBy(u => u.Nickname).Select(u => u.Nickname)));
		Assert.AreEqual("", string.Join(' ', bob.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.AreEqual("", string.Join(' ', client.Me.Channels.OrderBy(c => c.Name).Select(c => c.Name)));
		Assert.IsFalse(bob.IsSeen);
	}
	[Test]
	public void PartWithNoMessage() {
		using var client = new TestIrcClient(autoConnect: true);
		client.GetTestChannel(out var wonderland, out var bob);

		client.ChannelPart += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelPart));
		HandlePart(client, new(null, "Bob!bob@192.168.0.2", PART, new[] { "#wonderland" }), false);
		var e = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelPart));
		Assert.IsNull(e.Message);
	}

	[Test]
	public void HandlePingTest() {
		using var client = new TestIrcClient(autoConnect: true) { State = IrcClientState.Registering };
		HandlePing(client, new(PING, "Test ping"), false);
		client.AssertLine($"{PONG} :Test ping");
	}

	[Test]
	public void PrivmsgToChannel() {
		using var client = SetupMessageTest(out var bob, out var wonderland);
		HandlePrivmsg(client, new(null, "Bob!bob@192.168.0.2", PRIVMSG, new[] { "#wonderland", "Hello world!" }), false);
		var e = client.AssertEvent<ChannelMessageEventArgs>(nameof(client.ChannelMessage));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("Hello world!", e.Message);
		Assert.IsEmpty(e.Status);
	}
	[Test]
	public void PrivmsgCtcpToChannel() {
		using var client = SetupMessageTest(out var bob, out var wonderland);
		HandlePrivmsg(client, new(null, "Bob!bob@192.168.0.2", PRIVMSG, new[] { "#wonderland", "\u0001PING 123456\u0001" }), false);
		var e = client.AssertEvent<ChannelMessageEventArgs>(nameof(client.ChannelCTCP));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("PING 123456", e.Message);
		Assert.IsEmpty(e.Status);
	}
	[Test]
	public void PrivmsgToChannelStatus() {
		using var client = SetupMessageTest(out var bob, out var wonderland);
		HandlePrivmsg(client, new(null, "Bob!bob@192.168.0.2", PRIVMSG, new[] { "@#wonderland", "Hello world!" }), false);
		var e = client.AssertEvent<ChannelMessageEventArgs>(nameof(client.ChannelMessage));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(wonderland, e.Channel);
		Assert.AreEqual("Hello world!", e.Message);
		Assert.AreEqual("o", e.Status.ToString());
	}
	[Test]
	public void PrivmsgPrivate() {
		using var client = SetupMessageTest(out var bob, out _);
		HandlePrivmsg(client, new(null, "Bob!bob@192.168.0.2", PRIVMSG, new[] { "Alice", "Hello Alice!" }), false);
		var e = client.AssertEvent<PrivateMessageEventArgs>(nameof(client.PrivateMessage));
		Assert.AreSame(bob, e.Sender);
		Assert.AreEqual("Alice", e.Target);
		Assert.AreEqual("Hello Alice!", e.Message);
	}
	[Test]
	public void PrivmsgCtcpPrivate() {
		using var client = SetupMessageTest(out var bob, out _);
		HandlePrivmsg(client, new(null, "Bob!bob@192.168.0.2", PRIVMSG, new[] { "Alice", "\u0001PING 123456\u0001" }), false);
		var e = client.AssertEvent<PrivateMessageEventArgs>(nameof(client.PrivateCTCP));
		Assert.AreSame(bob, e.Sender);
		Assert.AreEqual("Alice", e.Target);
		Assert.AreEqual("PING 123456", e.Message);
	}
	[Test]
	public void PrivmsgGlobal() {
		using var client = SetupMessageTest(out var bob, out _);
		HandlePrivmsg(client, new(null, "Bob!bob@192.168.0.2", PRIVMSG, new[] { "$*", "Hello world!" }), false);
		var e = client.AssertEvent<PrivateMessageEventArgs>(nameof(client.BroadcastMessage));
		Assert.AreSame(bob, e.Sender);
		Assert.AreEqual("$*", e.Target);
		Assert.AreEqual("Hello world!", e.Message);
	}
	[Test]
	public void PrivmsgCtcpWithoutTrailingDelimiter() {
		using var client = SetupMessageTest(out var bob, out _);
		HandlePrivmsg(client, new(null, "Bob!bob@192.168.0.2", PRIVMSG, new[] { "Alice", "\u0001PING 123456" }), false);
		var e = client.AssertEvent<PrivateMessageEventArgs>(nameof(client.PrivateCTCP));
		Assert.AreSame(bob, e.Sender);
		Assert.AreEqual("Alice", e.Target);
		Assert.AreEqual("PING 123456", e.Message);
	}
	[Test]
	public void PrivmsgCtcpWithMultipleMessages() {
		using var client = SetupMessageTest(out _, out _);
		HandlePrivmsg(client, new(null, "Mallory!mallory@192.168.6.66", PRIVMSG, new[] { "Alice", "\u0001PING\u0001\u0001PING\u0001\u0001PING\u0001\u0001PING\u0001" }), false);
		var e = client.AssertEvent<PrivateMessageEventArgs>(nameof(client.PrivateCTCP));
		Assert.AreEqual("PING\u0001\u0001PING\u0001\u0001PING\u0001\u0001PING", e.Message);
	}

	[Test]
	public void QuitOther() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserQuit += client.HandleEvent<QuitEventArgs>(nameof(client.UserQuit));
		client.ChannelLeave += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleQuit(client, new(null, "Bob!bob@192.168.0.2", QUIT, new[] { "Quit: See you later!" }), false);
		var e = client.AssertEvent<QuitEventArgs>(nameof(client.UserQuit));
		Assert.AreSame(bob, e.Sender);
		Assert.AreEqual("Quit: See you later!", e.Message);
		var e2 = client.AssertEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		Assert.AreSame(bob, e.Sender);
		Assert.AreSame(wonderland, e2.Channel);
		Assert.AreEqual("Quit: See you later!", e2.Message);

		Assert.IsFalse(bob.IsSeen);
		Assert.AreEqual("Alice", string.Join(' ', wonderland.Users.Select(u => u.Nickname).OrderBy(s => s)));
	}
	[Test]
	public void QuitSelf() {
		using var client = new TestIrcClient(autoConnect: true);
		client.UserQuit += client.HandleEvent<QuitEventArgs>(nameof(client.UserQuit));
		client.ChannelLeave += client.HandleEvent<ChannelPartEventArgs>(nameof(client.ChannelLeave));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleQuit(client, new(null, "Alice!alice@192.168.0.1", QUIT, new[] { "Quit: See you later!" }), false);
		var e = client.AssertEvent<QuitEventArgs>(nameof(client.UserQuit));
		Assert.AreSame(client.Me, e.Sender);
		Assert.IsTrue(client.Channels.Contains("#wonderland"));  // We shouldn't be considered offline until the connection is actually closed.
	}

	[Test]
	public void TopicWithoutServerTime() {
		const string SAMPLE_TOPIC = "We're all mad here.";
		using var client = new TestIrcClient(autoConnect: true) { testUtcNow = new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc) };
		client.ChannelTopicChanged += client.HandleEvent<ChannelTopicChangeEventArgs>(nameof(client.ChannelTopicChanged));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleTopic(client, new(null, "Alice!alice@192.168.0.1", TOPIC, new[] { "#wonderland", SAMPLE_TOPIC }), false);
		var e = client.AssertEvent<ChannelTopicChangeEventArgs>(nameof(client.ChannelTopicChanged));
		Assert.AreSame(client.Me, e.Sender);
		Assert.IsNull(e.OldTopic);
		Assert.IsNull(e.OldTopicSetter);
		Assert.IsNull(e.OldTopicStamp);
		Assert.AreEqual(SAMPLE_TOPIC, wonderland.Topic);
		Assert.AreEqual("Alice!alice@192.168.0.1", wonderland.TopicSetter);
		Assert.AreEqual(new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc), wonderland.TopicStamp);
	}
	[Test]
	public void TopicWithServerTime() {
		const string SAMPLE_TOPIC = "We're all mad here.";
		using var client = new TestIrcClient(autoConnect: true) { testUtcNow = new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc) };
		client.ChannelTopicChanged += client.HandleEvent<ChannelTopicChangeEventArgs>(nameof(client.ChannelTopicChanged));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleTopic(client, new(new[] { new KeyValuePair<string, string>("time", "2021-01-01T00:00:00Z") }, "Alice!alice@192.168.0.1", TOPIC, new[] { "#wonderland", SAMPLE_TOPIC }), false);
		var e = client.AssertEvent<ChannelTopicChangeEventArgs>(nameof(client.ChannelTopicChanged));
		Assert.AreSame(client.Me, e.Sender);
		Assert.IsNull(e.OldTopic);
		Assert.IsNull(e.OldTopicSetter);
		Assert.IsNull(e.OldTopicStamp);
		Assert.AreEqual(SAMPLE_TOPIC, wonderland.Topic);
		Assert.AreEqual("Alice!alice@192.168.0.1", wonderland.TopicSetter);
		Assert.AreEqual(new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc), wonderland.TopicStamp);
	}
	[Test]
	public void TopicWithExistingTopic() {
		const string SAMPLE_TOPIC = "We're all mad here.";
		using var client = new TestIrcClient(autoConnect: true) { testUtcNow = new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc) };
		client.ChannelTopicChanged += client.HandleEvent<ChannelTopicChangeEventArgs>(nameof(client.ChannelTopicChanged));
		client.GetTestChannel(out var wonderland, out var bob);
		client.ReceivedLine(new IrcLine(RPL_TOPIC, "Alice", "#wonderland", "Hello world"));
		HandleTopicStamp(client, new(RPL_TOPICWHOTIME, "Alice", "#wonderland", "Cheshire", "1609459200"), false);

		HandleTopic(client, new(null, "Alice!alice@192.168.0.1", TOPIC, new[] { "#wonderland", SAMPLE_TOPIC }), false);
		var e = client.AssertEvent<ChannelTopicChangeEventArgs>(nameof(client.ChannelTopicChanged));
		Assert.AreSame(client.Me, e.Sender);
		Assert.AreEqual("Hello world", e.OldTopic);
		Assert.AreEqual("Cheshire", e.OldTopicSetter);
		Assert.AreEqual(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), e.OldTopicStamp);
		Assert.AreEqual(SAMPLE_TOPIC, wonderland.Topic);
		Assert.AreEqual("Alice!alice@192.168.0.1", wonderland.TopicSetter);
		Assert.AreEqual(new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc), wonderland.TopicStamp);
	}
	[Test]
	public void TopicWithEmptyTopic() {
		using var client = new TestIrcClient(autoConnect: true) { testUtcNow = new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc) };
		client.ChannelTopicChanged += client.HandleEvent<ChannelTopicChangeEventArgs>(nameof(client.ChannelTopicChanged));
		client.GetTestChannel(out var wonderland, out var bob);

		HandleTopic(client, new(null, "Alice!alice@192.168.0.1", TOPIC, new[] { "#wonderland", "" }), false);
		var e = client.AssertEvent<ChannelTopicChangeEventArgs>(nameof(client.ChannelTopicChanged));
		Assert.AreEqual("", wonderland.Topic);
		Assert.AreEqual("Alice!alice@192.168.0.1", wonderland.TopicSetter);
		Assert.AreEqual(new DateTime(2020, 1, 1, 12, 34, 56, DateTimeKind.Utc), wonderland.TopicStamp);
	}
}
