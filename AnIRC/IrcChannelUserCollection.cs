using System.Collections.Generic;
using System.Linq;

namespace AnIRC {
	/// <summary>
	/// Represents a read-only list of users on a channel.
	/// </summary>
	/// <seealso cref="IrcChannelUser"/>
	public class IrcChannelUserCollection : IrcNamedEntityCollection<IrcChannelUser> {
		/// <summary>Initializes a new <see cref="IrcChannelUserCollection"/> belonging to the specified <see cref="IrcClient"/>.</summary>
		protected internal IrcChannelUserCollection(IrcClient client) : base(client) { }

		/// <summary>Returns the number of users in this list who have the specified status or higher.</summary>
		public int StatusCount(ChannelStatus status) => this.Count(user => user.Status >= status);

		public IEnumerable<IrcChannelUser> Matching(string hostmask) => this.Where(user => Hostmask.Matches(user.User.ToString(), hostmask));
	}
}