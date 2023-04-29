using NUnit.Framework;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using static AnIRC.Replies;

namespace AnIRC.Tests;

[TestFixture]
public class IrcClientTests {
	[Test]
	public void ConstructorRejectsBoundIrcLocalUser() {
		var me = new IrcLocalUser("Alice", "alice", "\u00032Alice");
		_ = new IrcClient(me);  // This binds the IrcLocalUser instance to the IrcClient. It can't then be used with a new IrcClient.
		Assert.Throws<ArgumentException>(() => new IrcClient(me));
	}
	[Test]
	public void ConstructorSetsNetworkName() {
		var client = new IrcClient(new("Alice", "alice", "\u00032Alice"), "Wonderland");
		Assert.AreEqual("Wonderland", client.Extensions.NetworkName);
	}

	[Test]
	public void PingTest() {
		// TODO: Need to work out a better way to test this. Depending on time is not very reliable. And slow.
		var client = new TestIrcClient() { PingTimeout = 1 };
		client.Connect();
		Thread.Sleep(1500);
		client.AssertLine(l => l.Message == PING);
		client.ReceivedLine("PONG :Keep-alive");
		Thread.Sleep(1000);
		client.AssertLine(l => l.Message == PING);
		Thread.Sleep(1000);
		client.AssertLine(l => l.Message == QUIT);
		Assert.AreEqual(DisconnectReason.PingTimeout, client.disconnectReason);
	}

