﻿namespace AnIRC;

/// <summary>
/// Specifies the reason a join command failed.
/// </summary>
public enum ChannelJoinDeniedReason {
	/// <summary>The join failed for some other reason.</summary>
	Other,
	/// <summary>The join failed because the channel has reached its population limit.</summary>
	Limit,
	/// <summary>The join failed because the channel is invite only.</summary>
	InviteOnly,
	/// <summary>The join failed because we are banned.</summary>
	Banned,
	/// <summary>The join failed because we did not give the correct key.</summary>
	KeyFailure
}

/// <summary>
/// Specifies the cause of a disconnection from a server.
/// </summary>
public enum DisconnectReason {
	/// <summary>No reason was specified or no disconnection occurred.</summary>
	/// <remarks>
	///     This value may be returned by the in the <see cref="AsyncRequestDisconnectedException.DisconnectReason"/> property
	///     if the connection was not lost but the server did not respond as expected.
	/// </remarks>
	Unknown,
	/// <summary>The <see cref="IrcClient.Disconnect"/> method was called.</summary>
	ClientDisconnected,
	/// <summary>The server is closing the connection as a result of a QUIT command from the client.</summary>
	Quit,
	/// <summary>The server did not respond to a ping.</summary>
	PingTimeout,
	/// <summary>The server closed the connection unexpectedly.</summary>
	ServerDisconnected,
	/// <summary>A fatal exception occurred while reading data.</summary>
	Exception,
	/// <summary>The TLS authentication failed.</summary>
	TlsAuthenticationFailed,
	/// <summary>The SASL authentication failed.</summary>
	SaslAuthenticationFailed,
	/// <summary>There was a desynchronisation due to a server configuration change.</summary>
	CaseMappingCollision
}

/// <summary>
/// Specifies a user's gender.
/// </summary>
public enum Gender {
	Unspecified,
	Male,
	Female,
	Bot
}

/// <summary>Used to report the state of the IRC client.</summary>
public enum IrcClientState {
	/// <summary>The client is not connected.</summary>
	Disconnected,
	/// <summary>The client is establishing a TCP connection.</summary>
	Connecting,
	/// <summary>The client is making an TLS handshake before logging in.</summary>
	TlsHandshaking,
	/// <summary>The client is registering to IRC.</summary>
	Registering,
	/// <summary>The client is negotiating capabilities with the server.</summary>
	CapabilityNegotiating,
	/// <summary>The client is authenticating using SASL.</summary>
	SaslAuthenticating,
	/// <summary>The client is has successfully registered and is receiving server info.</summary>
	ReceivingServerInfo,
	/// <summary>The client is online on IRC.</summary>
	Online
}
