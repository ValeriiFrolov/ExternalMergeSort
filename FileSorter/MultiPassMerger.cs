using Shared;
using System.Diagnostics;
using System.Text;

namespace FileSorter;

/// <summary>
/// Handles the K-Way Merge phase of the external sort algorithm.
/// Supports multi-pass merging if the number of chunks exceeds the open file limit.
/// </summary>
public class MultiPassMerger
{
	// Buffer settings tuned for HDD performance to reduce seek time.
	// 4MB Read Buffer * 15 files = ~60MB RAM usage.
	private const int ReadBufferSize = 4 * 1024 * 1024;

	// 16MB Write Buffer to write in large sequential blocks.
	private const int WriteBufferSize = 16 * 1024 * 1024;

	private const int ReportThresholdBytes = 100 * 1024 * 1024;

	private const string ComponentName = "Merger";

	/// <summary>
	/// Merges sorted chunk files into a single output file.
	/// If the number of chunks exceeds <paramref name="maxFanIn"/>, it performs intermediate merge passes.
	/// </summary>
	/// <param name="initialChunks">List of paths to sorted chunk files.</param>
	/// <param name="finalOutputPath">Path for the final result.</param>
	/// <param name="maxFanIn">Maximum number of files to merge concurrently (Default: 15 for HDD).</param>
	public static void MergeResult(List<string> initialChunks, string finalOutputPath, int maxFanIn = 15)
	{
		AppLogger.Info(ComponentName, $"Starting Multi-Pass Merge. Total chunks: {initialChunks.Count}");

		var currentChunks = new List<string>(initialChunks);
		int passCount = 1;
		string tempDir = Path.GetDirectoryName(initialChunks[0])
			?? throw new ArgumentException("Invalid chunk path");

		long currentWrittenBytes = 0;
		long totalBatchSizeBytes = initialChunks.Sum(f => new FileInfo(f).Length);

		// While we have more chunks than we can merge in one go...
		while (currentChunks.Count > maxFanIn)
		{
			AppLogger.Info(ComponentName, $"Pass {passCount}. Chunks: {currentChunks.Count}. Merging in batches of {maxFanIn}...");
			var newChunks = new List<string>();

			// Split current chunks into batches (Chunks of chunks :))
			var batches = currentChunks.Chunk(maxFanIn).ToList();
			int batchIndex = 0;

			foreach (var batch in batches)
			{
				// Create a temporary file for this batch's result
				string intermediateFile = Path.Combine(tempDir, $"pass{passCount}_part{batchIndex}.tmp");

				currentWrittenBytes = MergeLowLevel(batch, intermediateFile, currentWrittenBytes, totalBatchSizeBytes);
				newChunks.Add(intermediateFile);

				// Delete processed input chunks to free up disk space immediately
				foreach (var oldFile in batch)
				{
					if (File.Exists(oldFile))
					{
						File.Delete(oldFile);
					}
				}

				batchIndex++;
			}

			currentChunks = newChunks;
			passCount++;
			currentWrittenBytes = 0;
		}

		AppLogger.Info(ComponentName, $"Final Pass. Merging last {currentChunks.Count} files.");
		currentWrittenBytes = MergeLowLevel(currentChunks, finalOutputPath, currentWrittenBytes, totalBatchSizeBytes);

		foreach (var file in currentChunks)
		{
			if (file != finalOutputPath && file.Contains("pass"))
				File.Delete(file);
		}

		AppLogger.Info(ComponentName, new string('-', 60));
		AppLogger.Info(ComponentName, "Merge completed successfully.");
	}

	/// <summary>
	/// Performs the actual K-Way merge using a PriorityQueue.
	/// </summary>
	private static long MergeLowLevel(
		IList<string> files,
		string outputFile,
		long currentWrittenBytes,
		long totalBatchSizeBytes)
	{
		// Reporting vars
		long lastReportBytes = currentWrittenBytes;
		long lastReportTimeMs = 0;
		var progressWatch = Stopwatch.StartNew();

		AppLogger.Debug(ComponentName, $"Merging {files.Count} files -> {Path.GetFileName(outputFile)}");

		var queue = new PriorityQueue<ChunkStream, Row>();
		var openStreams = new List<ChunkStream>(files.Count);

		try
		{
			foreach (var file in files)
			{
				var chunk = new ChunkStream(file, ReadBufferSize);
				openStreams.Add(chunk);

				if (chunk.HasData)
				{
					queue.Enqueue(chunk, chunk.CurrentRow);
				}
			}

			using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, WriteBufferSize);
			using var writer = new StreamWriter(fs, Encoding.UTF8, WriteBufferSize);

			while (queue.TryDequeue(out ChunkStream? minChunk, out Row minRow))
			{
				writer.WriteLine(minRow.FullLine);

				// Estimation: string chars (1 byte mostly for ASCII/UTF8) + NewLine (2 bytes)
				// This is approximate but fast.
				currentWrittenBytes += minRow.FullLine.Length + Environment.NewLine.Length;
				if (currentWrittenBytes - lastReportBytes >= ReportThresholdBytes)
				{
					long currentTotalMs = progressWatch.ElapsedMilliseconds;
					long chunkTimeMs = currentTotalMs - lastReportTimeMs;
					if (chunkTimeMs == 0) chunkTimeMs = 1;

					double percent = (double)currentWrittenBytes / totalBatchSizeBytes * 100;
					// Clamp percent to 99.9% visually because estimation might slightly differ from file size
					if (percent > 100) percent = 100;

					double speed = (currentWrittenBytes - lastReportBytes) / 1024d / 1024d / (chunkTimeMs / 1000d);

					AppLogger.Info(ComponentName,
						$"{percent,5:F1}% | {currentWrittenBytes / 1024 / 1024,5} MB / {totalBatchSizeBytes / 1024 / 1024,5} MB | Speed: {speed,6:F1} MB/s");

					lastReportBytes = currentWrittenBytes;
					lastReportTimeMs = currentTotalMs;
				}

				minChunk.MoveNext();

				if (minChunk.HasData)
				{
					queue.Enqueue(minChunk, minChunk.CurrentRow);
				}
				else
				{
					minChunk.Dispose();
				}
			}
		}
		finally
		{
			foreach (var s in openStreams)
			{
				s.Dispose();
			}
		}

		return currentWrittenBytes;
	}
}