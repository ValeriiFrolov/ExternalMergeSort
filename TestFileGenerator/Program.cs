using Shared;
using System.Diagnostics;

namespace TestFileGenerator;

internal class Program
{
	static async Task Main(string[] args)
	{
		// Default settings
		string output = "data.txt";
		int producerCount = 2;
		long sizeInBytes = 1L * 1024 * 1024 * 1024;

		for (int ind = 0; ind < args.Length; ind++)
		{
			try
			{
				switch (args[ind])
				{
					case "--output": output = args[++ind]; break;
					case "--cores": int.TryParse(args[++ind], out producerCount); break;
					case "--size":
						if (double.TryParse(args[++ind], out double sizeInGb))
						{
							sizeInBytes = (long)(sizeInGb * 1024 * 1024 * 1024);
						}
						break;
				}
			}
			catch (IndexOutOfRangeException)
			{
				AppLogger.Error("CLI", $"Argument {args[ind]} requires a value.");
				return;
			}
		}

		if (producerCount < 1 || producerCount > Environment.ProcessorCount - 1)
		{
			producerCount = 2;
		}

		if (sizeInBytes <= 0)
		{
			sizeInBytes = 1L * 1024 * 1024 * 1024;
		}

		if (string.IsNullOrWhiteSpace(output))
		{
			output = "data.txt";
		}

		var sw = Stopwatch.StartNew();

		try
		{
			await ParallelFileGenerator.GenerateAsync(output, sizeInBytes, producerCount);

			sw.Stop();

			AppLogger.Info("Program", new string('=', 60));
			AppLogger.Info("Program", $"FULL CYCLE COMPLETED IN: {sw.Elapsed}");
		}
		catch (Exception ex)
		{
			AppLogger.Error("Critical", ex.Message);
			AppLogger.Error("Trace", ex.StackTrace ?? "");
		}
	}
}