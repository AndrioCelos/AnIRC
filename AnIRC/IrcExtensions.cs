using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AnIRC;

/// <summary>
///     Stores the list of RPL_ISUPPORT parameters advertised by an IRC server.
/// </summary>
/// <remarks>
///     <para>For more information, see https://tools.ietf.org/html/draft-brocklesby-irc-isupport-03 </para>
///     <para>This class is implemented as a <see cref="ReadOnlyDictionary{TKey, TValue}"/> of <see cref="string"/>, <see cref="string"/>.</para>
/// </remarks>
public class IrcExtensions : ReadOnlyDictionary<string, string> {
	/// <summary>Returns the <see cref="IrcClient"/> that this <see cref="IrcExtensions"/> list belongs to.</summary>
	public IrcClient? Client { get; }

	/// <summary>The RPL_ISUPPORT specification of the case mapping this server uses to compare nicknames and channel names.</summary>
	/// <remarks>The value is case sensitive. There are three known values: <c>ascii</c>, <c>rfc1459</c> (the default) and <c>strict-rfc1459</c>.</remarks>
	public string CaseMapping { get; protected internal set; } = "rfc1459";
	/// <summary>The RPL_ISUPPORT specification of the maximum number of each type of channel we may be on.</summary>
	/// <remarks>Each key contains one of more channel prefixes, and the corresponding value is the limit for all of those channel types combined.</remarks>
	public ReadOnlyDictionary<string, int> ChannelLimit { get; protected internal set; }
	/// <summary>The RPL_ISUPPORT specification of the channel modes this server supports.</summary>
	/// <remarks>The value consists of four or more comma-separated categories, each containing zero or more mode characters. They are described in detail in http://www.irc.org/tech_docs/draft-brocklesby-irc-isupport-03.txt</remarks>
	public ChannelModes ChanModes { get; protected internal set; } = ChannelModes.RFC1459;
	/// <summary>The RPL_ISUPPORT specification of the maximum length of a channel name.</summary>
	public int ChannelLength { get; protected internal set; } = 200;
	/// <summary>The RPL_ISUPPORT specification of the channel types supported by this server.</summary>
	public ReadOnlyCollection<char> ChannelTypes { get; protected internal set; } = Array.AsReadOnly(new[] { '#' });
	/// <summary>True if the server supports channel ban exceptions.</summary>
	public bool SupportsBanExceptions { get; protected internal set; }
	/// <summary>The RPL_ISUPPORT specification of the mode character used for channel ban exceptions.</summary>
	public char BanExceptionsMode { get; protected internal set; }
	/// <summary>True if the server supports channel invite exceptions.</summary>
	public bool SupportsInviteExceptions { get; protected internal set; }
	/// <summary>The RPL_ISUPPORT specification of the mode character used for channel invite exceptions.</summary>
	public char InviteExceptionsMode { get; protected internal set; }
	/// <summary>The maximum number of entries allowed in the MONITOR or WATCH list, zero if neither is supported, or int.MaxValue if there is no limit.</summary>
	public int MonitorLimit { get; protected internal set; }
	/// <summary>True if the server supports the MONITOR or WATCH command.</summary>
	public bool SupportsMonitor => this.MonitorLimit > 0;
	/// <summary>The RPL_ISUPPORT specification of the maximum length of a kick message.</summary>
	public int KickMessageLength { get; protected internal set; } = 500;
	/// <summary>The RPL_ISUPPORT specification of the maximum number of entries that may be added to a channel list mode.</summary>
	/// <remarks>Each key contains one of more mode characters, and the corresponding value is the limit for all of those modes combined.</remarks>
	public ReadOnlyDictionary<string, int> ListModeLength { get; protected internal set; }
	/// <summary>The RPL_ISUPPORT specification of the maximum number of modes that can be set with a single command.</summary>
	public int Modes { get; protected internal set; } = 3;
	/// <summary>The RPL_ISUPPORT specification of the name of the IRC network.</summary>
	/// <remarks>Note that this is not known until, and unless, the RPL_ISUPPORT message is received.</remarks>
	public string NetworkName { get; protected internal set; }
	/// <summary>The RPL_ISUPPORT specification of the maximum length of a nickname we may use.</summary>
	public int NicknameLength { get; protected internal set; } = 9;
	/// <summary>The RPL_ISUPPORT specification of the channel status modes this server supports.</summary>
	/// <remarks>Each entry contains a prefix as the key, and the corresponding mode character as the value. They are given in order from highest to lowest status.</remarks>
	public ReadOnlyDictionary<char, char> StatusPrefix { get; protected internal set; }
	/// <summary>The RPL_ISUPPORT specification of the status prefixes we may use to only talk to users on a channel with that status.</summary>
	/// <remarks>Note that many servers require we also have that status to do this.</remarks>
	public ReadOnlyCollection<char> StatusMessage { get; protected internal set; }
	/// <summary>The RPL_ISUPPORT specification of the maximum number of targets we may give for certain commands.</summary>
	/// <remarks>Each entry consists of the command and the corresponding limit. Any command that's not listed does not support multiple targets.</remarks>
	public ReadOnlyDictionary<string, int> MaxTargets { get; protected internal set; }
	/// <summary>The RPL_ISUPPORT specification of the maximum length of a channel topic.</summary>
	public int TopicLength { get; protected internal set; } = 501;
	/// <summary>True if the server mandates UTF-8 only.</summary>
	public bool Utf8Only { get; protected internal set; }
	/// <summary>True if the server supports the WHOX command.</summary>
	/// <remarks>If true, we will use the WATCH list to monitor users in the Users list.</remarks>
	public bool SupportsWhox { get; protected internal set; }

