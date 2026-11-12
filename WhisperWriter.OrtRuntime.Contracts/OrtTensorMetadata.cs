namespace WhisperWriter.OrtRuntime.Contracts;

/// <summary>Metadata for one named input or output tensor.</summary>
public class OrtTensorMetadata {
	public string Name { get; }
	public OrtTensorDataType DataType { get; }
	public IReadOnlyList<int> Dimensions { get; }

	public OrtTensorMetadata (string name, OrtTensorDataType dataType, IEnumerable<int> dimensions) {
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(dimensions);
		this.Name = name;
		this.DataType = dataType;
		this.Dimensions = Array.AsReadOnly(dimensions.ToArray());
	}
}