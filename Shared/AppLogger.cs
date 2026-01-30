using System.Collections.Concurrent;

namespace Shared;

/// <summary>
/// Centralized logger to ensure consistent output format across the application.
/// </summary>
public static class AppLogger
{
	public static bool EnableDebug { get; set; } = true;

	public static void Info(string component, string message)
		=> Log(component, message, ConsoleColor.Gray);

	public static void Debug(string component, string message)
	{
		if (EnableDebug)
		{
			Log(component, message, ConsoleColor.DarkGray);
		}
	}

	public static void Error(string component, string message)
		=> Log(component, message, ConsoleColor.Red);

	public static void LogStats(string component, TimeSpan totalTime, long sizeInMb, ConcurrentBag<ReportStats> stats)
	{
		AppLogger.Debug(component, new string('-', 60));
		AppLogger.Debug(component, $"{component} stats:");
		AppLogger.Debug(component, $"{"ID",-3} | {"Items",-6} | {"Work Time",-12} | {"Total Time",-12} | {"Idle %",-8} | {"Avg/Item",-10}");

		foreach (var stat in stats.OrderBy(s => s.Id))
		{
			double idlePercent = 100.0 * (1.0 - (stat.WorkingTime.TotalMilliseconds / stat.TotalTime.TotalMilliseconds));
			double avgSort = stat.ItemProcessed > 0 ? stat.WorkingTime.TotalMilliseconds / stat.ItemProcessed : 0;
			AppLogger.Debug(component, $"{stat.Id,-3} | {stat.ItemProcessed,-6} | {stat.WorkingTime,-12:mm\\:ss\\.f} | {stat.TotalTime,-12:mm\\:ss\\.f} | {idlePercent,6:F1} % | {avgSort,7:F0} ms");
		}

		AppLogger.Info(component, new string('-', 60));
		AppLogger.Info(component, $"Done. Total time: {totalTime}");
		AppLogger.Info(component, $"Avg Speed: {(sizeInMb / totalTime.TotalSeconds):F1} MB/s");
	}

	private static void Log(string component, string message, ConsoleColor color)
	{
		var prevColor = Console.ForegroundColor;
		Console.ForegroundColor = color;
		Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{component}] {message}");
		Console.ForegroundColor = prevColor;
	}
}