	/// <summary>Returns an <see cref="IrcExtensions"/> object with the default parameters.</summary>
	public static IrcExtensions Default { get; } = new IrcExtensions(null, "");

	/// <summary>Returns true if all parameters have the default values.</summary>
	public bool IsDefault => this.Count == 0;

	/// <summary>Serves as the backing field for the ChannelLimit property.</summary>
	protected Dictionary<string, int> channelLimit = new() { { "#&", int.MaxValue } };
	/// <summary>Serves as the backing field for the ListModeLength property.</summary>
	protected Dictionary<string, int> listModeLength = new();
	/// <summary>Serves as the backing field for the StatusPrefix property.</summary>
	protected Dictionary<char, char> statusPrefix = new() { { '@', 'o' }, { '+', 'v' } };
	/// <summary>Serves as the backing field for the MaxTargets property.</summary>
	protected Dictionary<string, int> maxTargets = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Contains a list of status modes in descending order, including some unsupported ones.
	/// Used by <see cref="ChannelStatus"/> to give consistent behaviour when comparing with unsupported modes.</summary>
	/// <remarks>
	///     Specifically, to handle cases such as <c>user.Status >= ChannelStatus.Halfop</c> when the network doesn't have halfops.
	///     In this case, it shall be generally equivalent to <c>user.Status >= ChannelStatus.Op</c>.
	/// </remarks>
	internal List<char> allStatus = new() { 'q', 'a', 'o', 'h', 'v', 'V' };

	/// <summary>Initializes a new instance of the <see cref="IrcExtensions"/> class with the default values and a specified network name.</summary>
	internal IrcExtensions(IrcClient? client, string networkName) : base(new Dictionary<string, string>()) {
		this.Client = client;
		this.ChannelLimit = new(this.channelLimit);
		this.ListModeLength = new(this.listModeLength);
		this.NetworkName = networkName;
		this.StatusPrefix = new(this.statusPrefix);
		this.StatusMessage = new(Array.Empty<char>());
		this.MaxTargets = new(this.maxTargets);
	}

