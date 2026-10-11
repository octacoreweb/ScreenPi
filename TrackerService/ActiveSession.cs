namespace TrackerService;

/// <summary>
/// In-RAM buffer for the currently active window session.
/// Duration is computed on-the-fly; flushed to DB on window switch,
/// idle trigger, or every 5 minutes.
/// </summary>
internal class ActiveSession
{
    public long? DbId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime LastFlushTime { get; set; }
    public double FlushedDurationSeconds { get; set; }

    /// <summary>
    /// Total duration = already-flushed portion + time since last flush.
    /// </summary>
    public double CurrentDurationSeconds =>
        FlushedDurationSeconds + (DateTime.UtcNow - LastFlushTime).TotalSeconds;
}
