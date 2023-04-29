using NUnit.Framework;

namespace AnIRC.Tests;

[TestFixture]
public class HostmaskTests {
	[Test]
	public void MatchesExactMatch() => Assert.IsTrue(Hostmask.Matches("Alice!alice@192.168.0.1", "Alice!alice@192.168.0.1"));
	[Test]
	public void MatchesHostOnly() => Assert.IsTrue(Hostmask.Matches("Alice!alice@192.168.0.1", "*!*@192.168.0.1"));
	[Test]
	public void MatchesPartialHostNoDelimiter() => Assert.IsTrue(Hostmask.Matches("Alice!alice@192.168.0.1", "*192.168.*"));
	[Test]
	public void MatchesStarsMatchEmptyString() => Assert.IsTrue(Hostmask.Matches("", "***"));
	[Test]
	public void MatchesStarsNegativeTest() => Assert.IsFalse(Hostmask.Matches("Alice", "A*a"));
	[Test]
	public void MatchesNegativeExactMatch() => Assert.IsFalse(Hostmask.Matches("Alice1", "Alice"));
	[Test]
	public void MatchesQuestionMarkMatchesCharacter() => Assert.IsTrue(Hostmask.Matches("Alice1", "Alice?"));
	[Test]
	public void MatchesQuestionMarksDoNotMatchCharacter() => Assert.IsFalse(Hostmask.Matches("Alice1", "Alice??"));
	[Test]
	public void MatchesQuestionMarkDoesNotMatchEmptyString() => Assert.IsFalse(Hostmask.Matches("", "?"));

	[Test]
	public void GetNicknameWithFullHostmask() => Assert.AreEqual("Alice", Hostmask.GetNickname("Alice!alice@192.168.0.1"));
	[Test]
	public void GetNicknameWithNicknameOnly() => Assert.AreEqual("Alice", Hostmask.GetNickname("Alice"));
	[Test]
	public void GetNicknameWithNicknameAndHostOnly() => Assert.AreEqual("Alice", Hostmask.GetNickname("Alice@192.168.0.1"));
	[Test]
	public void GetNicknameWithEmptyString() => Assert.AreEqual("", Hostmask.GetNickname(""));

	[Test]
	public void GetIdentWithFullHostmask() => Assert.AreEqual("alice", Hostmask.GetIdent("Alice!alice@192.168.0.1"));
	[Test]
	public void GetIdentWithNicknameOnly() => Assert.AreEqual("*", Hostmask.GetIdent("Alice"));
	[Test]
	public void GetIdentWithNicknameAndHostOnly() => Assert.AreEqual("*", Hostmask.GetIdent("Alice@192.168.0.1"));
	[Test]
	public void GetIdentWithEmptyString() => Assert.AreEqual("*", Hostmask.GetIdent(""));

	[Test]
	public void GetHostWithFullHostmask() => Assert.AreEqual("192.168.0.1", Hostmask.GetHost("Alice!alice@192.168.0.1"));
	[Test]
	public void GetHostWithNicknameOnly() => Assert.AreEqual("*", Hostmask.GetHost("Alice"));
	[Test]
	public void GetHostWithNicknameAndHostOnly() => Assert.AreEqual("192.168.0.1", Hostmask.GetHost("Alice@192.168.0.1"));
	[Test]
	public void GetHostWithEmptyString() => Assert.AreEqual("*", Hostmask.GetHost(""));
}
