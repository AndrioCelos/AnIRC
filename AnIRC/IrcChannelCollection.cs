using System.Diagnostics;

namespace AnIRC;

/// <summary>
/// Represents a read-only list of IRC channels.
/// </summary>
/// <seealso cref="IrcChannel"/>
[DebuggerDisplay("Count = {Count}")]
public class IrcChannelCollection : IrcNamedEntityCollection<IrcChannel> {
	/// <summary>Initializes a new <see cref="IrcChannelCollection"/> belonging to the specified <see cref="IrcClient"/>.</summary>
	protected internal IrcChannelCollection(IrcClient client) : base(client) { }

	/// <summary>Returns the <see cref="IrcChannel"/> object representing the channel with the specified name, creating one if necessary.</summary>
	internal IrcChannel Get(string name) => this.TryGetValue(name, out var channel) ? channel : new IrcChannel(this.Client!, name);
}