	/// <summary>Gets the value of a parameter.</summary>
	/// <param name="key">The name of the parameter to get or set. Case sensitive.</param>
	public new string this[string key] {
		get => base[key];
		protected internal set {
			string[] fields;

			this.Dictionary[key] = value ?? throw new ArgumentNullException(nameof(value));  // Setting to null is no longer supported. Use Remove instead.

			switch (key) {  // Parameter names are case sensitive.
				case "CASEMAPPING":
					bool changed = value != this.CaseMapping;
					this.CaseMapping = value;
					if (changed) this.Client?.SetCaseMappingComparer();
					break;
				case "CHANLIMIT":
					this.channelLimit.Clear();
					foreach (string field in value.Split(new char[] { ',' })) {
						fields = field.Split(new char[] { ':' });
						this.channelLimit.Add(fields[0], fields[1] == "" ? int.MaxValue : int.Parse(fields[1]));
					}
					break;
				case "CHANMODES":
					var statusModes = this.ChanModes?.Status;
					fields = value.Split(new char[] { ',' });
					this.ChanModes = new ChannelModes(fields[0], fields[1], fields[2], fields[3], statusModes);
					break;
				case "CHANNELLEN": this.ChannelLength = int.Parse(value); break;
				case "CHANTYPES": this.ChannelTypes = new ReadOnlyCollection<char>((value ?? "#").ToCharArray()); break;
				case "EXCEPTS":
					this.SupportsBanExceptions = true;
					this.BanExceptionsMode = value == "" ? 'e' : value[0];
					break;
				case "INVEX":
					this.SupportsInviteExceptions = true;
					this.InviteExceptionsMode = value == "" ? 'I' : value[0];
					break;
				case "KICKLEN": this.KickMessageLength = int.Parse(value); break;
				case "MAXBANS":  // Obsolete form of MAXLIST
					this.listModeLength.Clear();
					var modes = "b" + (this.SupportsBanExceptions ? this.BanExceptionsMode : "") + (this.SupportsInviteExceptions ? this.InviteExceptionsMode : "");
					this.listModeLength.Add(modes, int.Parse(value));
					break;
				case "MAXCHANNELS":  // Obsolete form of CHANLIMIT
					this.channelLimit.Clear();
					if (this.TryGetValue("CHANTYPES", out var chantypes))
						this.channelLimit.Add(chantypes, int.Parse(value));
					else
						this.channelLimit.Add("#&+!", int.Parse(value));
					break;
				case "MAXLIST":
					this.listModeLength.Clear();
					foreach (string entry in value.Split(new char[] { ',' })) {
						fields = entry.Split(new char[] { ':' }, 2);
						this.listModeLength.Add(fields[0], int.Parse(fields[1]));
					}
					break;
				case "MODES": this.Modes = value == "" ? int.MaxValue : int.Parse(value); break;
				case "MONITOR": this.MonitorLimit = value == "" ? int.MaxValue : int.Parse(value); break;
				case "NETWORK": this.NetworkName = value; break;
				case "NICKLEN": this.NicknameLength = int.Parse(value); break;
				case "PREFIX":
					this.statusPrefix.Clear();
					this.allStatus.Clear();
					if (value != "") {
						var m = Regex.Match(value, @"^\(([a-zA-Z]*)\)(.*)$");
						this.ChanModes.SetStatusModes(m.Groups[1].Value);
						for (int j = 0; j < m.Groups[1].Value.Length; ++j) {
							this.statusPrefix.Add(m.Groups[2].Value[j], m.Groups[1].Value[j]);
							this.allStatus.Add(m.Groups[1].Value[j]);
						}
					}

					// Add some common, but unsupported modes.
					this.InsertUnsupportedMode('q', "", "ov");
					this.InsertUnsupportedMode('a', "q", "ov");
					this.InsertUnsupportedMode('o', "aq", "v");
					this.InsertUnsupportedMode('h', "oaq", "v");
					this.InsertUnsupportedMode('v', "hoaq", "");
					this.InsertUnsupportedMode('V', "vhoaq", "");
					break;
				case "STATUSMSG": this.StatusMessage = new(value.ToCharArray()); break;
				case "TARGMAX":
					this.maxTargets.Clear();
					foreach (string field in value.Split(new char[] { ',' })) {
						fields = field.Split(new char[] { ':' }, 2);
						if (fields[1] == "")
							this.maxTargets.Remove(fields[0]);
						else
							this.maxTargets.Add(fields[0], int.Parse(fields[1]));
					}
					break;
				case "TOPICLEN": this.TopicLength = string.IsNullOrEmpty(value) ? int.MaxValue : int.Parse(value); break;
				case "UTF8ONLY":
					this.Utf8Only = true;
					if (this.Client is not null) this.Client.encoding = IrcClient.Utf8Encoding;
					break;
				case "WATCH":
					if (!this.ContainsKey("MONITOR")) this.MonitorLimit = value == "" ? int.MaxValue : int.Parse(value);
					break;
				case "WHOX": this.SupportsWhox = true; break;
			}
		}
	}

