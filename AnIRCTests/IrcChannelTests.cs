using NUnit.Framework;

using System;

namespace AnIRC.Tests;

[TestFixture]
public class IrcChannelTests {
	[Test]
	public void SetModeTest() {
		using var client = new TestIrcClient(autoConnect: true);
		var wonderland = new IrcChannel(client, "#wonderland");
		wonderland.SetMode('+', 'v', "Alice", "Bob", "Carol");
		client.AssertLine("MODE #wonderland +vvv Alice Bob Carol");
	}
	[Test]
	public void SetModeTestWithManyTargets() {
		using var client = new TestIrcClient(autoConnect: true);
		var wonderland = new IrcChannel(client, "#wonderland");
		wonderland.SetMode('+', 'v', "Alice", "Bob", "Carol", "Dan", "Erin");
		client.AssertLine("MODE #wonderland +vvv Alice Bob Carol");
		client.AssertLine("MODE #wonderland +vv Dan Erin");
	}
	[Test]
	public void SetModeTestWithNoTargets() {
		using var client = new TestIrcClient(autoConnect: true);
		var wonderland = new IrcChannel(client, "#wonderland");
		wonderland.SetMode('+', 'v', Array.Empty<string>());
	}
}
