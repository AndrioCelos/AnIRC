using System;
using System.Collections.Generic;
using System.Text;

namespace AnIRC;

/// <summary>Provides a SASL authentication mechanism.</summary>
public abstract class SaslMechanism {
	/// <summary>When overridden, returns the name used in the IRC protocol to refer to this mechanism.</summary>
	public abstract string Name { get; }

	/// <summary>Returns a <see cref="SaslMechanism"/> providing the EXTERNAL mechanism.</summary>
	public static SaslMechanism External { get; } = new ExternalSaslMechanism();
	/// <summary>Returns a <see cref="SaslMechanism"/> providing the EXTERNAL mechanism that will be used over TLS only.</summary>
	public static SaslMechanism ExternalTlsOnly { get; } = new ExternalTlsOnlySaslMechanism();
	/// <summary>Returns a <see cref="SaslMechanism"/> providing the PLAIN mechanism.</summary>
	public static SaslMechanism Plain { get; } = new PlainSaslMechanism();
	/// <summary>Returns a <see cref="SaslMechanism"/> providing the PLAIN mechanism that will be used over TLS only.</summary>
	public static SaslMechanism PlainTlsOnly { get; } = new PlainTlsOnlySaslMechanism();

	protected SaslMechanism() { }

	/// <summary>When overridden, returns a value indicating whether authentication can be attempted using this mechanism.</summary>
	public abstract bool CanAttempt(IrcClient ircClient);
	/// <summary>When overridden, prepares to authenticate using this mechanism.</summary>
	/// <returns>An optional state object to pass to <see cref="Respond"/>.</returns>
	public abstract object? Initialise(IrcClient ircClient);
	/// <summary>When overridden, responds to a SASL message.</summary>
	/// <returns>A sequence of bytes to send in the response, or null to abort authentication.</returns>
	public abstract ArraySegment<byte>? Respond(IrcClient client, ArraySegment<byte> data, object? state);

	internal class PlainSaslMechanism : SaslMechanism {
		public override string Name => "PLAIN";

		public override bool CanAttempt(IrcClient ircClient) => ircClient.SaslUsername is not null && ircClient.SaslPassword is not null;
		public override object? Initialise(IrcClient ircClient) => null;
		public override ArraySegment<byte>? Respond(IrcClient client, ArraySegment<byte> data, object? state) {
			if (data.Count > 0) throw new ArgumentException("Unknown SASL challenge.");
			if (client.SaslUsername is null || client.SaslPassword is null) throw new InvalidOperationException("Missing credentials");

			var usernameSize = Encoding.UTF8.GetByteCount(client.SaslUsername);
			var passwordSize = Encoding.UTF8.GetByteCount(client.SaslPassword);

			var bytes = new byte[usernameSize * 2 + passwordSize + 2];
			Encoding.UTF8.GetBytes(client.SaslUsername, 0, client.SaslUsername.Length, bytes, 0);
			Encoding.UTF8.GetBytes(client.SaslUsername, 0, client.SaslUsername.Length, bytes, usernameSize + 1);
			Encoding.UTF8.GetBytes(client.SaslPassword, 0, client.SaslPassword.Length, bytes, (usernameSize + 1) * 2);
			return new(bytes);
		}
	}

	internal class PlainTlsOnlySaslMechanism : PlainSaslMechanism {
		public override bool CanAttempt(IrcClient ircClient) => ircClient.SslStream is not null && base.CanAttempt(ircClient);
	}

	internal class ExternalSaslMechanism : SaslMechanism {
		public override string Name => "EXTERNAL";

		public override bool CanAttempt(IrcClient ircClient) => true;
		public override object? Initialise(IrcClient ircClient) => null;
		public override ArraySegment<byte>? Respond(IrcClient client, ArraySegment<byte> data, object? state)
			=> data.Count == 0
				? client.SaslUsername is not null
					? new(Encoding.UTF8.GetBytes(client.SaslUsername))
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
					: ArraySegment<byte>.Empty
#else
					: new(Array.Empty<byte>())
#endif
				: throw new ArgumentException("Unknown SASL challenge.");
	}

	internal class ExternalTlsOnlySaslMechanism : ExternalSaslMechanism {
		public override bool CanAttempt(IrcClient ircClient) => ircClient.SslStream is not null && base.CanAttempt(ircClient);
	}
}
