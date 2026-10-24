namespace Cad2Gis.Core.Models;

/// <summary>
/// Authoritative working data source mode for a pipeline step.
/// Exactly two options per step: PreviousStep (default) or OwnData.
/// </summary>
public enum StepSource
{
    PreviousStep,
    OwnData
}

/// <summary>
/// Persisted reference to verified step output in HelperFiles\state\pipeline_state.json
/// Records verified output of each step so subsequent steps or restarted sessions reliably consume it.
/// </summary>
public class StepOutputRecord
{
    public int Step { get; set; } = 3;
    public string Path { get; set; } = string.Empty;
    public int FeatureCount { get; set; }
    public int Wkid { get; set; }
    public DateTime WrittenAt { get; set; } = DateTime.Now;
    
    // Backwards compatibility for existing tests
    public DateTime Timestamp
    {
        get => WrittenAt;
        set => WrittenAt = value;
    }

    public string ContentStamp { get; set; } = string.Empty;
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
}
