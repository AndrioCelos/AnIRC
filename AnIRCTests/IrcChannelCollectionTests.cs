using NUnit.Framework;

namespace AnIRC.Tests;

[TestFixture]
public class IrcChannelCollectionTests {
	[Test]
	public void GetUnknownChannel() {
		var client = new TestIrcClient(autoConnect: true);
		var subject = new IrcChannelCollection(client);
		var channel = subject.Get("#wonderland");
		Assert.AreEqual(0, subject.Count);
		Assert.AreEqual("#wonderland", channel.Name);
	}
	[Test]
	public void GetKnownChannel() {
		var client = new TestIrcClient(autoConnect: true);
		var subject = new IrcChannelCollection(client);
		var channel = new IrcChannel(client, "#wonderland");
		subject.Add(channel);
		var channel2 = subject.Get("#wonderland");
		Assert.AreEqual(1, subject.Count);
		Assert.AreSame(channel, channel2);
	}
}
