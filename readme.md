AnIRC
=====

The IRC client implementation that is used by [my robot](https://github.com/AndrioCelos/CBot). The aim is to provide simple enough support for modern features of IRC, but it isn't really complete.

The `IrcClient` class is the main class of the project. When a connection is established, a new thread is started to read messages. All message-related events are run on this thread.

Features
--------

* Parsing of KVIrc gender codes.
* CTCP request parsing: there are currently different events for CTCP and non-CTCP messages.
* [`RPL_ISUPPORT`](https://tools.ietf.org/html/draft-brocklesby-irc-isupport-03) extension parsing, and use of many tokens.
* Won't necessarily crash on servers that use non-standard status prefixes.
* [IRCv3.2 capability negotiation](http://ircv3.net/irc/#capability-negotiation), and use of `account-notify`, `extended-join`, `multi-prefix` and `sasl`.
* SASL PLAIN authentication.
* `async` methods for asynchronous requests and waiting for messages.

Example usage
-------------

```Csharp
IrcClient client;

void Initialize() {
	client = new IrcClient(new IrcLocalUser("Nickname", "Ident", "Full name"));
	client.ChannelMessage += OnChannelMessage;
	client.Connect("localhost", 6667);
}

async void OnChannelMessage(object sender, ChannelMessageEventArgs e) {
	try {
		var client = ((IrcClient) sender);

		if (e.Message.StartsWith(".ban ")) {
			if (e.Channel.Me.Status < ChannelStatus.Halfop) {
				e.Sender.Notice($"I'm not an op in {e.Channel.Name}.");
				return;
			}

			var nickname = e.Message.Substring(5).Trim();
			if (client.Users.TryGetValue(nickname, out var user) && user.Host != "*") {
				e.Channel.Ban("*!*@" + user.Host);
			} else {
				var result = await ((IrcClient) sender).WhoisAsync(e.Message.Substring(5).Trim());
				e.Channel.Ban("*!*@" + result.Host);
			}
		}
	} catch (AsyncRequestErrorException ex) when (ex.Line.Message == Replies.ERR_NOSUCHNICK) {
		e.Sender.Notice("No such nickname.");
	} catch (Exception ex) {
		e.Sender.Notice("The command failed: " + ex.Message);
	}
}
```
