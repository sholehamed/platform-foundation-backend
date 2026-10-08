namespace Application.SharedKernel.Models;

/// <summary>Represents successful completion of a command without a return value.</summary>
public readonly record struct Unit
{
    public static Unit Value => default;
}