	[Test]
	public void DecodeUnixTimeTest() {
		Assert.AreEqual(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), IrcClient.DecodeUnixTime(0));
		Assert.AreEqual(new DateTime(1969, 12, 31, 23, 59, 59, DateTimeKind.Utc), IrcClient.DecodeUnixTime(-1));
		Assert.AreEqual(new DateTime(2038, 1, 19, 3, 14, 8, DateTimeKind.Utc), IrcClient.DecodeUnixTime(2147483648));
	}

	[Test]
	public void EncodeUnixTimeTest() {
		Assert.AreEqual(0.0, IrcClient.EncodeUnixTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
		Assert.AreEqual(-1.0, IrcClient.EncodeUnixTime(new DateTime(1969, 12, 31, 23, 59, 59, DateTimeKind.Utc)));
		Assert.AreEqual(2147483648.0, IrcClient.EncodeUnixTime(new DateTime(2038, 1, 19, 3, 14, 8, DateTimeKind.Utc)));
	}

	[Test]
	public void SendBareCommand() {
		var stream = new MemoryStream(512);
		var client = new TestIrcClient(testRegistration: true, stream: stream);
		stream.Position = 0;
		client.SendBase(new IrcLine(QUIT));
		Assert.AreEqual($"{QUIT}\r\n", Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int) stream.Position));
	}
	[Test]
	public void SendWithParameters() {
		var stream = new MemoryStream(512);
		var client = new TestIrcClient(testRegistration: true, stream: stream);
		stream.Position = 0;
		client.SendBase(new IrcLine(CAP, "LS", "302"));
		Assert.AreEqual($"{CAP} LS 302\r\n", Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int) stream.Position));
	}
	[Test]
	public void SendWithSourceAndTrail() {
		var stream = new MemoryStream(512);
		var client = new TestIrcClient(testRegistration: true, stream: stream);
		stream.Position = 0;
		client.SendBase(new IrcLine(null, "Alice!alice@192.168.0.1", PRIVMSG, new[] { "#wonderland", "Hello world!" }));
		Assert.AreEqual($"{PRIVMSG} #wonderland :Hello world!\r\n", Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int) stream.Position));
	}
	[Test]
	public void SendWithTags() {
		var stream = new MemoryStream(512);
		var client = new TestIrcClient(testRegistration: true, stream: stream);
		stream.Position = 0;
		client.SendBase(new IrcLine(new KeyValuePair<string, string>[] { new("draft/react", "🐱"), new("reply", "123456") }, TAGMSG, "#wonderland"));
		Assert.AreEqual($"@draft/react=🐱;reply=123456 {TAGMSG} #wonderland\r\n", Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int) stream.Position));
	}
	[Test]
	public void SendWithTagsWithEscapedValues() {
		var stream = new MemoryStream(512);
		var client = new TestIrcClient(testRegistration: true, stream: stream);
		stream.Position = 0;
		client.SendBase(new IrcLine(new KeyValuePair<string, string>[] { new("draft/react", "🐱"), new("reply", "Test; \\\r\nTest") }, TAGMSG, "#wonderland"));
		Assert.AreEqual($@"@draft/react=🐱;reply=Test\:\s\\\r\nTest {TAGMSG} #wonderland{"\r\n"}", Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int) stream.Position));
	}

	[Test]
	public void RemoveCodesTest() {
		Assert.AreEqual("Hello world!", IrcClient.RemoveCodes("\x02\x16\x1E\x1FHello world!\x0F"));
		Assert.AreEqual("Should remove all colour codes 2.", IrcClient.RemoveCodes("\u000304Should \u00035remove \u0003all \u00036,0colour \u00037,99codes \u0003082."));
	}

	[Test]
	public void CaseMappingComparerTest() {
		var client = new TestIrcClient(new IrcLocalUser("[Alice]", "alice", "\u00032\u000FAlice"));
		client.Connect();

		// Should use RFC1459 case mapping by default ('^' is considered uppercase of '~'; same for "[]" and "{}").
		Assert.IsTrue(client.CaseMappingComparer.Equals("^", "~"));
		Assert.AreSame(client.Me, client.Users["{alice}"]);

		// Should switch to ASCII case mapping when specified by the server.
		client.ReceivedLine("005 [Alice] CASEMAPPING=ascii :are supported by this server");
		Assert.IsFalse(client.CaseMappingComparer.Equals("[", "{"));
		Assert.AreSame(client.Me, client.Users["[alice]"]);
		Assert.IsFalse(client.Users.Contains("{alice}"));
	}

	[Test]
	public void SplitMessageTest() {
		Assert.AreEqual("Hello\nworld!", string.Join('\n', IrcClient.SplitMessage("Hello world!", 11, Encoding.UTF8)));
		Assert.AreEqual("Hellowor\nld!", string.Join('\n', IrcClient.SplitMessage("Helloworld!", 8, Encoding.UTF8)));
		Assert.AreEqual("Hello world!", string.Join('\n', IrcClient.SplitMessage("Hello world!", 12, Encoding.UTF8)));
		Assert.AreEqual("Hello\n🙂", string.Join('\n', IrcClient.SplitMessage("Hello 🙂", 9, Encoding.UTF8)));  // Emojis are 4 bytes long in UTF-8.
		Assert.AreEqual("Hello 🙂", string.Join('\n', IrcClient.SplitMessage("Hello 🙂", 10, Encoding.UTF8)));
		Assert.AreEqual("Hello\nworld!", string.Join('\n', IrcClient.SplitMessage("Hello world!", 12, Encoding.Unicode)));
	}

	[Test]
	public void CaseMappingChange() {
		using var client = new TestIrcClient();
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "CASEMAPPING=ascii", "are supported by this server."));
		client.ReceivedLine(new IrcLine(null, "Alice!alice@192.168.0.1", JOIN, new[] { "#[wonderland]" }));
		client.ReceivedLine(new IrcLine(RPL_NAMREPLY, "Alice", "=", "#[wonderland]", "Alice [Bob]"));
		client.ReceivedLine(new IrcLine(RPL_ENDOFNAMES, "Alice", "#[wonderland]"));
		var wonderland = client.Channels["#[wonderland]"];
		var bob = client.Users["[Bob]"];

		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "CASEMAPPING=rfc1459", "are supported by this server."));
		Assert.AreSame(wonderland, client.Channels["#{wonderland}"]);
		Assert.AreSame(bob, client.Users["{Bob}"]);
		Assert.AreSame(wonderland, bob.Channels["#{wonderland}"]);
		Assert.IsTrue(wonderland.Users.Contains("{Bob}"));
	}

	[Test]
	public void CaseMappingChangeWithCollision() {
		using var client = new TestIrcClient();
		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "CASEMAPPING=ascii", "are supported by this server."));
		client.ReceivedLine(new IrcLine(null, "Alice!alice@192.168.0.1", JOIN, new[] { "#[wonderland]" }));
		client.ReceivedLine(new IrcLine(RPL_NAMREPLY, "Alice", "=", "#[wonderland]", "Alice [Bob] {Bob}"));
		client.ReceivedLine(new IrcLine(RPL_ENDOFNAMES, "Alice", "#[wonderland]"));

		client.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "CASEMAPPING=rfc1459", "are supported by this server."));
		client.AssertLine($"{QUIT} :{IrcClient.QUIT_MESSAGE_CASEMAPPING_COLLISION}");
	}
}
