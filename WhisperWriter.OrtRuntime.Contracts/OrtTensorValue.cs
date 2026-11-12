namespace WhisperWriter.OrtRuntime.Contracts;

/// <summary>Named tensor values passed between the host and a provider-specific runtime plugin.</summary>
public class OrtTensorValue {
	public string Name { get; }
	public OrtTensorDataType DataType { get; }
	public int[] Dimensions { get; }
	public Array Values { get; }

	public OrtTensorValue (string name, OrtTensorDataType dataType, int[] dimensions, Array values) {
		this.Name = name;
		this.DataType = dataType;
		this.Dimensions = dimensions;
		this.Values = values;
	}
}