using Shared;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace TestFileGenerator;

/// <summary>
/// Generates massive test files using a Producer-Consumer pattern.
/// optimized for high throughput (HDD/SSD).
/// </summary>
public class ParallelFileGenerator
{
	private const int FileBuffer = 4 * 1024 * 1024; // 4MB Write Buffer
	private const int BatchSizeChars = 64 * 1024;   // 64KB text batch per producer push
	private const int ReportThresholdBytes = 100 * 1024 * 1024; // Log progress every 100MB

	private const string ComponentName = "Generator";

	// Test Data Dictionary
	private static readonly string[] Dictionary =
	{
		"Apple", "Banana is yellow", "Cherry is the best", "Something something something",
		"Avocado", "Zero", "This is a very long string to test memory allocation",
		"Short", "A", "Zebra", "123 test 321"
	};

	/// <summary>
	/// Generates a file of the specified size populated with random lines "Number. Text".
	/// </summary>
	public static async Task GenerateAsync(string filePath, long sizeInBytes, int producerCount)
	{
		string fullPath = Path.GetFullPath(filePath);
		long sizeInMb = sizeInBytes / 1024 / 1024;

		AppLogger.Info(ComponentName, $"Target: {fullPath}");
		AppLogger.Info(ComponentName, $"Size: {sizeInMb:N0} MB | Producers: {producerCount}");

		var globalStopwatch = Stopwatch.StartNew();

		// Cancellation token to stop producers once the target size is reached
		using var cts = new CancellationTokenSource();

		// Channel Setup:
		// Capacity limit ensures we don't exhaust RAM if Disk I/O is slow.
		var channelOptions = new BoundedChannelOptions(1000)
		{
			SingleReader = true,
			SingleWriter = false,
			FullMode = BoundedChannelFullMode.Wait
		};

		var channel = Channel.CreateBounded<string>(channelOptions);

		// --- Start Workflow ---

		// Writer (Consumer) - Controls the stopping condition (size limit)
		var writerTask = Task.Run(() => RunWriterAsync(fullPath, sizeInBytes, channel.Reader, cts, globalStopwatch));

		// Producers - Generate data until cancelled
		AppLogger.Info(ComponentName, $"Producers count: {producerCount}");

		var sorterStats = new ConcurrentBag<ReportStats>();
		var producerTasks = new Task[producerCount];
		for (int producerId = 0; producerId < producerCount; producerId++)
		{
			int id = producerId;
			producerTasks[id] = Task.Run(() => RunProducerAsync(id, channel.Writer, cts.Token, sorterStats));
		}

		// 3. Wait for Writer to finish (it controls the stop condition)
		await writerTask;

		// 4. Cleanup
		// Writer finished -> Size reached -> Cancel producers
		// channel.Writer.Complete() is implied/handled by cancellation in producers
		channel.Writer.TryComplete();
		globalStopwatch.Stop();

		await Task.WhenAll(producerTasks);

		AppLogger.LogStats(ComponentName, globalStopwatch.Elapsed, sizeInMb, sorterStats);
		//LogStats(globalStopwatch.Elapsed, sizeInMb, results);
	}

	/// <summary>
	/// Consumes text chunks from the channel and writes them to disk.
	/// Cancels the CTS when the target size is reached.
	/// </summary>
	private static async Task RunWriterAsync(
		string fullPath,
		long targetSizeBytes,
		ChannelReader<string> input,
		CancellationTokenSource cts,
		Stopwatch globalStopwatch)
	{
		long currentBytes = 0;
		long lastReportBytes = 0;
		long lastReportTimeMs = 0;

		// FileOptions.WriteThrough ensures data is flushed to physical disk (benchmark accuracy),
		// but for generation speed we usually stick to None (OS Cache). 
		// Use FileOptions.WriteThrough only if you want to test raw disk write speed.
		await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, FileBuffer, FileOptions.None);
		await using var writer = new StreamWriter(fs, Encoding.UTF8, FileBuffer);

