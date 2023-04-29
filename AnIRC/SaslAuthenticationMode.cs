namespace AnIRC;

public enum SaslAuthenticationMode {
	/// <summary>SASL authentication will not be used.</summary>
	Disabled,
	/// <summary>SASL authentication will be used if available, and the connection will continue if authentication is not successful.</summary>
	UseIfAvailable,
	/// <summary>SASL authentication will be used if available, and the connection will be aborted if authentication is not successful.</summary>
	Required
}