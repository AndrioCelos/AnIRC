namespace AnIRC;

/// <summary>
/// Represents an IRCv3 capability.
/// </summary>
public class IrcCapability : INamedEntity {
	public string Name { get; }
	public string? Parameter { get; }

	public IrcCapability(string name) : this(name, null) { }
	public IrcCapability(string name, string? parameter) {
		this.Name = name;
		this.Parameter = parameter;
	}

	public override int GetHashCode() => this.Name.GetHashCode();
	public override bool Equals(object? other) => other is IrcCapability capability && this.Name == capability.Name;
	public override string? ToString() => this.Parameter is not null ? $"{{{this.Name}={this.Parameter}}}" : $"{{{this.Name}}}";
}
