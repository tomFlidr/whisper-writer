namespace WhisperWriter.Services;

/// <summary>Physical RAM currently available to the process environment.</summary>
public sealed record SystemMemoryBudget(ulong TotalPhysicalBytes, ulong AvailablePhysicalBytes);
