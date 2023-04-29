using NUnit.Framework;

using System.Linq;

namespace AnIRC.Tests;

[TestFixture()]
public class ChannelStatusTests {
	private static TestIrcClient PrepareTestClient() {
		var client = new TestIrcClient();
		client.Connect();
		client.ReceivedLine(":127.0.0.1 005 Alice CHANMODES=bq,k,l,n EXCEPTS=e INVEX=I MODES=4 PREFIX=(odv)@*+");
		client.ReceivedLine(":Alice!alice@127.0.0.1 JOIN #wonderland");
		client.ReceivedLine(":Alice!alice@127.0.0.1 353 Alice = #wonderland :Frank Erin +Dan *Carol @Bob Alice");
		return client;
	}

	[Test]
	public void ChannelStatusTest() {
		Assert.AreSame(ChannelStatus.Voice, ChannelStatus.Voice);
		Assert.AreEqual("v", ChannelStatus.Voice.ToString());

		Assert.AreSame(ChannelStatus.Halfop, ChannelStatus.Halfop);
		Assert.AreEqual("h", ChannelStatus.Halfop.ToString());

		Assert.AreSame(ChannelStatus.Op, ChannelStatus.Op);
		Assert.AreEqual("o", ChannelStatus.Op.ToString());

		Assert.AreSame(ChannelStatus.Admin, ChannelStatus.Admin);
		Assert.AreEqual("a", ChannelStatus.Admin.ToString());

		Assert.AreSame(ChannelStatus.Owner, ChannelStatus.Owner);
		Assert.AreEqual("q", ChannelStatus.Owner.ToString());
	}

	[Test]
	public void FromPrefixTest() {
		var client = PrepareTestClient();

		// Test with non-standard modes
		var status = ChannelStatus.FromPrefix(client, "*@");
		Assert.AreEqual("od", status.ToString());

		// Test with empty set
		status = ChannelStatus.FromPrefix(client, Enumerable.Empty<char>());
		Assert.AreEqual("", status.ToString());

		// Test with no IRCClient
		status = ChannelStatus.FromPrefix(null, "&@");
		Assert.AreEqual("o", status.ToString());

		// Test with no IRCClient and non-standard prefixes; these will be ignored.
		status = ChannelStatus.FromPrefix(null, "*");
		Assert.AreEqual("", status.ToString());
	}

	[Test]
	public void CompareToTest() {
		var client = PrepareTestClient();

		// o is equal to ov
		Assert.AreEqual(true, ChannelStatus.Op.Equals(new ChannelStatus(client, "ov")));

		// o is greater than h (even if that mode doesn't exist)
		Assert.IsTrue(new ChannelStatus(client, "o") > ChannelStatus.Halfop);

		// v is less than h (even if that mode doesn't exist)
		Assert.IsTrue(new ChannelStatus(client, "v") < ChannelStatus.Halfop);

		// d is greater than v.
		Assert.IsTrue(new ChannelStatus(client, "d") > ChannelStatus.Voice);

		// d is less than o.
		Assert.IsTrue(new ChannelStatus(client, "d") < ChannelStatus.Op);

		// d is also less than h as h doesn't exist.
		Assert.IsTrue(new ChannelStatus(client, "d") < ChannelStatus.Halfop);

		// The empty set is greater than null.
		Assert.IsTrue(new ChannelStatus(client, Enumerable.Empty<char>()) > null);
	}

	[Test]
	public void GetPrefixesTest() {
		var client = PrepareTestClient();

		Assert.AreEqual("@*+", new ChannelStatus(client, "vdo").GetPrefixes());
		Assert.AreEqual("", new ChannelStatus(null, Enumerable.Empty<char>()).GetPrefixes());
	}
}
