using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AnIRC;

/// <summary>
/// Provides methods that help deal with hostmasks.
/// </summary>
public static class Hostmask {
	/// <summary>Determines whether the specified string matches the specified glob pattern case-insensitively.</summary>
	/// <param name="input">The string to check.</param>
	/// <param name="pattern">The pattern to match against. '*' matches any sequence of zero or more characters, and '?' matches any single character.</param>
	// TODO: Use the actual case mapping comparer from the IRC client.
	public static bool Matches(string input, string pattern) {
		var regexBuilder = new StringBuilder();
		regexBuilder.Append('^');
		foreach (char c in pattern) {
			switch (c) {
				case '*': regexBuilder.Append(".*"); break;
				case '?': regexBuilder.Append('.'); break;
				case '\\': case '+': case '|': case '{': case '[': case '(': case ')': case '^': case '$': case '.':
					regexBuilder.Append('\\'); regexBuilder.Append(c); break;
				default: regexBuilder.Append(c); break;
			}
		}
		regexBuilder.Append('$');

		return Regex.IsMatch(input, regexBuilder.ToString(), RegexOptions.IgnoreCase);
	}

	/// <summary>Returns the nickname part of the specified hostmask.</summary>
	public static string GetNickname(string mask) {
		var pos = mask.IndexOf('!');
		if (pos >= 0) return mask[..pos];
		pos = mask.IndexOf('@');
		return pos < 0 ? mask : mask[..pos];
	}

	/// <summary>Returns the ident part of the specified hostmask.</summary>
	public static string GetIdent(string mask) {
		var pos = mask.IndexOf('!');
		if (pos < 0 || pos == mask.Length - 1) return "*";
		++pos;

		var pos2 = mask.IndexOf('@', pos);
		return pos2 < 0 ? mask[pos..] : mask[pos..pos2];
	}

	/// <summary>Returns the host part of the specified hostmask.</summary>
	public static string GetHost(string mask) {
		var pos = mask.IndexOf('@');
		return pos < 0 ? "*" : mask[(pos + 1)..];
	}
}
