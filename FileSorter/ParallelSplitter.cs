using Shared;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace FileSorter;

/// <summary>
/// Splits a large input file into sorted chunks using a pipelined approach:
/// Reader -> Channel -> Parallel Sorters -> Channel -> Sequential Writer.
/// Supports optimization for HDD (sequential I/O enforcement) and SSD (full parallelism).
/// </summary>
public class ParallelSplitter
{
	private const int ReportThresholdBytes = 100 * 1024 * 1024; // Log progress every 100MB

	private const string ComponentName = "Splitter";

	/// <summary>
	/// Reads the input file, splits it into chunks of approximately <paramref name="maxMemoryChunkMb"/> size,
	/// sorts them in parallel, and writes them to temporary files.
	/// </summary>
	/// <returns>A list of paths to the created sorted chunk files.</returns>
	public static async Task<List<string>> SplitAndSortAsync(
		string inputFile,
		string tempDirectory,
		int sorterCount = 0,
		int maxMemoryChunkMb = 200,
		int channelCount = 0,
		bool optimizeForHdd = true)
	{
		// --- Configuration Tuning ---
		// For 200MB chunks: 4 sorters, Channel Capacity 2 -> ~1.5-2GB Active RAM.
		// For 100MB chunks: 8-10 sorters could be used, but 4 is usually enough for HDD IO.
		// Limit channel capacity to prevent OOM if Reader is faster than Writer.
		int channelCapacity = channelCount > 0 ? channelCount :
			maxMemoryChunkMb >= 200 ? 2 : 4;
		int maxSorters = sorterCount > 0 ? sorterCount : 
			maxMemoryChunkMb >= 200 ? 4 : Math.Max(1, Environment.ProcessorCount - 2);

		AppLogger.Info(ComponentName, $"Starting Parallel Splitter. HDD Optimization: {optimizeForHdd}");
		AppLogger.Info(ComponentName, $"Chunk Size: {maxMemoryChunkMb}MB | Sorters: {maxSorters} | Channel Capacity: {channelCapacity}");

		var sw = Stopwatch.StartNew();

		using var cts = new CancellationTokenSource();

		// 1. Channels for the pipeline
		var sortChannel = Channel.CreateBounded<ChunkData>(new BoundedChannelOptions(channelCapacity)
		{
			SingleWriter = true,
			SingleReader = false,
			FullMode = BoundedChannelFullMode.Wait
		});

		var writeChannel = Channel.CreateBounded<ChunkData>(new BoundedChannelOptions(channelCapacity)
		{
			SingleWriter = false,
			SingleReader = true,
			FullMode = BoundedChannelFullMode.Wait
		});

		// 2. Synchronization for HDD (prevents simultaneous Read and Write)
		// For SSD - allow high concurrency.
		var ioLock = new SemaphoreSlim(optimizeForHdd ? 1 : 100);

		var generatedFilesBag = new ConcurrentBag<string>();
		var sorterStats = new ConcurrentBag<ReportStats>();

		// --- STAGE 1: READER ---
		var readerTask = RunReaderAsync(inputFile, maxMemoryChunkMb, ioLock, sortChannel.Writer, cts.Token);

		// --- STAGE 2: SORTERS (Parallel CPU) ---
		var sorterTasks = new Task[maxSorters];
		for (int sorterId = 0; sorterId < maxSorters; sorterId++)
		{
			int id = sorterId;
			sorterTasks[sorterId] = Task.Run(() => RunSorterAsync(id, sortChannel.Reader, writeChannel.Writer, sorterStats, cts.Token), cts.Token);
		}

		var sortersSupervisor = Task.Run(async () =>
		{
			try
			{
				await Task.WhenAll(sorterTasks);

				// Close write channel when all sorters are done
				writeChannel.Writer.Complete();
			}
			catch (Exception ex)
			{
				writeChannel.Writer.Complete(ex);
				throw;
			}
		}, cts.Token);

		
		//var sortersWaiter = Task.WhenAll(sorterTasks).ContinueWith(_ => writeChannel.Writer.Complete());

		// --- STAGE 3: WRITER (Sequential IO) ---
		var writerTask = RunWriterAsync(tempDirectory, ioLock, writeChannel.Reader, generatedFilesBag, cts.Token);

		// Wait for pipeline completion
		await Task.WhenAll(readerTask, sortersSupervisor, writerTask);

		sw.Stop();

		long fileSize = new FileInfo(inputFile).Length;
		long fileSizeMb = fileSize / 1024 / 1024;
		AppLogger.LogStats(ComponentName, sw.Elapsed, fileSizeMb, sorterStats);

		return generatedFilesBag.OrderBy(x => x).ToList();
	}

	// --- WORKER METHODS ---

