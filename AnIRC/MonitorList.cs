using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AnIRC {
	/// <summary>Represents a MONITOR or WATCH list, and provides methods to modify the list.</summary>
	/// <seealso cref="IrcExtensions.SupportsMonitor"/>
	public class MonitorList : ISet<string> {
		/// <summary>Returns the <see cref="IrcClient"/> that this instance belongs to.</summary>
		public IrcClient Client { get; }
		private HashSet<string> nicknames;

		public int Count => this.nicknames.Count;

		public bool IsReadOnly => false;

		internal MonitorList(IrcClient client) {
			this.Client = client;
			this.nicknames = new HashSet<string>(client.CaseMappingComparer);
		}

		internal void SetCaseMapping() {
			var oldNicknames = this.nicknames;
			this.nicknames = new HashSet<string>(this.Client.CaseMappingComparer);
			foreach (var nickname in oldNicknames) this.nicknames.Add(nickname);
		}

		public IEnumerator<string> GetEnumerator() => this.nicknames.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

		/// <summary>Determines whether the specified nickname is in this list.</summary>
		public bool Contains(string nickname) => this.nicknames.Contains(nickname);

		/// <summary>Sends a command to add the specified nickname to the list.</summary>
		public bool Add(string nickname) {
			if (this.Client.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to perform this operation.");
			if (!this.Client.Extensions.SupportsMonitor) throw new NotSupportedException("This network does not support a monitor list.");
			if (this.Contains(nickname)) return false;
			CheckNickname(nickname);
			if (this.Client.Extensions.ContainsKey("MONITOR")) this.Client.Send("MONITOR + " + nickname);
			else this.Client.Send("WATCH +" + nickname);
			this.AddInternal(nickname);
			return true;
		}
		void ICollection<string>.Add(string nickname) => this.Add(nickname);
		/// <summary>Sends a command to add the specified nicknames to the list.</summary>
		public void AddRange(IEnumerable<string> nicknames) {
			if (this.Client.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to perform this operation.");
			if (!this.Client.Extensions.SupportsMonitor) throw new NotSupportedException("This network does not support a monitor list.");
			this.Change('+', nicknames.Where(n => !this.Contains(n)), this.AddInternal);
		}
		/// <summary>Sends a command to remove the specified nickname from the list.</summary>
		/// <returns>True if the nickname was removed; false if it was not in the list.</returns>
		public bool Remove(string nickname) {
			if (this.Client.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to perform this operation.");
			if (!this.Client.Extensions.SupportsMonitor) throw new NotSupportedException("This network does not support a monitor list.");
			CheckNickname(nickname);
			if (!this.Contains(nickname)) return false;
			if (this.Client.Extensions.ContainsKey("MONITOR")) this.Client.Send("MONITOR - " + nickname);
			else this.Client.Send("WATCH -" + nickname);
			this.RemoveInternal(nickname);
			return true;
		}
		/// <summary>Sends a command to remove the specified nicknames from the list.</summary>
		public void RemoveRange(IEnumerable<string> nicknames) {
			if (this.Client.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to perform this operation.");
			if (!this.Client.Extensions.SupportsMonitor) throw new NotSupportedException("This network does not support a monitor list.");
			this.Change('-', nicknames.Where(n => this.Contains(n)), this.RemoveInternal);
		}
		/// <summary>Sends a command to clear the list.</summary>
		public void Clear() {
			if (this.Client.State < IrcClientState.ReceivingServerInfo) throw new InvalidOperationException("The client must be registered to perform this operation.");
			if (!this.Client.Extensions.SupportsMonitor) throw new NotSupportedException("This network does not support a monitor list.");
			if (this.Client.Extensions.ContainsKey("MONITOR")) this.Client.Send("MONITOR C");
			else this.Client.Send("WATCH C");
			this.ClearInternal();
		}

		internal void AddInternal(string nickname) {
			this.nicknames.Add(nickname);
			if (this.Client.Users.TryGetValue(nickname, out var user))
				user.Monitoring = true;
		}
		internal void RemoveInternal(string nickname) {
			this.nicknames.Remove(nickname);
			if (this.Client.Users.TryGetValue(nickname, out var user)) {
				user.Monitoring = false;
				if (user.Channels.Count == 0)
					this.Client.OnUserDisappeared(new IrcUserEventArgs(user));
			}
		}
		internal void ClearInternal() {
			foreach (var nickname in this.nicknames) {
				if (this.Client.Users.TryGetValue(nickname, out var user)) {
					user.Monitoring = false;
					if (user.Channels.Count == 0)
						this.Client.OnUserDisappeared(new IrcUserEventArgs(user));
				}
			}
			this.nicknames.Clear();
		}

		private void Change(char direction, IEnumerable<string> nicknames, Action<string> action) {
			bool monitor = this.Client.Extensions.ContainsKey("MONITOR");
			bool end = false;
			var enumerator = nicknames.GetEnumerator();
			var commandBuilder = new StringBuilder();
			while (!end) {
				commandBuilder.Clear();
				commandBuilder.Append(monitor ? "MONITOR " + direction : "WATCH");
				commandBuilder.Append(' ');
				var any = false;
				while (true) {
					if (!enumerator.MoveNext()) {
						end = true;
						break;
					}
					var nickname = enumerator.Current;
					CheckNickname(nickname);
					if (any) {
						if (commandBuilder.Length + 1 + nickname.Length > 510) break;
						if (monitor) commandBuilder.Append(',');
						else {
							commandBuilder.Append(' ');
						}
					} else
						any = true;
					if (!monitor) commandBuilder.Append(direction);
					commandBuilder.Append(nickname);
					action.Invoke(nickname);
				}
				this.Client.Send(commandBuilder.ToString());
			}
		}

		private static void CheckNickname(string nickname) {
			if (nickname.Any(c => c is ' ' or ',' or '\r' or '\n'))
				throw new ArgumentException("Nickname contains invalid characters.", nameof(nickname));
		}

		public void UnionWith(IEnumerable<string> other) => this.AddRange(other);
		public void IntersectWith(IEnumerable<string> other) => this.RemoveRange(this.Where(n => !other.Contains(n)).ToArray());
		public void ExceptWith(IEnumerable<string> other) => this.RemoveRange(other);
		public void SymmetricExceptWith(IEnumerable<string> other) {
			var toRemove = new HashSet<string>();
			var toAdd = new HashSet<string>();
			foreach (var nickname in other) {
				if (this.Contains(nickname))
					toRemove.Add(nickname);
				else
					toAdd.Add(nickname);
			}
			this.RemoveRange(toRemove);
			this.AddRange(toAdd);
		}

		public bool IsSubsetOf(IEnumerable<string> other) => this.nicknames.IsSubsetOf(other);
		public bool IsSupersetOf(IEnumerable<string> other) => this.nicknames.IsSupersetOf(other);
		public bool IsProperSupersetOf(IEnumerable<string> other) => this.nicknames.IsProperSupersetOf(other);
		public bool IsProperSubsetOf(IEnumerable<string> other) => this.nicknames.IsProperSubsetOf(other);
		public bool Overlaps(IEnumerable<string> other) => this.nicknames.Overlaps(other);
		public bool SetEquals(IEnumerable<string> other) => this.nicknames.SetEquals(other);
		public void CopyTo(string[] array, int arrayIndex) => this.nicknames.CopyTo(array, arrayIndex);
	}
}
