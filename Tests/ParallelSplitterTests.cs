using FileSorter;

namespace Tests;

[TestFixture]
public class ParallelSplitterTests : BaseTest
{
	[Test]
	public async Task SplitAndSortAsync_CreatesSortedChunks()
	{
		// Arrange
		var lines = new[]
		{
			"100. Apple",
			"1. Banana",
			"50. Cherry",
			"2. Apple",
			"10. Banana"
		};
		var inputFile = CreateFile("input.txt", lines);
		var tempDir = Path.Combine(_testDir, "chunks");
		Directory.CreateDirectory(tempDir);

		// Act
		// Use a very small chunk size (1MB) to force creation of at least one chunk (or multiple if configured purely by size)
		// Note: Our logic uses estimated bytes. For 5 lines, likely 1 chunk will be created unless we mock memory limit.
		// However, the test verifies the sorting logic regardless of chunk count.
		var chunks = await ParallelSplitter.SplitAndSortAsync(inputFile, tempDir, maxMemoryChunkMb: 1);

		// Assert
		Assert.IsNotEmpty(chunks, "Should create at least one chunk.");

		foreach (var chunkPath in chunks)
		{
			Assert.True(File.Exists(chunkPath));

			var chunkLines = await File.ReadAllLinesAsync(chunkPath);
			Assert.That(chunkLines.Length, Is.GreaterThan(0));

			Assert.True(chunkLines.Any(l => l.Contains("Apple")), "Chunk should contain data");

			for (int i = 0; i < chunkLines.Length - 1; i++)
			{
				Row.TryParse(chunkLines[i], out var r1);
				Row.TryParse(chunkLines[i + 1], out var r2);
				Assert.LessOrEqual(r1.CompareTo(r2), 0, $"Line {i} is not smaller than line {i + 1}");
			}
		}
	}

	[Test]
	public async Task SplitAndSortAsync_HandlesLargeVolume_SplitsIntoMultipleFiles()
	{
		// Arrange
		var tempDir = Path.Combine(_testDir, "chunks_multi");
		Directory.CreateDirectory(tempDir);

		// Create enough data to trigger splitting logic. 
		// We set maxMemoryChunkMb to 1. We need > 1MB of data.
		// Average line ~20 bytes. 1MB = ~50k lines.
		int lineCount = 60_000;
		var largeFile = Path.Combine(_testDir, "large_input.txt");

		using (var writer = new StreamWriter(largeFile))
		{
			for (int i = 0; i < lineCount; i++)
			{
				await writer.WriteLineAsync($"{i}. TestStringData");
			}
		}

		// Act
		var chunks = await ParallelSplitter.SplitAndSortAsync(largeFile, tempDir, maxMemoryChunkMb: 1);

		// Assert
		Assert.That(chunks.Count, Is.GreaterThan(1), "Should split into multiple chunks due to memory limit.");

		long totalLines = 0;
		foreach (var chunk in chunks)
		{
			totalLines += File.ReadLines(chunk).Count();
		}
		Assert.That(totalLines, Is.EqualTo(lineCount), "Total lines in chunks should match input.");
	}

	[Test]
	public async Task SplitAndSortAsync_HandlesEmptyFile()
	{
		// Arrange
		var emptyFile = CreateFile("empty.txt");
		var tempDir = Path.Combine(_testDir, "chunks_empty");
		Directory.CreateDirectory(tempDir);

		// Act
		var chunks = await ParallelSplitter.SplitAndSortAsync(emptyFile, tempDir);

		// Assert
		Assert.IsEmpty(chunks, "Should produce no chunks for empty input.");
	}
}