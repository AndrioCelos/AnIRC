﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static AnIRC.Replies;

namespace AnIRC {
    /// <summary>
    /// Provides a <see cref="TaskCompletionSource{TResult}"/> object to allow users to await a response to an IRC command.
    /// </summary>
    /// <remarks>
    ///     <para>Async requests are intended to provide a clean, simple way to allow code to asynchronously wait for a reply from the IRC server.</para>
    ///     <para>At the core of the <see cref="AsyncRequest"/> class is the <see cref="Task"/> property; it returns the task that can be awaited.
    ///         Since there is no base type for <see cref="TaskCompletionSource{TResult}"/>, derived classes must provide the <see cref="Task"/> object.</para>
    ///     <para>Async requests can listen for specific replies from the server, specified by the <see cref="Replies"/> property.
    ///         When a matching reply is received, the <see cref="OnReply(IrcLine, ref bool)"/> method is called,
    ///         allowing a derived class to respond.</para>
    ///     <para>If the reply is a final reply (specified by the value true in the <see cref="Replies"/> collection
    ///         or by the <see cref="OnReply(IrcLine, ref bool)"/> method through a ref parameter), 
    ///         the <see cref="IrcClient"/> class will automatically drop the request.</para>
    ///     <para>If the connection is lost, or a timeout occurs, the <see cref="OnFailure(Exception)"/> method is called
    ///         and the request is dropped.</para>
    ///     <para>The IRC client read thread must not be blocked waiting on an async request; this would cause a deadlock.
    ///         Await the request instead.</para>
    /// </remarks>
    /// <example>
    ///     <para>The <see cref="ChannelJoinEventArgs"/> object provided in the <see cref="IrcClient.ChannelJoin"/> event
    ///         now contains a <see cref="AsyncRequest"/> that will complete when the NAMES list is received.
    ///         This example will print the number of users in the channel and the number of ops.</para>
    ///     <code>
    ///         public async void IrcClient_ChannelJoin(object sender, ChannelJoinEventArgs e) {
    ///             if (e.Sender.IsMe) {
    ///                 try {
    ///                     await e.NamesTask;
    ///                     Console.WriteLine($"{e.Channel.Name} has {e.Channel.Users.Count} users and {e.Channel.Users.StatusCount(ChannelStatus.Op)} ops.");
    ///                 } catch (Exception ex) { }
    ///             }
    ///         }
    ///     </code>
    /// </example>
    public abstract class AsyncRequest {
        /// <summary>Returns the set of replies that this <see cref="AsyncRequest"/> is listening for.</summary>
        public ReadOnlyDictionary<string, bool> Replies { get; }
        /// <summary>Provides read-write access to the <see cref="Replies"/> collection.</summary>
        protected IDictionary<string, bool> RepliesSource { get; }

		/// <summary>Returns the list of parameters that must be present for this <see cref="AsyncRequest"/> to receive the reply.</summary>
		/// <remarks>
		/// Each element in the list must either match the parameter in the corresponding position (case insensitive) or be null.
		/// If this is null or empty, no checks on parameters will be done.
		/// </remarks>
		public ReadOnlyCollection<string> Parameters { get; }
		/// <summary>Provides read-write access to the <see cref="Parameters"/> collection.</summary>
		protected IList<string> ParametersSource { get; }

        /// <summary>Returns a <see cref="Task"/> object representing the status of this <see cref="AsyncRequest"/>.</summary>
        /// <remarks>
        ///     The details of what happens to the task are up to the implementation, but in general, the task might complete when a final response is received,
        ///     or fail if the connection is lost.
        ///     Derived classes may introduce other failure conditions.
        /// </remarks>
        public abstract Task Task { get; }
		/// <summary>Returns a value indicating whether this <see cref="AsyncRequest"/> can time out.</summary>
		public virtual bool CanTimeout => true;

