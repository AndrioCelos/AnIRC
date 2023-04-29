using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace AnIRC;

/// <summary>
/// Represents an IRC message, and provides methods to help parse them.
/// </summary>
public class IrcLine {
	private static readonly IReadOnlyDictionary<string, string> EmptyTags = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

	[ThreadStatic]
	private static List<string>? cacheParameters;
	[ThreadStatic]
	private static StringBuilder? cacheStringBuilder;

	/// <summary>A dictionary of IRCv3 tags in this message, or an empty dictionary if no tags are present.</summary>
	public IReadOnlyDictionary<string, string> Tags { get; }
	/// <summary>The source prefix of this message, or null if no prefix is present.</summary>
	public string? Source { get; }
	/// <summary>The message or command.</summary>
	public string Message { get; }
	/// <summary>The list of parameters in this message.</summary>
	public IReadOnlyList<string> Parameters { get; }

	public IrcLine(string message, params string[]? parameters) : this(null, null, message, (IEnumerable<string>?) parameters) { }
	public IrcLine(string message, IEnumerable<string>? parameters) : this(null, null, message, parameters) { }
	public IrcLine(IEnumerable<KeyValuePair<string, string>>? tags, string message, params string[]? parameters) : this(tags, null, message, parameters) { }
	public IrcLine(IEnumerable<KeyValuePair<string, string>>? tags, string message, IEnumerable<string>? parameters) : this(tags, null, message, parameters) { }
		
	public IrcLine(IEnumerable<KeyValuePair<string, string>>? tags, string? source, string message, IEnumerable<string>? parameters)
		: this(tags is not null
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
			  ? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(tags.Select(e => new KeyValuePair<string, string>(e.Key, e.Value ?? ""))))
#else
			  ? new ReadOnlyDictionary<string, string>(CreateTags(tags))
#endif
			  : EmptyTags,
			  source, message, parameters?.ToArray() ?? Array.Empty<string>()) {
		if (parameters is not null) {
			var trail = false;
			foreach (var parameter in parameters) {
				if (parameter == "" || parameter[0] == ':' || parameter.Contains(' ')) {
					if (trail) throw new ArgumentException("More than one trailing parameter was found.", nameof(parameters));
					trail = true;
				}
			}
		}
	}

	private IrcLine(IReadOnlyDictionary<string, string> tags, string? sourcd, string message, IReadOnlyList<string> parameters) {
		this.Tags = tags;
		this.Source = sourcd;
		this.Message = message;
		this.Parameters = parameters;
	}

	public string? GetParameter(int index) => this.Parameters.Count > index ? this.Parameters[index] : null;
	public string GetParameter(int index, string defaultValue) => this.GetParameter(index) ?? defaultValue;

#if !(NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER)
	private static Dictionary<string, string> CreateTags(IEnumerable<KeyValuePair<string, string>> tags) {
		var dictionary = new Dictionary<string, string>();
		foreach (var pair in tags) dictionary.Add(pair.Key, pair.Value);
		return dictionary;
	}
