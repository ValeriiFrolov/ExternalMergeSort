using Shared;
using System.Diagnostics;

namespace FileSorter;
internal class Program
{
	static async Task Main(string[] args)
	{
		// Default settings
		string input = "data.txt";
		string output = "result.txt";
		string tempDir = "temp_chunks";
		int sorterCount = 2;
		int chunkSize = 200; // MB
		int channelCount = 2;
		bool optimizeHdd = true;

		for (int ind = 0; ind < args.Length; ind++)
		{
			try
			{
				switch (args[ind])
				{
					case "--input": input = args[++ind]; break;
					case "--output": output = args[++ind]; break;
					case "--temp": tempDir = args[++ind]; break;
					case "--chunk-size": int.TryParse(args[++ind], out chunkSize); break;
					case "--hdd-mode": bool.TryParse(args[++ind], out optimizeHdd); break;
					case "--cores": int.TryParse(args[++ind], out sorterCount); break;
					case "--channels": int.TryParse(args[++ind], out channelCount); break;
				}
			}
			catch (IndexOutOfRangeException)
			{
				AppLogger.Error("CLI", $"Argument {args[ind]} requires a value.");
				return;
			}
		}

		if (sorterCount < 1 || sorterCount > Environment.ProcessorCount - 1)
		{
			sorterCount = 2;
		}

		if (chunkSize <= 0)
		{
			chunkSize = 200;
		}

		if (channelCount <= 0)
		{
			channelCount = 2;
		}

		if (string.IsNullOrWhiteSpace(input))
		{
			input = "data.txt";
		}

		if (string.IsNullOrWhiteSpace(output))
		{
			output = "result.txt";
		}

		if (string.IsNullOrWhiteSpace(tempDir))
		{
			tempDir = "temp_chunks";
		}

		if (!File.Exists(input))
		{
			AppLogger.Error("Error", $"Input file not found: {input}");
			return;
		}

		if (Directory.Exists(tempDir))
		{
			Directory.Delete(tempDir, true);
		}
		Directory.CreateDirectory(tempDir);

		var sw = Stopwatch.StartNew();

		try
		{
			var chunks = await ParallelSplitter.SplitAndSortAsync(input, tempDir, sorterCount, chunkSize, channelCount, optimizeHdd);

			MultiPassMerger.MergeResult(chunks, output);

			sw.Stop();

			using var proc = Process.GetCurrentProcess();
			long peakMemoryBytes = proc.PeakWorkingSet64;
			double peakMemoryMb = peakMemoryBytes / 1024d / 1024d;

			long fileSize = new FileInfo(input).Length;
			double speedMb = (fileSize / 1024d / 1024d) / sw.Elapsed.TotalSeconds;

			string statsData = $"{sw.Elapsed};{peakMemoryMb:F0};{speedMb:F0}";
			File.WriteAllText("last_run_stats.txt", statsData);

			AppLogger.Info("Program", new string('=', 60));
			AppLogger.Info("Program", $"FULL CYCLE COMPLETED IN: {sw.Elapsed}");
			AppLogger.Info("Program", $"Peak RAM: {peakMemoryMb:N0} MB");
			AppLogger.Info("Program", $"Avg Speed: {speedMb:N0} MB/s");

			AppLogger.Info("Program", "Head of result.txt:");
			using var reader = new StreamReader(output);
			for (int i = 0; i < 10; i++)
			{
				string? line = reader.ReadLine();
				if (line != null) AppLogger.Info("Program", $"  {line}");
			}
		}
		catch (Exception ex)
		{
			AppLogger.Error("Critical", ex.Message);
			AppLogger.Error("Trace", ex.StackTrace ?? "");
		}
		finally
		{
			if (Directory.Exists(tempDir))
			{
				Directory.Delete(tempDir, true);
			}
			}
		}
}