        /// <summary>Initializes a new <see cref="AsyncRequest"/> waiting for the specified list of replies.</summary>
        /// <param name="replies">A dictionary with the replies waited on as keys. For each, if the corresponding value is true, the reply is considered a final reply.</param>
        protected AsyncRequest(IDictionary<string, bool> replies) : this(replies, null) { }
		/// <summary>Initializes a new <see cref="AsyncRequest"/> waiting for the specified list of replies and parameters.</summary>
		/// <param name="replies">A dictionary with the replies waited on as keys. For each, if the corresponding value is true, the reply is considered a final reply.</param>
		/// <param name="parameters">If not null, a list of parameters that must be present in the reply for this <see cref="AsyncRequest"/> to be triggered. Null values match anything.</param>
		protected AsyncRequest(IDictionary<string, bool> replies, IList<string> parameters) {
			this.RepliesSource = replies;
			this.Replies = new ReadOnlyDictionary<string, bool>(replies);
			this.ParametersSource = parameters;
			this.Parameters = (parameters != null ? new ReadOnlyCollection<string>(parameters) : null);
		}

		/// <summary>Called when one of the replies listed in the <see cref="Replies"/> table is received.</summary>
		/// <param name="line">The IRC line that was matched.</param>
		/// <param name="final">Indicates whether this is considered a final response, and the <see cref="AsyncRequest"/> will be dropped.</param>
		/// <returns>True if processing of async requests of this type should be stopped; false otherwise.</returns>
		protected internal abstract bool OnReply(IrcLine line, ref bool final);
        /// <summary>Called when the request times out, or the connection is lost.</summary>
        protected internal abstract void OnFailure(Exception exception);


        /// <summary>
        /// Represents an <see cref="AsyncRequest"/> whose task does not return a value, and completes when a final response is received.
        /// </summary>
        public class VoidAsyncRequest : AsyncRequest {
			/// <summary>Returns a <see cref="TaskCompletionSource{TResult}"/> that can be used to affect the <see cref="Task"/> property.</summary>
            protected TaskCompletionSource<object> TaskSource { get; } = new TaskCompletionSource<object>();
            /// <summary>Returns a <see cref="Task"/> object representing the status of this <see cref="AsyncRequest"/>.</summary>
            /// <remarks>This task will complete when a final response is received.</remarks>
            public override Task Task => this.TaskSource.Task;

			public override bool CanTimeout { get; }

			private IrcClient client;
            private string nickname;
            private HashSet<string> errors;

			/// <summary>Initializes a new <see cref="VoidAsyncRequest"/> that listens for the specified replies with the specified list of parameters.</summary>
			/// <param name="replies">The set of replies to listen for.</param>
			/// <param name="parameters">If not null, a list of parameters that must be present in the reply for this <see cref="AsyncRequest"/> to be triggered. Null values match anything.</param>
			public VoidAsyncRequest(IDictionary<string, bool> replies, IList<string> parameters) : base(replies, parameters) {
				this.CanTimeout = true;
			}
			/// <summary>Initializes a new <see cref="VoidAsyncRequest"/> that listens for the specified replies with the specified list of parameters.</summary>
			/// <param name="client">The <see cref="IrcClient"/> that this <see cref="AsyncRequest"/> belongs to.</param>
			/// <param name="nickname">If not null, the entity sending the message must have the specified nickname.</param>
			/// <param name="successReply">A reply that is considered a successful reply.</param>
			/// <param name="parameters">If not null, a list of parameters that must be present in the reply for this <see cref="AsyncRequest"/> to be triggered. Null values match anything.</param>
			/// <param name="errors">A list of replies that are considered error replies, and will cause this <see cref="AsyncRequest"/> to throw an <see cref="AsyncRequestErrorException"/>.</param>
			public VoidAsyncRequest(IrcClient client, string nickname, string successReply, IList<string> parameters, params string[] errors) : this(client, nickname, successReply, parameters, true, errors) { }
			/// <summary>Initializes a new <see cref="VoidAsyncRequest"/> that listens for the specified replies with the specified list of parameters.</summary>
			/// <param name="client">The <see cref="IrcClient"/> that this <see cref="AsyncRequest"/> belongs to.</param>
			/// <param name="nickname">If not null, the entity sending the message must have the specified nickname.</param>
			/// <param name="successReply">A reply that is considered a successful reply.</param>
			/// <param name="parameters">If not null, a list of parameters that must be present in the reply for this <see cref="AsyncRequest"/> to be triggered. Null values match anything.</param>
			/// <param name="canTimeout">Specifies whether this <see cref="AsyncRequest"/> can time out.</param>
			/// <param name="errors">A list of replies that are considered error replies, and will cause this <see cref="AsyncRequest"/> to throw an <see cref="AsyncRequestErrorException"/>.</param>
			public VoidAsyncRequest(IrcClient client, string nickname, string successReply, IList<string> parameters, bool canTimeout, params string[] errors) : base(getReplies(successReply, errors), parameters) {
                this.client = client;
                this.nickname = nickname;
                this.errors = new HashSet<string>(errors);
				this.CanTimeout = canTimeout;
            }