	internal void Remove(string key) {
		var result = this.Dictionary.Remove(key);
		if (!result) return;  // Nothing happened.

		switch (key) {  // Parameter names are case sensitive.
			case "CASEMAPPING":
				if (this.CaseMapping != "rfc1459") {
					this.CaseMapping = "rfc1459";
					this.Client?.SetCaseMappingComparer();
				}
				break;
			case "CHANLIMIT":
				this.channelLimit.Clear();
				this.channelLimit.Add("#&+!", int.MaxValue);
				break;
			case "CHANMODES":
				this.ChanModes = ChannelModes.RFC1459;
				break;
			case "CHANNELLEN": this.ChannelLength = 200; break;
			case "CHANTYPES": this.ChannelTypes = new ReadOnlyCollection<char>(new[] { '#' }); break;
			case "EXCEPTS":
				this.SupportsBanExceptions = false;
				this.BanExceptionsMode = '\0';
				break;
			case "INVEX":
				this.SupportsInviteExceptions = false;
				this.InviteExceptionsMode = '\0';
				break;
			case "KICKLEN": this.KickMessageLength = int.MaxValue; break;
			case "MAXBANS":  // Obsolete form of MAXLIST
				this.listModeLength.Clear();
				break;
			case "MAXCHANNELS":  // Obsolete form of CHANLIMIT
				this.channelLimit.Clear();
				break;
			case "MAXLIST":
				this.listModeLength.Clear();
				break;
			case "MODES": this.Modes = 3; break;
			case "MONITOR": this.MonitorLimit = this.TryGetValue("WATCH", out var s) ? (s == "" ? int.MaxValue : int.Parse(s)) : 0; break;
			//case "NETWORK": break;  // Ignore unsetting NETWORK.
			case "NICKLEN": this.NicknameLength = 9; break;
			case "PREFIX":
				this.statusPrefix.Clear();
				this.allStatus.Clear();
				this.statusPrefix.Add('@', 'o');
				this.statusPrefix.Add('+', 'v');
				this.allStatus = new() { 'q', 'a', 'o', 'h', 'v', 'V' };
				this.ChanModes.SetStatusModes("ov");
				break;
			case "STATUSMSG": this.StatusMessage = new(Array.Empty<char>()); break;
			case "TARGMAX": this.maxTargets.Clear(); break;
			case "TOPICLEN": this.TopicLength = int.MaxValue; break;
			case "UTF8ONLY": this.Utf8Only = false; break;
			case "WATCH":
				if (!this.ContainsKey("MONITOR")) this.MonitorLimit = 0;
				break;
			case "WHOX": this.SupportsWhox = false; break;
		}
	}

	/// <summary>Inserts the specified mode to the list of all status modes if it is not already present.</summary>
	/// <param name="after">A list of higher status modes. The new mode will be considered immediately lower than the first existing mode in this list.</param>
	/// <remarks>This is used to enable comparisons of <see cref="ChannelStatus"/> such as '>= <see cref="ChannelStatus.Halfop"/>' even if that mode does not exist.</remarks>
	private void InsertUnsupportedMode(char mode, IEnumerable<char> below, IEnumerable<char> above) {
		if (this.allStatus.Contains(mode)) return;
		foreach (var c in below) {
			var pos = this.allStatus.IndexOf(c);
			if (pos >= 0) {
				// To insert after this mode, this mode must also be above all the 'above' modes.
				// This is so that in the theoretical case of `PREFIX=(ovq)@+-`, a will not be below q.
				var invalid = false;
				foreach (var c2 in above) {
					if (this.allStatus.IndexOf(c2, 0, pos) >= 0) {
						invalid = true;
						break;
					} 
				}
				if (!invalid) {
					this.allStatus.Insert(pos + 1, mode);
					return;
				}
			}
		}
		this.allStatus.Insert(0, mode);
	}

