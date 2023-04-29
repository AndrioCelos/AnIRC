using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

using NUnit.Framework;

using static AnIRC.Replies;

namespace AnIRC.Tests;

internal class TestIrcClient : IrcClient, IDisposable {
	private readonly Queue<IrcLine> sentLines = new();
	private readonly Queue<(string eventName, EventArgs e)> sentEvents = new();
	private new readonly Stream stream;

	internal EventHandler HandleEvent(string eventName)
		=> new(delegate (object? sender, EventArgs e) { sentEvents.Enqueue((eventName, e)); });
	internal EventHandler<TEventArgs> HandleEvent<TEventArgs>(string eventName) where TEventArgs : EventArgs
		=> new(delegate (object? sender, TEventArgs e) { sentEvents.Enqueue((eventName, e)); });

	internal static EventHandler HandleInvalidEvent(string eventName)
		=> new((sender, e) => Assert.Fail($"An invalid event was raised: {eventName}"));
	internal static EventHandler<TEventArgs> HandleInvalidEvent<TEventArgs>(string eventName) where TEventArgs : EventArgs
		=> new((sender, e) => Assert.Fail($"An invalid event was raised: {eventName}"));

	/// <summary>Asserts that the next recorded event raised was the specified event.</summary>
	public EventArgs AssertEvent(string eventName) => this.AssertEvent<EventArgs>(eventName);
	/// <summary>Asserts that the next recorded event raised was the specified event and returns the associated <see cref="EventArgs"/>.</summary>
	public TEventArgs AssertEvent<TEventArgs>(string eventName) where TEventArgs : EventArgs {
		if (sentEvents.Count == 0) Assert.Fail($"The event {eventName} was never raised.");

		var entry = sentEvents.Dequeue();
		Assert.AreEqual(eventName, entry.eventName, "The wrong event was raised. Expected:<{0}>. Actual:<{1}>.", eventName, entry.eventName);
		return (TEventArgs) entry.e;
	}

	public void AssertEndOfEvents() {
		if (this.sentEvents.TryDequeue(out var actualEvent)) Assert.Fail($"An unexpected event was raised: {actualEvent.eventName}");
	}

	public bool TestRegistration { get; }
	public bool VerifySentLines { get; set; } = true;

	internal TestIrcClient(IrcLocalUser? localUser = null, bool autoConnect = true, bool enableSaslInPlaintext = true, bool testRegistration = false, TlsMode tls = TlsMode.Plaintext, Stream? stream = null)
		: base(localUser ?? new IrcLocalUser("Alice", "alice", "Test User"), "Test Network") {
		if (stream is not null && !testRegistration) throw new ArgumentException($"Setting {nameof(stream)} requires {nameof(testRegistration)}.");
		this.Address = "localhost";
		this.Tls = tls;
		this.PingTimeout = 0;
		this.stream = stream ?? Stream.Null;
		this.TestRegistration = testRegistration;
		if (enableSaslInPlaintext) {
			this.SaslMechanisms.Clear();
			this.SaslMechanisms.Add(SaslMechanism.External);
			this.SaslMechanisms.Add(SaslMechanism.Plain);
		}
		if (autoConnect) this.Connect();
	}

	public new void Dispose() {
		if (this.VerifySentLines) this.AssertEndOfLines();
		this.AssertEndOfEvents();
		this.VerifySentLines = false;
		base.Dispose();
	}

	internal void GetTestChannel(out IrcChannel channel, out IrcUser user, string names = "Alice @Bob", string channelName = "#wonderland") {
		this.ReceivedLine(new IrcLine(null, "Alice!alice@in.wonderland", JOIN, new[] { channelName }));
		this.ReceivedLine(new IrcLine(RPL_NAMREPLY, "Alice", "=", channelName, names));
		this.ReceivedLine(new IrcLine(RPL_ENDOFNAMES, "Alice", channelName));
		if (!this.Channels.TryGetValue(channelName, out channel!)) throw new InconclusiveException("Channel not found in test arrangement");
		if (!this.Users.TryGetValue("Bob", out user!)) throw new InconclusiveException("User not found in test arrangement");
	}
	internal void GetTestUser(out IrcUser user) {
		this.ReceivedLine(new IrcLine(RPL_ISUPPORT, "Alice", "MONITOR=8", "are supported by this server"));
		this.ReceivedLine(new IrcLine(RPL_MONONLINE, "*", "Bob"));
		if (!this.Users.TryGetValue("Bob", out user!)) throw new InconclusiveException("Monitored user not found in test arrangement");
	}

	internal void AssertLine(string expectedLine) {
		if (!this.sentLines.TryDequeue(out var actualLine)) { Assert.Fail($"An expected line was not sent: {expectedLine}"); return; }
		Assert.AreEqual(expectedLine, actualLine.ToString());
	}
	internal void AssertLine(IrcLine expectedLine) => this.AssertLine(expectedLine.ToString());
	internal void AssertLine(Predicate<IrcLine> predicate) {
		if (!this.sentLines.TryDequeue(out var actualLine)) { Assert.Fail($"An expected line was not sent."); return; }
		if (!predicate(actualLine)) Assert.Fail($"The sent line did not match the expected predicate. Actual:<{actualLine}>.");
	}

	internal void AssertEndOfLines() {
		if (this.sentLines.TryDequeue(out var actualLine)) Assert.Fail($"An unexpected line was sent: {actualLine}");
	}

	protected override void InitialiseTcpClient(string address, int port) {
		if (this.TestRegistration) this.OnConnected(null!);
		else this.State = IrcClientState.Online;
	}
	protected override void InitialiseTcpClient(IPAddress address, int port) {
		if (this.TestRegistration) this.OnConnected(null!);
		else this.State = IrcClientState.Online;
	}
	protected override Stream GetBaseStream(TcpClient? tcpClient, IAsyncResult? result) => this.stream;
	protected override void ReadLoop() { }

	public void ReceivedLine(IrcLine line, string? expectedClientResponse) {
		this.ReceivedLine(line);
		if (expectedClientResponse is not null) this.AssertLine(expectedClientResponse);
	}
	public void ReceivedLine(IrcLine line, Predicate<IrcLine> expectedClientResponse) {
		this.ReceivedLine(line);
		if (expectedClientResponse is not null) this.AssertLine(expectedClientResponse);
	}

	public void Connect() => this.Connect("localhost", 6667);

	protected override void StartRead() { }

	public override void Send(IrcLine line) {
		if (this.VerifySentLines) this.sentLines.Enqueue(line);
	}
	internal void SendBase(IrcLine line) => base.Send(line);
}

internal class TestSaslMechanism : SaslMechanism {
	private readonly string? expectedString;
	private static readonly object testStateObject = new();
	private readonly ArraySegment<byte>? response;

	public override string Name => "TEST";

	public TestSaslMechanism(string? expectedString, ArraySegment<byte>? response) {
		this.expectedString = expectedString;
		this.response = response;
	}

	public override bool CanAttempt(IrcClient ircClient) => true;
	public override object? Initialise(IrcClient ircClient) => testStateObject;
	public override ArraySegment<byte>? Respond(IrcClient client, ArraySegment<byte> data, object? state) {
		Assert.AreSame(testStateObject, state, $"{nameof(SaslMechanism)} state object was not persisted correctly.");
		if (expectedString is not null) {
			if (expectedString == "")
				Assert.AreEqual(0, data.Count);
			else
				Assert.AreEqual(expectedString, Encoding.UTF8.GetString(data.Array!, data.Offset, data.Count));
		}
		return this.response;
	}
}
