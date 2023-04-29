using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace AnIRC.Tests;

[TestFixture]
public class IrcStringComparerTests {
	[Test]
	public void EqualsNullEqualsNull() => Assert.IsTrue(IrcStringComparer.ASCII.Equals(null, null));
	[Test]
	public void EqualsNullDoesNotEqualEmptyString() => Assert.IsFalse(IrcStringComparer.ASCII.Equals(null, ""));
	[Test]
	public void EqualsSameLength() => Assert.IsTrue(IrcStringComparer.ASCII.Equals("AZaz", "azAZ"));
	[Test]
	public void EqualsDifferentLengths() {
		Assert.IsFalse(IrcStringComparer.ASCII.Equals("AZaz", "azAZ["));
		Assert.IsFalse(IrcStringComparer.ASCII.Equals("AZaz{", "azAZ"));
	}

	[Test]
	public void EqualsASCII() => Assert.IsFalse(IrcStringComparer.ASCII.Equals("[", "{"));
	[Test]
	public void EqualsRFC1459() {
		Assert.IsTrue(IrcStringComparer.RFC1459.Equals(@"[\]^", "{|}~"));
		Assert.IsFalse(IrcStringComparer.RFC1459.Equals(@"_", "\u007F"));
	}
	[Test]
	public void EqualsStrictRFC1459() {
		Assert.IsTrue(IrcStringComparer.StrictRFC1459.Equals(@"[\]", "{|}"));
		Assert.IsFalse(IrcStringComparer.StrictRFC1459.Equals(@"[\]^", "{|}~"));
	}

	[Test]
	public void GetHashCodeTest() {
		// Strings that are equal should map to the same hash. Strings that are not equal need not.
		Assert.AreEqual(IrcStringComparer.ASCII.GetHashCode("AZaz"), IrcStringComparer.ASCII.GetHashCode("azAZ"));
		Assert.AreNotEqual(IrcStringComparer.ASCII.GetHashCode("AZaz"), IrcStringComparer.ASCII.GetHashCode("azAZ["));
		Assert.AreNotEqual(IrcStringComparer.ASCII.GetHashCode("AZaz{"), IrcStringComparer.ASCII.GetHashCode("azAZ"));
	}

	[Test]
	public void GetHashCodeASCII() => Assert.AreNotEqual(IrcStringComparer.ASCII.GetHashCode("["), IrcStringComparer.ASCII.GetHashCode("{"));
	[Test]
	public void GetHashCodeRFC1459() {
		Assert.AreEqual(IrcStringComparer.RFC1459.GetHashCode(@"[\]^"), IrcStringComparer.RFC1459.GetHashCode("{|}~"));
		Assert.AreNotEqual(IrcStringComparer.RFC1459.GetHashCode(@"_"), IrcStringComparer.RFC1459.GetHashCode("\u007F"));
	}
	[Test]
	public void GetHashCodeStrictRFC1459() {
		Assert.AreEqual(IrcStringComparer.StrictRFC1459.GetHashCode(@"[\]"), IrcStringComparer.StrictRFC1459.GetHashCode("{|}"));
		Assert.AreNotEqual(IrcStringComparer.StrictRFC1459.GetHashCode(@"[\]^"), IrcStringComparer.StrictRFC1459.GetHashCode("{|}~"));
	}

	[Test]
	public void CompareNull() {
		// Any string is greater than null.
		Assert.ByVal(IrcStringComparer.ASCII.Compare(null, null), new EqualConstraint(0));
		Assert.ByVal(IrcStringComparer.ASCII.Compare("", null), new GreaterThanConstraint(0));
		Assert.ByVal(IrcStringComparer.ASCII.Compare(null, ""), new LessThanConstraint(0));
	}

	[Test] public void CompareCaseSensitivity() => Assert.ByVal(IrcStringComparer.ASCII.Compare("ABCabc", "abcABC"), new EqualConstraint(0));
	[Test]
	public void ComparePrefix() {
		Assert.ByVal(IrcStringComparer.ASCII.Compare("1234", "123"), new EqualConstraint(1));
		Assert.ByVal(IrcStringComparer.ASCII.Compare("123", "1234"), new EqualConstraint(-1));
		Assert.ByVal(IrcStringComparer.ASCII.Compare("13", "123"), new EqualConstraint(1));
		Assert.ByVal(IrcStringComparer.ASCII.Compare("123", "13"), new EqualConstraint(-1));
	}
	[Test]
	public void CompareASCII() {
		Assert.ByVal(IrcStringComparer.ASCII.Compare("AZaz", "azAZ"), new EqualConstraint(0));
		Assert.ByVal(IrcStringComparer.ASCII.Compare("AZaz[", "azAZ{"), new LessThanConstraint(0));
		Assert.ByVal(IrcStringComparer.ASCII.Compare("AZaz{", "azAZ["), new GreaterThanConstraint(0));
	}
	[Test]
	public void CompareRFC1459() {
		Assert.ByVal(IrcStringComparer.RFC1459.Compare(@"AZaz[\]^", "azAZ{|}~"), new EqualConstraint(0));
		Assert.ByVal(IrcStringComparer.RFC1459.Compare(@"AZaz[\]^_", "azAZ{|}~\u007F"), new LessThanConstraint(0));
	}
	[Test]
	public void CompareStrictRFC1459() {
		Assert.ByVal(IrcStringComparer.StrictRFC1459.Compare(@"AZaz[\]", "azAZ{|}"), new EqualConstraint(0));
		Assert.ByVal(IrcStringComparer.StrictRFC1459.Compare(@"AZaz[\]^", "azAZ{|}~"), new LessThanConstraint(0));
		Assert.ByVal(IrcStringComparer.StrictRFC1459.Compare("AZaz{|}~", @"azAZ[\]^"), new GreaterThanConstraint(0));
	}

	[Test] public void ToLowerASCII() => Assert.AreEqual(@"@az[\]^", IrcStringComparer.ASCII.ToLower(@"@AZ[\]^"));
	[Test] public void ToLowerRFC1459() => Assert.AreEqual(@"@az{|}~", IrcStringComparer.RFC1459.ToLower(@"@AZ[\]^"));
	[Test] public void ToLowerStrictRFC1459() => Assert.AreEqual(@"@az{|}^", IrcStringComparer.StrictRFC1459.ToLower(@"@AZ[\]^"));

	[Test] public void ToUpperASCII() => Assert.AreEqual("`AZ{|}~", IrcStringComparer.ASCII.ToUpper("`az{|}~"));
	[Test] public void ToUpperRFC1459() => Assert.AreEqual(@"`AZ[\]^", IrcStringComparer.RFC1459.ToUpper("`az{|}~"));
	[Test] public void ToUpperStrictRFC1459() => Assert.AreEqual(@"`AZ[\]~", IrcStringComparer.StrictRFC1459.ToUpper("`az{|}~"));
}