	/// <summary>
	/// Reads the input file sequentially, parses rows, and pushes chunks to the sorting channel.
	/// </summary>
	private static async Task RunReaderAsync(
		string inputFile,
		int chunkLimitMb,
		SemaphoreSlim ioLock,
		ChannelWriter<ChunkData> output,
		CancellationToken token)
	{
		// SequentialScan hint for OS file cache
		await using var fs = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
		using var reader = new StreamReader(fs);

		int chunkIndex = 0;
		long sizeInMb = fs.Length;
		long maxBytesInMemory = chunkLimitMb * 1024L * 1024L;
		long currentBytes = 0;

		// Progress tracking variables
		long lastReportBytes = 0;
		long lastReportTimeMs = 0;
		var progressWatch = Stopwatch.StartNew();

		// Pre-allocate list capacity to reduce resizing overhead (heuristic: ~50 bytes per row)
		int estimatedCapacity = (chunkLimitMb * 1024 * 1024) / 50;
		var currentRows = new List<Row>(estimatedCapacity);

		string? line;

		try
		{
			// Acquire IO lock for reading
			await ioLock.WaitAsync(token);
			try
			{
				while ((line = await reader.ReadLineAsync()) is not null)
				{
					token.ThrowIfCancellationRequested();

					if (line.Length == 0)
					{
						continue;
					}

					int dotIndex = line.IndexOf('.');
					if (dotIndex == -1)
					{
						continue;
					}

					// Zero-allocation parsing logic is inside Row.TryParse
					if (Row.TryParse(line, out Row row))
					{
						currentRows.Add(row);
						currentBytes += (line.Length * 2) + 20;
					}

					long bytesReadTotal = fs.Position;
					if (bytesReadTotal - lastReportBytes >= ReportThresholdBytes)
					{
						long currentTotalMs = progressWatch.ElapsedMilliseconds;
						long chunkTimeMs = currentTotalMs - lastReportTimeMs;
						if (chunkTimeMs == 0) chunkTimeMs = 1;

						double percent = (double)bytesReadTotal / sizeInMb * 100;
						double speed = (bytesReadTotal - lastReportBytes) / 1024d / 1024d / (chunkTimeMs / 1000d);

						AppLogger.Info(ComponentName,
							$"{percent,5:F1}% | {bytesReadTotal / 1024 / 1024,5} MB / {sizeInMb / 1024 / 1024,5} MB | Speed: {speed,6:F1} MB/s");

						lastReportBytes = bytesReadTotal;
						lastReportTimeMs = currentTotalMs;
					}

					if (currentBytes >= maxBytesInMemory)
					{
						// Release IO lock allows Writer to flush data while we prepare the next batch
						ioLock.Release();

						// Push to Sorters
						await output.WriteAsync(new ChunkData
						{
							Index = chunkIndex++,
							Rows = currentRows
						});

						// Start new chunk
						currentRows = new List<Row>(estimatedCapacity);
						currentBytes = 0;

						// Re-acquire IO lock for reading
						await ioLock.WaitAsync(token);
					}
				}

				// Push remaining data
				if (currentRows.Count > 0)
				{
					ioLock.Release();
					await output.WriteAsync(new ChunkData
					{
						Index = chunkIndex++,
						Rows = currentRows
					});
				}
				else
				{
					ioLock.Release();
				}
			}
			finally
			{
				output.Complete();
			}
		}
		catch (Exception ex)
		{
			output.Complete(ex);

			throw;
		}
	}

	/// <summary>
	/// Consumes chunks from the Reader, sorts them in-memory (CPU bound), and pushes to the Writer.
	/// </summary>
	private static async Task RunSorterAsync(
		int id,
		ChannelReader<ChunkData> input,
		ChannelWriter<ChunkData> output,
		ConcurrentBag<ReportStats> statsBag,
		CancellationToken token)
	{
		var stats = new ReportStats { Id = id };
		var totalWatch = Stopwatch.StartNew();
		var workWatch = new Stopwatch();

		await foreach (var chunk in input.ReadAllAsync(token))
		{
			workWatch.Start();

			// CPU Bound Operation: Sort in memory
			chunk.Rows.Sort();

			workWatch.Stop();

			stats.ItemProcessed++;

			// Send to Writer
			await output.WriteAsync(chunk, token);
		}

		totalWatch.Stop();
		stats.TotalTime = totalWatch.Elapsed;
		stats.WorkingTime = workWatch.Elapsed;
		statsBag.Add(stats);
	}

	/// <summary>
	/// Consumes sorted chunks and writes them to temporary files on disk sequentially.
	/// </summary>
	private static async Task RunWriterAsync(
		string tempDirectory,
		SemaphoreSlim ioLock,
		ChannelReader<ChunkData> input,
		ConcurrentBag<string> filesBag,
		CancellationToken token)
	{
		const int WriteBuffer = 4 * 1024 * 1024;
		string? fileName = null;

		try
		{
			await foreach (var chunk in input.ReadAllAsync(token))
			{
				// Wait for IO access (if Reader is currently reading, we wait)
				await ioLock.WaitAsync(token);
				try
				{
					fileName = Path.Combine(tempDirectory, $"chunk_{chunk.Index:000}.tmp");

					var watch = Stopwatch.StartNew();
					await using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, WriteBuffer);
					await using var writer = new StreamWriter(fs, Encoding.UTF8, WriteBuffer);

					foreach (var row in chunk.Rows)
					{
						token.ThrowIfCancellationRequested();
						await writer.WriteLineAsync(row.FullLine.AsMemory(), token);
					}

					filesBag.Add(fileName);
					fileName = null;

					watch.Stop();
					// Explicit cleanup to help GC (LOH management)
					chunk.Rows.Clear();
					chunk.Rows = null!;

					AppLogger.Debug(ComponentName, $"-> Chunk {chunk.Index} saved. Write Time: {watch.ElapsedMilliseconds}ms");
				}
				finally
				{
					ioLock.Release();
				}
			}
		}
		catch (Exception)
		{
			if (fileName is not null && File.Exists(fileName))
			{
				try
				{
					File.Delete(fileName);
					AppLogger.Debug(ComponentName, $"Cleaned up partial file: {fileName}");
				}
				catch
				{ 
					/* Ignore delete errors during crash */
				}
			}
			throw;
		}
	}
}