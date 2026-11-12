namespace WhisperWriter.Services.Inference.Memory;

/// <summary>Physical RAM currently available to the process environment.</summary>
public record SystemBudget (ulong TotalPhysicalBytes, ulong AvailablePhysicalBytes);