#endif

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
	public static IrcLine FromBinary(ReadOnlySpan<byte> bytes, Encoding encoding) {
		var i = 0;
		var endIndex = bytes.Length;
#else
	private static IrcLine FromBinaryInternal(byte[] bytes, int startIndex, int endIndex, Encoding encoding) {
		if (bytes is null) throw new ArgumentNullException(nameof(bytes));
		var i = startIndex;
#endif
		if (encoding is null) throw new ArgumentNullException(nameof(encoding));

		cacheParameters ??= new(16);

		int j;
		try {
			IReadOnlyDictionary<string, string> tagsReadOnly;
			if (bytes[i] == '@') {
				// IRCv3 tags (always UTF-8): https://ircv3.net/specs/extensions/message-tags
				var tags = new Dictionary<string, string>();
				do {
					i++;
					string? value;

					for (j = i; j < endIndex; j++) {
						if (bytes[j] is (byte) '=' or (byte) ';' or (byte) ' ') break;
					}
					if (j == i) continue;

					var tag = GetString(Encoding.UTF8, bytes, i, j);

					if (bytes[j] == '=') {
						i = j + 1;
						for (j = i; j < endIndex; j++) {
							if (bytes[j] is (byte) ';' or (byte) ' ') break;
						}
						value = UnescapeTagValue(GetString(Encoding.UTF8, bytes, i, j));
					} else
						value = "";

					tags.Add(tag, value);
					i = j;
				} while (bytes[i] != ' ');
				do { i++; } while (bytes[i] == ' ');
				tagsReadOnly = new ReadOnlyDictionary<string, string>(tags);
			} else
				tagsReadOnly = EmptyTags;

			string? prefix;
			if (bytes[i] == ':') {
				for (j = i + 1; j < endIndex; j++) {
					if (bytes[j] == ' ') break;
				}
				prefix = GetString(encoding, bytes, i + 1, j);
				i = j;
				do { i++; } while (bytes[i] == ' ');
			} else
				prefix = null;

			for (j = i; j < endIndex; j++) {
				if (bytes[j] == ' ') break;
			}
			var message = GetString(Encoding.UTF8, bytes, i, j);
			i = j;

			while (true) {
				do { i++; } while (i < endIndex && bytes[i] == ' ');
				if (i >= endIndex) break;

				if (bytes[i] == ':') {
					// Trailing parameter: include all text to the end of the line.
					cacheParameters.Add(GetString(encoding, bytes, i + 1, endIndex));
					break;
				} else {
					for (j = i + 1; j < endIndex; j++) {
						if (bytes[j] == ' ') break;
					}
					cacheParameters.Add(GetString(encoding, bytes, i, j));
				}
				i = j;
			}

			var parameters = cacheParameters.ToArray();
			cacheParameters.Clear();
			cacheParameters.Capacity = 16;
			return new IrcLine(tagsReadOnly, prefix, message, parameters);
		} catch (IndexOutOfRangeException) {
			throw new FormatException("No message type was found.");
		}
	}
	public static IrcLine FromBinary(byte[] buffer, int startIndex, int length, Encoding encoding)
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
		=> FromBinary(buffer.AsSpan(startIndex, length), encoding);
#else
		=> FromBinaryInternal(buffer, startIndex, startIndex + length, encoding);
#endif
	public static IrcLine FromBinary(byte[] buffer, Range range, Encoding encoding)
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
		=> FromBinary(buffer.AsSpan(range), encoding);
#else
		=> FromBinaryInternal(buffer, range.Start.GetOffset(buffer.Length), range.End.GetOffset(buffer.Length), encoding);
#endif

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
	private static string GetString(Encoding encoding, ReadOnlySpan<byte> bytes, int startIndex, int endIndex)
		=> encoding.GetString(bytes[startIndex..endIndex]);
#else
	private static string GetString(Encoding encoding, byte[] bytes, int startIndex, int endIndex)
		=> encoding.GetString(bytes, startIndex, endIndex - startIndex);
#endif

	public static IrcLine Parse(string s) {
		var i = 0;
		var endIndex = s.Length;
		int j;

		cacheParameters ??= new(16);

		try {
			IReadOnlyDictionary<string, string> tagsReadOnly;
			if (s[i] == '@') {
				// IRCv3 tags (always UTF-8): https://ircv3.net/specs/extensions/message-tags
				var tags = new Dictionary<string, string>();
				do {
					i++;
					string? value;

					for (j = i; j < endIndex; j++) {
						if (s[j] is '=' or ';' or ' ') break;
					}
					if (j == i) continue;

					var tag = s[i..j];

					if (s[j] == '=') {
						i = j + 1;
						for (j = i; j < endIndex; j++) {
							if (s[j] is ';' or ' ') break;
						}
						value = UnescapeTagValue(s[i..j]);
					} else
						value = "";

					tags.Add(tag, value);
					i = j;
				} while (s[i] != ' ');
				do { i++; } while (s[i] == ' ');
				tagsReadOnly = new ReadOnlyDictionary<string, string>(tags);
			} else
				tagsReadOnly = EmptyTags;

			string? prefix;
			if (s[i] == ':') {
				for (j = i + 1; j < endIndex; j++) {
					if (s[j] == ' ') break;
				}
				prefix = s[(i + 1)..j];
				i = j;
				do { i++; } while (s[i] == ' ');
			} else
				prefix = null;

			for (j = i; j < endIndex; j++) {
				if (s[j] == ' ') break;
			}
			var message = s[i..j];
			i = j;

			while (true) {
				do { i++; } while (i < endIndex && s[i] == ' ');
				if (i >= endIndex) break;

				if (s[i] == ':') {
					// Trailing parameter: include all text to the end of the line.
					cacheParameters.Add(s[(i + 1)..]);
					break;
				} else {
					for (j = i + 1; j < endIndex; j++) {
						if (s[j] == ' ') break;
					}
					cacheParameters.Add(s[i..j]);
				}
				i = j;
			}

			var parameters = cacheParameters.ToArray();
			cacheParameters.Clear();
			cacheParameters.Capacity = 16;
			return new IrcLine(tagsReadOnly, prefix, message, parameters);
		} catch (IndexOutOfRangeException) {
			throw new FormatException("No message type was found.");
		}
	}

	internal static string EscapeTag(string value) {
		cacheStringBuilder ??= new(1024);
		cacheStringBuilder.Clear();
		EscapeTag(value, cacheStringBuilder);
		var s = cacheStringBuilder.ToString();
		cacheStringBuilder.Clear();
		cacheStringBuilder.Capacity = 1024;
		return s;
	}
	internal static void EscapeTag(string value, StringBuilder stringBuilder) {
		foreach (var c in value) {
			switch (c) {
				case ';' : stringBuilder.Append(@"\:"); break;
				case ' ' : stringBuilder.Append(@"\s"); break;
				case '\\': stringBuilder.Append(@"\\"); break;
				case '\r': stringBuilder.Append(@"\r"); break;
				case '\n': stringBuilder.Append(@"\n"); break;
				default  : stringBuilder.Append(c); break;
			}
		}
	}

	private static string UnescapeTagValue(string s) {
		var pos2 = s.IndexOf('\\');
		if (pos2 < 0) return s;

		var pos = 0;
		cacheStringBuilder ??= new(1024);
		cacheStringBuilder.Clear();
		do {
			cacheStringBuilder.Append(s, pos, pos2 - pos);
			pos2++;
			if (pos2 >= s.Length) {
				pos = pos2;
				break;  // Lone trailing \ produces nothing.
			}
			cacheStringBuilder.Append(s[pos2] switch { ':' => ';', 's' => ' ', 'r' => '\r', 'n' => '\n', _ => s[pos2] });
			pos = pos2 + 1;
			pos2 = s.IndexOf('\\', pos);
		} while (pos2 >= 0);
		cacheStringBuilder.Append(s, pos, s.Length - pos);

		var result = cacheStringBuilder.ToString();
		cacheStringBuilder.Clear();
		cacheStringBuilder.Capacity = 1024;
		return result.ToString();
	}

	/// <summary>Returns the string representation of this <see cref="IrcLine"/>, as specified by the IRC protocol.</summary>
	public override string ToString() {
		cacheStringBuilder ??= new(1024);
		cacheStringBuilder.Clear();

		if (this.Tags.Count > 0) {
			var anyTags = false;
			foreach (var tag in this.Tags) {
				if (anyTags)
					cacheStringBuilder.Append(';');
				else {
					cacheStringBuilder.Append('@');
					anyTags = true;
				}
				cacheStringBuilder.Append(tag.Key);
				if (!string.IsNullOrEmpty(tag.Value)) {
					cacheStringBuilder.Append('=');
					EscapeTag(tag.Value, cacheStringBuilder);
				}
			}
			cacheStringBuilder.Append(' ');
		}

		if (this.Source != null) {
			cacheStringBuilder.Append(':');
			cacheStringBuilder.Append(this.Source);
			cacheStringBuilder.Append(' ');
		}

		cacheStringBuilder.Append(this.Message);

		foreach (var parameter in this.Parameters) {
			cacheStringBuilder.Append(' ');
			if (parameter == "" || parameter[0] == ':' || parameter.Contains(' '))
				cacheStringBuilder.Append(':');
			cacheStringBuilder.Append(parameter);
		}

		var result = cacheStringBuilder.ToString();
		cacheStringBuilder.Clear();
		cacheStringBuilder.Capacity = 1024;
		return result.ToString();
	}
}