            private static Dictionary<string, bool> getReplies(string successReply, IEnumerable<string> errors) {
                var replies = new Dictionary<string, bool>();
                replies.Add(successReply, true);
                foreach (var reply in errors)
                    replies.Add(reply, true);
                return replies;
            }

            protected internal override bool OnReply(IrcLine line, ref bool final) {
                if (final) {
                    if (!char.IsDigit(line.Message[0]) && this.nickname != null && line.Prefix != null &&
                        !this.client.CaseMappingComparer.Equals(this.nickname, Hostmask.GetNickname(line.Prefix))) {
                        // Wrong user.
                        final = false;
                        return false;
                    }
                    if (line.Message[0] == '4' || (this.errors != null && this.errors.Contains(line.Message))) {
                        this.TaskSource.SetException(new AsyncRequestErrorException(line));
                        return true;
                    }
                    this.TaskSource.SetResult(null);
                }
                return false;
            }

			protected internal override void OnFailure(Exception exception) => this.TaskSource.SetException(exception);
		}

		/// <summary>
		/// An <see cref="AsyncRequest"/> that listens for a WHO response.
		/// </summary>
		/// <remarks>
		///	The task, if successful, returns all the data provided by the server in a <see cref="ReadOnlyCollection{WhoResponse}"/> object.
		/// </remarks>
		public class WhoAsyncRequest : AsyncRequest {
            private static Dictionary<string, bool> replies = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                // Successful replies
                { RPL_WHOREPLY, false },
                { RPL_ENDOFWHO, true },

                // Error replies
                { ERR_NOSUCHSERVER, true },
                { ERR_NOSUCHCHANNEL, true },
            };

            private IrcClient client;
            private List<WhoResponse> responses;

            public ReadOnlyCollection<WhoResponse> Responses { get; }

            private TaskCompletionSource<ReadOnlyCollection<WhoResponse>> taskSource { get; } = new TaskCompletionSource<ReadOnlyCollection<WhoResponse>>();
            private string target;
			/// <summary>Returns a <see cref="Task{TResult}"/> of <see cref="ReadOnlyCollection{T}"/> of <see cref="WhoResponse"/> representing the status of the request.</summary>
			public override Task Task => this.taskSource.Task;

			/// <summary>Initializes a new <see cref="WhoAsyncRequest"/> for the specified channel, associated with the specified <see cref="IrcClient"/>.</summary>
            public WhoAsyncRequest(IrcClient client, string target) : base(replies, new[] { null, target }) {
                this.client = client;
                this.target = target;
                this.responses = new List<WhoResponse>();
                this.Responses = this.responses.AsReadOnly();
            }