		try
		{
			await foreach (var chunk in input.ReadAllAsync())
			{
				await writer.WriteLineAsync(chunk);

				currentBytes += chunk.Length; // Approx (UTF-16 length close enough for progress)

				// Reporting logic
				if (currentBytes - lastReportBytes >= ReportThresholdBytes)
				{
					long currentTotalMs = globalStopwatch.ElapsedMilliseconds;
					long chunkTimeMs = currentTotalMs - lastReportTimeMs;
					if (chunkTimeMs == 0)
					{
						chunkTimeMs = 1;
					}

					double percent = (double)currentBytes / targetSizeBytes * 100;
					double speed = (currentBytes - lastReportBytes) / 1024d / 1024d / (chunkTimeMs / 1000d);

					AppLogger.Info(ComponentName,
						$"{percent,5:F1}% |" +
						$"{currentBytes / 1024 / 1024,5} MB / {targetSizeBytes / 1024 / 1024,5} MB | " +
						$"Chunk: {chunkTimeMs,5}ms (~{speed:F1} MB/s) | " +
						$"Total: {globalStopwatch.Elapsed:mm\\:ss}");

					lastReportBytes = currentBytes;
					lastReportTimeMs = currentTotalMs;
				}

				// Проверка завершения (чтобы не писать лишнего)
				if (currentBytes >= targetSizeBytes)
				{
					cts.Cancel(); // Stop producers
					return;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected when CTS is cancelled
		}
	}

	/// <summary>
	/// Generates random text lines and pushes them to the channel.
	/// </summary>
	private static async Task RunProducerAsync(
		int id,
		ChannelWriter<string> output,
		CancellationToken cts,
		ConcurrentBag<ReportStats> statsBag)
	{
		var stats = new ReportStats { Id = id };
		var rnd = Random.Shared;
		var totalWatch = Stopwatch.StartNew();
		var workWatch = new Stopwatch();

		var sb = new StringBuilder(BatchSizeChars + 200);

		while (!cts.IsCancellationRequested)
		{
			try
			{
				workWatch.Start();
				sb.Clear();

				// Generate a batch of lines to reduce Channel overhead
				while (sb.Length < BatchSizeChars)
				{
					string text = Dictionary[rnd.Next(Dictionary.Length)];
					int number = rnd.Next(0, int.MaxValue);

					sb.Append(number);
					sb.Append(". ");
					sb.AppendLine(text);
				}

				workWatch.Stop();

				await output.WriteAsync(sb.ToString());

				stats.ItemProcessed++;
			}
			catch (OperationCanceledException)
			{
				// Normal exit
			}
			catch (ChannelClosedException)
			{
				// Normal exit
			}
		}

		stats.TotalTime = totalWatch.Elapsed;
		stats.WorkingTime = workWatch.Elapsed;
		statsBag.Add(stats);
	}
	/*
	private static void LogStats(TimeSpan totalTime, long sizeInMb, ReportStats[] stats)
	{

		AppLogger.Debug(ComponentName, new string('-', 60));
		AppLogger.Debug(ComponentName, "DIAGNOSTICS (Producer Stats):");
		AppLogger.Debug(ComponentName, $"Total Time: {totalTime.TotalSeconds:F2} sec");
		AppLogger.Debug(ComponentName, $"{"ID",-4} | {"Generated",-12} | {"Wait Time",-12} | {"% Waiting",-8}");

		foreach (var stat in stats)
		{
			double waitPercent = (stat.TimeSpentWaiting.TotalMilliseconds / totalTime.TotalMilliseconds) * 100;
			AppLogger.Debug(ComponentName, $"{stat.Id,-4} | {stat.ItemsGenerated,-12:N0} | {stat.TimeSpentWaiting.TotalSeconds,-12:F2} s | {waitPercent,-8:F1}%");
		}

		AppLogger.Info(ComponentName, new string('-', 60));
		AppLogger.Info(ComponentName, $"Done, Total Time: {totalTime}");
		AppLogger.Info(ComponentName, $"Avg Speed: {(sizeInMb / totalTime.TotalSeconds):F1} MB/s");
	}
	*/
}