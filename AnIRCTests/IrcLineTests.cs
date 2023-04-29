using System;
using System.Text;

using NUnit.Framework;

using static AnIRC.Replies;

namespace AnIRC.Tests;

[TestFixture]
public class IrcLineTests {
	[Test]
	public void ParseBareCommand() {
		var line = IrcLine.Parse(QUIT);
		Assert.IsEmpty(line.Tags);
		Assert.IsNull(line.Source);
		Assert.AreEqual(QUIT, line.Message);
		Assert.IsEmpty(line.Parameters);
	}
	[Test]
	public void ParseWithParameters() {
		var line = IrcLine.Parse($"{CAP} LS 302");
		Assert.IsEmpty(line.Tags);
		Assert.IsNull(line.Source);
		Assert.AreEqual(CAP, line.Message);
		Assert.AreEqual("LS\n302", string.Join('\n', line.Parameters));
	}
	[Test]
	public void ParseWithSourceAndTrail() {
		var line = IrcLine.Parse($":Alice!alice@192.168.0.1 {PRIVMSG} #wonderland :Hello world!");
		Assert.IsEmpty(line.Tags);
		Assert.AreEqual("Alice!alice@192.168.0.1", line.Source);
		Assert.AreEqual(PRIVMSG, line.Message);
		Assert.AreEqual("#wonderland\nHello world!", string.Join('\n', line.Parameters));
	}
	[Test]
	public void ParseWithTags() {
		var line = IrcLine.Parse($"@time=2021-01-01T12:34:56.789Z;account=Alice :Alice!alice@192.168.0.1 {JOIN} #wonderland");
		Assert.AreEqual("account:Alice,time:2021-01-01T12:34:56.789Z", Extensions.JoinDictionary(line.Tags));
		Assert.AreEqual("Alice!alice@192.168.0.1", line.Source);
		Assert.AreEqual(JOIN, line.Message);
		Assert.AreEqual("#wonderland", string.Join('\n', line.Parameters));
	}
	[Test]
	public void ParseWithTagsWithEscapedValues() {
		var line = IrcLine.Parse(@$"@andriocelos.net/test-1=Test\:\s\\\rTest\n\b\;andriocelos.net/test-2=Test\sTest\ PING");
		Assert.AreEqual("andriocelos.net/test-1:Test; \\\rTest\nb,andriocelos.net/test-2:Test Test", Extensions.JoinDictionary(line.Tags));
	}

	[Test]
	public void FromBinaryBareCommand() {
		var line = IrcLine.FromBinary(Encoding.UTF8.GetBytes(QUIT), Encoding.UTF8);
		Assert.IsEmpty(line.Tags);
		Assert.IsNull(line.Source);
		Assert.AreEqual(QUIT, line.Message);
		Assert.IsEmpty(line.Parameters);
	}
	[Test]
	public void FromBinaryWithParameters() {
		var line = IrcLine.FromBinary(Encoding.UTF8.GetBytes($"{CAP} LS 302"), Encoding.UTF8);
		Assert.IsEmpty(line.Tags);
		Assert.IsNull(line.Source);
		Assert.AreEqual(CAP, line.Message);
		Assert.AreEqual("LS\n302", string.Join('\n', line.Parameters));
	}
	[Test]
	public void FromBinaryWithSourceAndTrail() {
		var line = IrcLine.FromBinary(Encoding.UTF8.GetBytes($":Alice!alice@192.168.0.1 {PRIVMSG} #wonderland :Hello world!"), Encoding.UTF8);
		Assert.IsEmpty(line.Tags);
		Assert.AreEqual("Alice!alice@192.168.0.1", line.Source);
		Assert.AreEqual(PRIVMSG, line.Message);
		Assert.AreEqual("#wonderland\nHello world!", string.Join('\n', line.Parameters));
	}
	[Test]
	public void FromBinaryWithTags() {
		var line = IrcLine.FromBinary(Encoding.UTF8.GetBytes($"@time=2021-01-01T12:34:56.789Z;account=Alice :Alice!alice@192.168.0.1 {JOIN} #wonderland"), Encoding.UTF8);
		Assert.AreEqual("account:Alice,time:2021-01-01T12:34:56.789Z", Extensions.JoinDictionary(line.Tags));
		Assert.AreEqual("Alice!alice@192.168.0.1", line.Source);
		Assert.AreEqual(JOIN, line.Message);
		Assert.AreEqual("#wonderland", string.Join('\n', line.Parameters));
	}
	[Test]
	public void FromBinaryWithTagsWithEscapedValues() {
		var line = IrcLine.FromBinary(Encoding.UTF8.GetBytes(@$"@andriocelos.net/test-1=Test\:\s\\\rTest\n\b\;andriocelos.net/test-2=Test\sTest\ PING"), Encoding.UTF8);
		Assert.AreEqual("andriocelos.net/test-1:Test; \\\rTest\nb,andriocelos.net/test-2:Test Test", Extensions.JoinDictionary(line.Tags));
	}
	[Test]
	public void FromBinaryWithInvalidUtf8() {
		var line = IrcLine.FromBinary(new byte[] { (byte) 'P', (byte) 'I', (byte) 'N', (byte) 'G', (byte) ' ', 0xE1, 0xA0, 0xC0, (byte) ' ', (byte) ':', 0xFF, (byte) '!'  }, Encoding.UTF8);
		Assert.AreEqual("��\n�!", string.Join('\n', line.Parameters));
	}
	[Test]
	public void FromBinaryWithNonUtf8Encoding() {
		const string message = "This is not UTF-8.";
		var stream = new System.IO.MemoryStream(1024);
		stream.Write(Encoding.UTF8.GetBytes($"@andriocelos.net/test-utf8=✓ :Alice!alice@192.168.0.1 {PRIVMSG} "));
		stream.Write(Encoding.Unicode.GetBytes("#wonderland"));
		stream.WriteByte((byte) ' ');
		stream.WriteByte((byte) ':');
		stream.Write(Encoding.Unicode.GetBytes(message));
		var line = IrcLine.FromBinary(stream.GetBuffer().AsSpan(0, (int) stream.Length), Encoding.Unicode);
		Assert.AreEqual("andriocelos.net/test-utf8:✓", Extensions.JoinDictionary(line.Tags));
		Assert.AreEqual($"#wonderland\n{message}", string.Join('\n', line.Parameters));
	}
}