            protected internal override bool OnReply(IrcLine line, ref bool final) {
                switch (line.Message) {
                    case RPL_WHOREPLY:
                        string[] fields = line.Parameters[7].Split(new char[] { ' ' }, 2);

                        var reply = new WhoResponse() {
                            Ident = line.Parameters[2],
                            Host = line.Parameters[3],
                            Server = line.Parameters[4],
                            Nickname = line.Parameters[5],
                            HopCount = int.Parse(fields[0]),
                            FullName = fields[1]
                        };

                        if (line.Parameters[1] != "*") {
                            reply.Channel = line.Parameters[1];
                            reply.ChannelStatus = new ChannelStatus(this.client);
                        }

                        foreach (char flag in line.Parameters[6]) {
							switch (flag) {
                                case 'G':
                                    reply.Away = true;
                                    break;
                                case '*':
                                    reply.Oper = true;
                                    break;
                                default:
                                    if (client.Extensions.StatusPrefix.TryGetValue(flag, out char mode)) {
                                        if (reply.ChannelStatus == null) reply.ChannelStatus = new ChannelStatus(this.client);
                                        reply.ChannelStatus.Add(mode);
                                    }
                                    break;
                            }
                        }

						this.responses.Add(reply);
                        break;

                    case RPL_ENDOFWHO:
                        this.taskSource.SetResult(this.Responses);
                        final = true;
                        break;

                    case ERR_NOSUCHSERVER:
                    case ERR_NOSUCHCHANNEL:
                        this.taskSource.SetException(new AsyncRequestErrorException(line));
                        final = true;
                        break;
                }

                return true;
            }

			protected internal override void OnFailure(Exception exception) => this.taskSource.SetException(exception);
		}

		/// <summary>
		/// An <see cref="AsyncRequest"/> that listens for a WHOX response.
		/// </summary>
		public class WhoxAsyncRequest : AsyncRequest {
			private static Dictionary<string, bool> replies = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                // Successful replies
                { RPL_WHOSPCRPL, false },
				{ RPL_ENDOFWHO, true },

                // Error replies
                { ERR_NOSUCHSERVER, true },
				{ ERR_NOSUCHCHANNEL, true },
			};

			private IrcClient client;
			private WhoxField[] fields;
			public ReadOnlyCollection<WhoxField> Fields { get; }
			private List<WhoResponse> responses = new List<WhoResponse>();
			public ReadOnlyCollection<WhoResponse> Responses { get; }

			private TaskCompletionSource<ReadOnlyCollection<WhoResponse>> taskSource { get; } = new TaskCompletionSource<ReadOnlyCollection<WhoResponse>>();
			public string Target { get; }
			public string QueryType { get; }
			/// <summary>Returns a <see cref="Task{TResult}"/> of <see cref="ReadOnlyCollection{T}"/> of <see cref="WhoResponse"/> representing the status of the request.</summary>
			public override Task Task => this.taskSource.Task;

			/// <summary>Initializes a new <see cref="WhoAsyncRequest"/> for the specified channel, associated with the specified <see cref="IrcClient"/>.</summary>
			public WhoxAsyncRequest(IrcClient client, string target, string queryType, params WhoxField[] fields) : this(client, target, queryType, (IList<WhoxField>) fields) { }
			/// <summary>Initializes a new <see cref="WhoAsyncRequest"/> for the specified channel, associated with the specified <see cref="IrcClient"/>.</summary>
			public WhoxAsyncRequest(IrcClient client, string target, string queryType, IList<WhoxField> fields) : base(replies, null) {
				if (queryType != null) {
					if (queryType == "")
						throw new ArgumentException("Query type string cannot be empty.", nameof(queryType));
					if (queryType[0] == ':' || queryType.Contains(" "))
						throw new ArgumentException("Query type string contains invalid characters.", nameof(queryType));
				}

				this.client = client;
				this.Target = target;
				this.QueryType = queryType;

				this.fields = fields.ToArray();
				this.Fields = Array.AsReadOnly(this.fields);
				this.Responses = this.responses.AsReadOnly();
			}

