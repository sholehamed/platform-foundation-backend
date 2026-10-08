namespace Application.SharedKernel.Abstractions.Messaging;

/// <summary>Marker for durable, versioned application messages.</summary>
public interface IMessage { }

/// <summary>
/// Stable serialized identity. Changing a published contract requires a new version,
/// not a rename of a CLR class.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class MessageContractAttribute(string name) : Attribute
{
    public string Name { get; } = !string.IsNullOrWhiteSpace(name)
        ? name : throw new ArgumentException("Message name cannot be empty.", nameof(name));
}
