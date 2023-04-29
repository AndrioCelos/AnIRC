using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnIRC;

public enum TlsMode {
	/// <summary>The connection will be in plaintext only.</summary>
	Plaintext,
	/// <summary>The connection will be upgraded to TLS using STARTTLS if the server supports it.</summary>
	StartTlsOptional,
	/// <summary>The conenction will be upgraded to TLS using STARTTLS or aborted if this is not possible.</summary>
	StartTlsRequired,
	/// <summary>The connection will be over TLS from the start.</summary>
	Tls
}