			protected internal override bool OnReply(IrcLine line, ref bool final) {
				switch (line.Message) {
					case RPL_WHOSPCRPL:
						if (line.Parameters.Length != this.fields.Length + 1) return false;

						var reply = new WhoResponse();

						for (int i = line.Parameters.Length - 1; i >= 1; --i) {
							switch (this.fields[i - 1]) {
								case WhoxField.QueryType:
									if (line.Parameters[i] != this.QueryType) return false;
									break;
								case WhoxField.Channel:
									reply.Channel = line.Parameters[i];
									break;
								case WhoxField.Ident:
									reply.Ident = line.Parameters[i];
									break;
								case WhoxField.Host:
									reply.Host = line.Parameters[i];
									break;
								case WhoxField.IPAddress:
									if (line.Parameters[i] != "255.255.255.255")
										reply.IPAddress = line.Parameters[i];
									break;
								case WhoxField.ServerName:
									reply.Server = line.Parameters[i];
									break;
								case WhoxField.Nickname:
									reply.Nickname = line.Parameters[i];
									break;
								case WhoxField.Flags:
									foreach (char flag in line.Parameters[i]) {
										switch (flag) {
											case 'G':
												reply.Away = true;
												break;
											case '*':
												reply.Oper = true;
												break;
											default:
												if (client.Extensions.StatusPrefix.TryGetValue(flag, out char mode)) {
													if (reply.ChannelStatus == null) reply.ChannelStatus = new ChannelStatus(this.client);
													reply.ChannelStatus.Add(mode);
												}
												break;
										}
									}
									break;
								case WhoxField.HopCount:
									if (line.Parameters[i] != "0")
										reply.HopCount = int.Parse(line.Parameters[i]);
									break;
								case WhoxField.IdleTime:
									if (line.Parameters[i] != "0")
										reply.IdleTime = int.Parse(line.Parameters[i]);
									break;
								case WhoxField.Account:
									if (line.Parameters[i] != "0")
										reply.Account = line.Parameters[i];
									break;
								case WhoxField.FullName:
									reply.FullName = line.Parameters[i];
									break;
							}
						}

						this.responses.Add(reply);
						break;

					case RPL_ENDOFWHO:
						this.taskSource.SetResult(this.Responses);
						final = true;
						break;

					case ERR_NOSUCHSERVER:
					case ERR_NOSUCHCHANNEL:
						this.taskSource.SetException(new AsyncRequestErrorException(line));
						final = true;
						break;
				}

				return true;
			}

