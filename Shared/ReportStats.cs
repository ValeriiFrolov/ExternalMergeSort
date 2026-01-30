namespace Shared;

public struct ReportStats
{
	public int Id;
	public int ItemProcessed;
	public TimeSpan WorkingTime; // Pure CPU time (sorting)
	public TimeSpan TotalTime;   // Thread lifetime
}
