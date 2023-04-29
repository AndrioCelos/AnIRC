using NUnit.Framework;

using System.Linq;

namespace AnIRC.Tests;

[TestFixture]
public class IrcExtensionsTests {
	[Test]
	public void TreatsNamesAsCaseSensitive() {
		var client = new TestIrcClient();
		client.Extensions["network"] = "Not Test";
		Assert.AreNotEqual("Not Test", client.Extensions.NetworkName);
		Assert.IsFalse(client.Extensions.ContainsKey("NETWORK"));
	}

	[Test]
	public void CaseMapping() {
		var client = new TestIrcClient();
		client.Extensions["CASEMAPPING"] = "ascii";
		Assert.AreEqual("ascii", client.Extensions.CaseMapping);
		Assert.IsFalse(client.CaseMappingComparer.Equals('[', '{'));
	}

	[Test]
	public void ChanLimit() {
		var client = new TestIrcClient();
		client.Extensions["CHANLIMIT"] = "#+:25,&:";
		Assert.AreEqual(2, client.Extensions.ChannelLimit.Count);
		Assert.AreEqual(25, client.Extensions.ChannelLimit["#+"]);
		Assert.AreEqual(int.MaxValue, client.Extensions.ChannelLimit["&"]);
	}

	[Test]
	public void ChanModes() {
		var client = new TestIrcClient();
		client.Extensions["CHANMODES"] = "beI,k,l,BCMNORScimnpstz";
		Assert.AreEqual("Ibe,k,l,BCMNORScimnpstz,ov", client.Extensions.ChanModes.ToString());
	}

	[Test]
	public void Excepts() {
		var client = new TestIrcClient();
		Assert.IsFalse(client.Extensions.SupportsBanExceptions);
		client.Extensions["EXCEPTS"] = "e";
		Assert.IsTrue(client.Extensions.SupportsBanExceptions);
		Assert.AreEqual('e', client.Extensions.BanExceptionsMode);

		client.Extensions.Remove("EXCEPTS");
		Assert.IsFalse(client.Extensions.SupportsBanExceptions);
	}

	[Test]
	public void InvEx() {
		var client = new TestIrcClient();
		Assert.IsFalse(client.Extensions.SupportsInviteExceptions);
		client.Extensions["INVEX"] = "I";
		Assert.IsTrue(client.Extensions.SupportsInviteExceptions);
		Assert.AreEqual('I', client.Extensions.InviteExceptionsMode);

		client.Extensions.Remove("INVEX");
		Assert.IsFalse(client.Extensions.SupportsInviteExceptions);
	}

	[Test]
	public void MaxBans() {
		var client = new TestIrcClient();
		client.Extensions["EXCEPTS"] = "e";
		client.Extensions["MAXBANS"] = "100";
		Assert.AreEqual("be:100", Extensions.JoinDictionary(client.Extensions.ListModeLength));
		client.Extensions.Remove("MAXBANS");
		Assert.AreEqual("", Extensions.JoinDictionary(client.Extensions.ListModeLength));
	}

	[Test]
	public void MaxChannels() {
		var client = new TestIrcClient();
		client.Extensions["CHANTYPES"] = "#~";
		client.Extensions["MAXCHANNELS"] = "50";
		Assert.AreEqual(50, client.Extensions.ChannelLimit["#~"]);
		client.Extensions.Remove("MAXCHANNELS");
		Assert.AreEqual("", Extensions.JoinDictionary(client.Extensions.ChannelLimit));
	}

	[Test]
	public void MaxList() {
		var client = new TestIrcClient();
		client.Extensions["MAXLIST"] = "b:25,eI:50";
		Assert.AreEqual("b:25,eI:50", Extensions.JoinDictionary(client.Extensions.ListModeLength));
	}

	[Test]
	public void Modes() {
		var client = new TestIrcClient();
		client.Extensions["MODES"] = "6";
		Assert.AreEqual(6, client.Extensions.Modes);
		client.Extensions["MODES"] = "";
		Assert.AreEqual(int.MaxValue, client.Extensions.Modes);
		client.Extensions.Remove("MODES");
		Assert.AreEqual(3, client.Extensions.Modes);
	}

	[Test]
	public void Monitor() {
		var client = new TestIrcClient();
		client.Extensions["MONITOR"] = "32";
		Assert.AreEqual(32, client.Extensions.MonitorLimit);
		Assert.IsTrue(client.Extensions.SupportsMonitor);
		client.Extensions["MONITOR"] = "";
		Assert.AreEqual(int.MaxValue, client.Extensions.MonitorLimit);
		client.Extensions.Remove("MONITOR");
		Assert.IsFalse(client.Extensions.SupportsMonitor);
	}

