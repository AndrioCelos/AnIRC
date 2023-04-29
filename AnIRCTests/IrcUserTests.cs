using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NUnit.Framework;

namespace AnIRC.Tests;

[TestFixture]
public class IrcUserTests {
	[Test]
	public void IsServerWithServer()
		=> Assert.IsTrue(new IrcUser(new TestIrcClient(autoConnect: false), "test.irc.example.com", "*", "*", null, "test.irc.example.com").IsServer);
	[Test]
	public void IsServerWithUser()
		=> Assert.IsFalse(new IrcUser(new TestIrcClient(autoConnect: false), "Alice", "*", "*", null, "Alice").IsServer);
	[Test]
	public void IsServerWithUserImpersonatingServer()
		=> Assert.IsFalse(new IrcUser(new TestIrcClient(autoConnect: false), "mallory.me", "mallory", "192.168.6.66", null, "mallory.me").IsServer);
}