	/// <summary>Escapes certain characters in a string for sending as a RPL_ISUPPORT value using UTF-8.</summary>
	/// <returns>A copy of the input string with certain characters escaped, or the input string itself if no characters were escaped.</returns>
	/// <remarks>
	/// The escape code is "\xHH", where HH is the numeric representation of a byte.
	/// Multibyte characters can be escaped as multiple \x sequences.
	/// This method escapes all characters outside the range 32-126 and backslashes.
	/// </remarks>
	public static string EscapeValue(string value) => EscapeValue(value, Encoding.UTF8);
	/// <summary>Escapes certain characters in a string for sending as a RPL_ISUPPORT value using the specified encoding.</summary>
	/// <returns>A copy of the input string with certain characters escaped, or the input string itself if no characters were escaped.</returns>
	/// <remarks>
	/// The escape code is "\xHH", where HH is the hexadecimal representation of a byte.
	/// Multibyte characters can be escaped as multiple \x sequences.
	/// This method escapes all characters outside the range 32-126 and backslashes.
	/// </remarks>
	public static string EscapeValue(string value, Encoding encoding) {
		bool anyEscapes = false;
		// TODO: Make this also escape multibyte characters where one byte happens to be 0x00, 0x0A, 0x0D, 0x20 or 0x5C?
		// This isn't possible in UTF-8, as all bytes of multibyte characters have bit 7 set, but may be for other encodings.
		var builder = new StringBuilder();
		foreach (char c in value) {
			if (c >= 127) {
				anyEscapes = true;
				foreach (var b in encoding.GetBytes(c.ToString())) {
					builder.Append(@"\x");
					builder.Append(b.ToString("X2"));
				}
			} else if (c is <= ' ' or '\\') {
				anyEscapes = true;
				builder.Append(@"\x");
				builder.Append(((int) c).ToString("X2"));
			} else
				builder.Append(c);
		}
		return !anyEscapes ? value : builder.ToString();
	}
	/// <summary>Decodes escape sequences in a RPL_ISUPPORT value using UTF-8.</summary>
	/// <returns>A copy of the input string with escape sequences decoded, or the input string itself if no escape sequences are present.</returns>
	/// <remarks>
	/// The escape code is "\xHH", where HH is the numeric representation of a byte.
	/// Multibyte characters can be escaped as multiple \x sequences.
	/// </remarks>
	public static string UnescapeValue(string value) => UnescapeValue(value, Encoding.UTF8);
	/// <summary>Decodes escape sequences in a RPL_ISUPPORT value using the specified encoding.</summary>
	/// <returns>A copy of the input string with escape sequences decoded, or the input string itself if no escape sequences are present.</returns>
	/// <remarks>
	/// The escape code is "\xHH", where HH is the numeric representation of a byte.
	/// Multibyte characters can be escaped as multiple \x sequences.
	/// </remarks>
	public static string UnescapeValue(string value, Encoding encoding)
		=> Regex.Replace(value, @"(?:\\x([0-9a-f]{2}))+",
			m => encoding.GetString(m.Groups[1].Captures.Cast<Capture>().Select(c => byte.Parse(c.Value, System.Globalization.NumberStyles.HexNumber)).ToArray()),
			RegexOptions.IgnoreCase);

	/// <summary>Returns the parameters contained in this Extensions object as a string, in the form used by RPL_ISUPPORT.</summary>
	public override string ToString() {
		var builder = new StringBuilder();
		foreach (var parameter in this) {
			if (builder.Length != 0) builder.Append(' ');
			builder.Append(parameter.Key);
			if (parameter.Value != "") {
				builder.Append('=');
				builder.Append(EscapeValue(parameter.Value));
			}
		}
		return builder.ToString();
	}
}