	[Test]
	public void MonitorWithWatch() {
		var client = new TestIrcClient();
		client.Extensions["WATCH"] = "100";
		client.Extensions["MONITOR"] = "32";
		Assert.AreEqual(32, client.Extensions.MonitorLimit);
		Assert.IsTrue(client.Extensions.SupportsMonitor);
		client.Extensions["MONITOR"] = "";
		Assert.AreEqual(int.MaxValue, client.Extensions.MonitorLimit);
		client.Extensions.Remove("MONITOR");
		Assert.AreEqual(100, client.Extensions.MonitorLimit);
		Assert.IsTrue(client.Extensions.SupportsMonitor);
	}

	[Test]
	public void Network() {
		var client = new TestIrcClient();
		client.Extensions["NETWORK"] = "Another Test";
		Assert.AreEqual("Another Test", client.NetworkName);
	}

	[Test]
	public void NickLen() {
		var client = new TestIrcClient();
		client.Extensions["NICKLEN"] = "16";
		Assert.AreEqual(16, client.Extensions.NicknameLength);
		client.Extensions.Remove("NICKLEN");
		Assert.AreEqual(9, client.Extensions.NicknameLength);
	}

	[Test]
	public void Prefix() {
		var client = new TestIrcClient();
		client.Extensions["PREFIX"] = "(Yodvq)!@*+-";
		Assert.AreEqual("Yodvq", string.Join(null, client.Extensions.ChanModes.Status));
		Assert.AreEqual("!:Y,*:d,+:v,-:q,@:o", Extensions.JoinDictionary(client.Extensions.StatusPrefix));
		Assert.AreEqual("aYohdvVq", string.Join(null, client.Extensions.allStatus));
		client.Extensions.Remove("PREFIX");
		Assert.AreEqual("ov", string.Join(null, client.Extensions.ChanModes.Status));
		Assert.AreEqual("+:v,@:o", Extensions.JoinDictionary(client.Extensions.StatusPrefix));
		Assert.AreEqual("qaohvV", string.Join(null, client.Extensions.allStatus));
	}

	[Test]
	public void TargMax() {
		var client = new TestIrcClient();
		client.Extensions["TARGMAX"] = "PRIVMSG:3,WHOIS:1,JOIN:";
		Assert.AreEqual("PRIVMSG:3,WHOIS:1", Extensions.JoinDictionary(client.Extensions.MaxTargets));
	}

	[Test]
	public void TopicLen() {
		var client = new TestIrcClient();
		client.Extensions["TOPICLEN"] = "120";
		Assert.AreEqual(120, client.Extensions.TopicLength);
		client.Extensions["TOPICLEN"] = "";
		Assert.AreEqual(int.MaxValue, client.Extensions.TopicLength);
	}

	[Test]
	public void Watch() {
		var client = new TestIrcClient();
		client.Extensions["WATCH"] = "100";
		Assert.AreEqual(100, client.Extensions.MonitorLimit);
		Assert.IsTrue(client.Extensions.SupportsMonitor);
		client.Extensions["WATCH"] = "";
		Assert.AreEqual(int.MaxValue, client.Extensions.MonitorLimit);
		client.Extensions.Remove("WATCH");
		Assert.IsFalse(client.Extensions.SupportsMonitor);
	}

	[Test]
	public void WatchWithMonitor() {
		// MONITOR always takes precedence over WATCH.
		var client = new TestIrcClient();
		client.Extensions["MONITOR"] = "32";
		client.Extensions["WATCH"] = "100";
		Assert.AreEqual(32, client.Extensions.MonitorLimit);
		Assert.IsTrue(client.Extensions.SupportsMonitor);
		client.Extensions["WATCH"] = "";
		Assert.AreEqual(32, client.Extensions.MonitorLimit);
		client.Extensions.Remove("WATCH");
		Assert.AreEqual(32, client.Extensions.MonitorLimit);
		Assert.IsTrue(client.Extensions.SupportsMonitor);
	}

	[Test]
	public void WhoX() {
		var client = new TestIrcClient();
		client.Extensions["WHOX"] = "";
		Assert.IsTrue(client.Extensions.SupportsWhox);
		client.Extensions.Remove("WHOX");
		Assert.IsFalse(client.Extensions.SupportsWhox);
	}
}
