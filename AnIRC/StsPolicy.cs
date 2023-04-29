using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnIRC;

public class StsPolicy {
	public DateTime Expiry { get; private set; }
	public TimeSpan Duration { get; }
	public bool Preload { get; }

	public bool IsValid => DateTime.UtcNow < this.Expiry;

	public StsPolicy(TimeSpan duration, bool preload) {
		this.Duration = duration;
		this.Preload = preload;
		this.RefreshExpiry();
	}

	public void RefreshExpiry() => this.Expiry = DateTime.UtcNow + this.Duration;
}
