using TestFileGenerator;

namespace Tests;

[TestFixture]
public class ParallelFileGeneratorTests : BaseTest
{
	[Test]
	public async Task GenerateAsync_CreatesFileOfApproximateSize()
	{
		// Arrange
		string file = Path.Combine(_testDir, "test_10mb.txt");
		long targetBytes = 10 * 1024 * 1024;

		// Act
		await ParallelFileGenerator.GenerateAsync(file, targetBytes, 2);

		// Assert
		Assert.True(File.Exists(file), "File should exist");

		var fileInfo = new FileInfo(file);
		// Size won't be exact due to batching, but should be close (>= target)
		// Since we check limit after writing a batch (64KB), overshoot is small.
		Assert.That(fileInfo.Length, Is.GreaterThanOrEqualTo(targetBytes));
		Assert.That(fileInfo.Length, Is.LessThan(targetBytes + 500 * 1024)); // Should not overshoot by > 500KB
	}

	[Test]
	public async Task GenerateAsync_ContentHasCorrectFormat()
	{
		// Arrange
		string file = Path.Combine(_testDir, "format_test.txt");
		long targetBytes = 1 * 1024 * 1024;

		// Act
		await ParallelFileGenerator.GenerateAsync(file, targetBytes, 1);

		// Assert
		// Read first few lines to verify format "Number. Text"
		var lines = File.ReadLines(file).Take(10).ToList();

		Assert.IsNotEmpty(lines);
		foreach (var line in lines)
		{
			// Check for ". " delimiter
			int dotIndex = line.IndexOf(". ");
			Assert.That(dotIndex, Is.GreaterThan(0), $"Line '{line}' is missing delimiter '. '");

			// Check number part
			string numPart = line.Substring(0, dotIndex);
			bool isNum = long.TryParse(numPart, out _);
			Assert.True(isNum, $"Prefix '{numPart}' should be a number");

			// Check text part is not empty
			string textPart = line.Substring(dotIndex + 2);
			Assert.IsNotEmpty(textPart);
		}
	}
}