			protected internal override void OnFailure(Exception exception) => this.taskSource.SetException(exception);
		}

		/// <summary>
		/// An <see cref="AsyncRequest"/> that listens for a WHOIS response.
		/// </summary>
		/// <remarks>
		///	The task, if successful, returns all of the data provided by the server in a <see cref="WhoisResponse"/> object.
		/// </remarks>
		public class WhoisAsyncRequest : AsyncRequest {
            private static Dictionary<string, bool> replies = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                // Successful replies
                { "275", false },
                { RPL_AWAY, false },
                { RPL_WHOISREGNICK, false },
                { "308", false },
                { "309", false },
                { "310", false },
                { RPL_WHOISUSER, false },
                { RPL_WHOISSERVER, false },
                { RPL_WHOISOPERATOR, false },
                { "316", false },
                { RPL_WHOISIDLE, false },
                { RPL_WHOISCHANNELS, false },
                { "320", false },
                { RPL_WHOISACCOUNT, false },
                { "703", false },

                // End of WHOIS list
                { RPL_ENDOFWHOIS, true },

                // Error replies
                { ERR_NOSUCHSERVER, true },
                { ERR_NONICKNAMEGIVEN, true },
                { ERR_NOSUCHNICK, true }
            };

            private IrcClient client;
            private WhoisResponse response;
            private IrcLine error;

            private TaskCompletionSource<WhoisResponse> taskSource { get; } = new TaskCompletionSource<WhoisResponse>();
            public string Target { get; }
			/// <summary>Returns a <see cref="Task{TResult}"/> of <see cref="WhoisResponse"/> representing the status of the request.</summary>
			public override Task Task => this.taskSource.Task;

			/// <summary>Initializes a new <see cref="WhoisAsyncRequest"/> for the specified nickname, associated with the specified <see cref="IrcClient"/>.</summary>
			public WhoisAsyncRequest(IrcClient client, string target) : base(replies, new[] { null, target }) {
                this.client = client;
                this.Target = target;
                this.response = new WhoisResponse(client);
            }

            protected internal override bool OnReply(IrcLine line, ref bool final) {
                response.lines.Add(line);

                switch (line.Message) {
                    case RPL_AWAY:
                        response.AwayMessage = line.Parameters[2];
                        break;
                    case RPL_WHOISREGNICK:
                        if (response.Account == null) response.Account = line.Parameters[1];
                        break;
                    case RPL_WHOISUSER:
                        response.Nickname = line.Parameters[1];
                        response.Ident = line.Parameters[2];
                        response.Host = line.Parameters[3];
                        response.FullName = line.Parameters[5];
                        break;
                    case RPL_WHOISSERVER:
                        response.ServerName = line.Parameters[2];
                        response.ServerInfo = line.Parameters[3];
                        break;
                    case RPL_WHOISOPERATOR:
                        response.Oper = true;
                        break;
                    case RPL_WHOISIDLE:
                        response.IdleTime = TimeSpan.FromSeconds(long.Parse(line.Parameters[2]));
                        if (line.Parameters.Length > 4)
                            response.SignonTime = IrcClient.DecodeUnixTime(long.Parse(line.Parameters[3]));
                        break;
                    case RPL_WHOISCHANNELS:
                        foreach (var token in line.Parameters[2].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
                            for (int i = 0; i < token.Length; ++i) {
                                if (this.client.Extensions.ChannelTypes.Contains(token[i])) {
                                    response.channels.Add(token.Substring(i), ChannelStatus.FromPrefix(this.client, token.Take(i)));
                                    break;
                                }
                            }
                        }
                        break;
                    case RPL_WHOISACCOUNT:
                        response.Account = line.Parameters[2];
                        break; 
                    case RPL_ENDOFWHOIS:
                        if (response.Nickname != null) {
                            this.taskSource.SetResult(response);
                        } else if (error != null) {
                            this.taskSource.SetException(new AsyncRequestErrorException(line));
                        } else {
                            this.taskSource.SetException(new IOException("The server did not send any response."));
                        }
                        final = true;
                        break;

                    case ERR_NOSUCHSERVER:
                    case ERR_NOSUCHNICK:
                    case ERR_NONICKNAMEGIVEN:
                        if (error == null) error = line;
                        break;
                }
                return true;
            }

			protected internal override void OnFailure(Exception exception) => this.taskSource.SetException(exception);
		}

		/// <summary>
		/// An <see cref="AsyncRequest"/> that listens for CTCP reply from a user.
		/// </summary>
		/// <remarks>
		///	The task, if successful, returns the parameters in the reply as a string.
		/// </remarks>
		public class CtcpAsyncRequest : AsyncRequest {
			private static Dictionary<string, bool> replies = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                // Successful replies
                { "NOTICE", false },

                // Error replies
				{ ERR_CANNOTSENDTOCHAN, false },
				{ ERR_NOTOPLEVEL, false },
				{ ERR_WILDTOPLEVEL, false },
				{ ERR_TOOMANYTARGETS, false },
				{ ERR_NOSUCHNICK, false },
			};

			private IrcClient client;

			private TaskCompletionSource<string> taskSource { get; } = new TaskCompletionSource<string>();
			private string target;
			private string request;
			/// <summary>Returns a <see cref="Task{TResult}"/> of <see cref="string"/> representing the status of the request.</summary>
			public override Task Task => this.taskSource.Task;

			public CtcpAsyncRequest(IrcClient client, string target, string request) : base(replies) {
				this.client = client;
				this.target = target;
				this.request = request;
			}

			protected internal override bool OnReply(IrcLine line, ref bool final) {
				if (client.CaseMappingComparer.Equals(line.Message, "NOTICE")) {
					if (line.Parameters[1].Length >= 2 && line.Parameters[1].StartsWith("\u0001") && line.Parameters[1].EndsWith("\u0001") &&
						client.CaseMappingComparer.Equals(Hostmask.GetNickname(line.Prefix), this.target) &&
						client.CaseMappingComparer.Equals(line.Parameters[0], client.Me.Nickname)) {
						var fields = line.Parameters[1].Substring(1, line.Parameters[1].Length - 2).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
						if (this.request.Equals(fields[0], StringComparison.InvariantCultureIgnoreCase)) {
							this.taskSource.SetResult(fields.Length >= 2 ? fields[1] : null);
							final = true;
							return true;
						}
					}
				} else if (line.Message[0] == '4') {
					if (client.CaseMappingComparer.Equals(line.Parameters[1], this.target)) {
						this.taskSource.SetException(new AsyncRequestErrorException(line));
						final = true;
						return true;
					}
				}
				return false;
			}

			protected internal override void OnFailure(Exception exception) => this.taskSource.SetException(exception);
		}

		/// <summary>
		/// Represents an <see cref="AsyncRequest"/> that waits for a message from a specific user to a specific target.
		/// </summary>
		public class MessageAsyncRequest : AsyncRequest {
			private static Dictionary<string, bool> repliesPrivmsg = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
                { "PRIVMSG", false },
			};
			private static Dictionary<string, bool> repliesNotice = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
				{ "NOTICE", false },
			};

			protected TaskCompletionSource<string> TaskSource { get; } = new TaskCompletionSource<string>();
			/// <summary>Returns a <see cref="Task{TResult}"/> of <see cref="string"/> representing the status of the request.</summary>
			public override Task Task => this.TaskSource.Task;

			public override bool CanTimeout => false;

			private IrcUser user;
			private IrcMessageTarget target;

			/// <summary>Initializes a new <see cref="MessageAsyncRequest"/> waiting for the specified type of message from the specified user to the specified target.</summary>
			/// <param name="user">The user to listen for a message from.</param>
			/// <param name="target">The entity to listen for a message to, which should be either the local user or a channel.</param>
			/// <param name="notice">Specifies whether to listen for a NOTICE instead of a PRIVMSG.</param>
			public MessageAsyncRequest(IrcUser user, IrcMessageTarget target, bool notice) : base(notice ? repliesNotice : repliesPrivmsg) {
				this.user = user;
				this.target = target;
			}

			protected internal override bool OnReply(IrcLine line, ref bool final) {
				if (this.user.Client.CaseMappingComparer.Equals(Hostmask.GetNickname(line.Prefix ?? this.user.Client.ServerName), this.user.Nickname) &&
					this.user.Client.CaseMappingComparer.Equals(line.Parameters[0], this.target.Target)) {
					this.TaskSource.SetResult(line.Parameters[1]);
					final = true;
				}
				return false;
			}

			protected internal override void OnFailure(Exception exception) => this.TaskSource.SetException(exception);
		}
	}

	/// <summary>
	/// The exception that is thrown on an <see cref="AsyncRequest"/> when an error reply is received from the server.
	/// </summary>
	[Serializable]
    public class AsyncRequestErrorException : Exception {
		/// <summary>Returns the error reply that was received.</summary>
        public IrcLine Line { get; }

        public AsyncRequestErrorException(IrcLine line) : base(line.Parameters[line.Parameters.Length - 1]) {
            this.Line = line;
        }
    }

    /// <summary>
    /// The exception that is thrown when an async request fails because the connection is lost.
    /// </summary>
    [Serializable]
    public class AsyncRequestDisconnectedException : Exception {
        /// <summary>Returns a <see cref="AnIRC.DisconnectReason"/> value indicating the cause of the disconnection.</summary>
        public DisconnectReason DisconnectReason { get; }
        private const string defaultMessage = "The request failed because the connection to the server was lost.";

        /// <summary>Initializes a new <see cref="AsyncRequestDisconnectedException"/> object with the specified <see cref="AnIRC.DisconnectReason"/> value.</summary>
        /// <param name="reason">A <see cref="AnIRC.DisconnectReason"/> value indicating the cause of the disconnection.</param>
        public AsyncRequestDisconnectedException(DisconnectReason reason) : base(defaultMessage) {
            this.DisconnectReason = reason;
        }
        /// <summary>Initializes a new <see cref="AsyncRequestDisconnectedException"/> object with the specified <see cref="AnIRC.DisconnectReason"/> value and inner exception.</summary>
        /// <param name="reason">A <see cref="AnIRC.DisconnectReason"/> value indicating the cause of the disconnection.</param>
        /// <param name="inner">The exception that caused or resulted from the disconnection.</param>
        public AsyncRequestDisconnectedException(DisconnectReason reason, Exception inner) : base(defaultMessage, inner) {
            this.DisconnectReason = reason;
        }
    }

}
