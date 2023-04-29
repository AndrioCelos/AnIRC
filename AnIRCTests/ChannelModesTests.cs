
using NUnit.Framework;

namespace AnIRC.Tests;

[TestFixture]
public class ChannelModesTests {
	[Test]
	public void SetStatusModesTest() {
		var subject = new ChannelModes("b", "k", "l", "imnpst", "qaov");
		var newStatusModes = new[] { 'o', 'h', 'v' };
		subject.SetStatusModes(newStatusModes);
		Assert.AreEqual('\0', subject.ModeType('q'));
		Assert.AreEqual('\0', subject.ModeType('a'));
		Assert.AreEqual('S', subject.ModeType('o'));
		Assert.AreEqual('S', subject.ModeType('h'));
		Assert.AreEqual('S', subject.ModeType('v'));
		// Status is ordered; TypeA~TypeD do not guarantee the order.
		Assert.AreEqual(string.Join("", newStatusModes), string.Join("", subject.Status));
	}

	[Test]
	public void ToStringTest() => Assert.AreEqual("Ibe,k,l,aimnpqrst,ov", ChannelModes.RFC2811.ToString());
}
