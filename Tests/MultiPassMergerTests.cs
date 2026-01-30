using FileSorter;

namespace Tests;

[TestFixture]
public class MultiPassMergerTests : BaseTest
{

	/// <summary>
	/// Verifies that a simple K-Way merge (all files fit in one pass) works correctly.
	/// </summary>
	[Test]
	public void MergeResult_SinglePass_SortsCorrectly()
	{
		// Arrange
		var file1 = CreateFile("chunk_01.tmp", "10. Apple", "30. Cherry");
		var file2 = CreateFile("chunk_02.tmp", "2. Banana", "100. Zebra");
		var file3 = CreateFile("chunk_03.tmp", "1. Avocado");

		var output = Path.Combine(_testDir, "result.txt");
		var inputs = new List<string> { file1, file2, file3 };

		// Act
		MultiPassMerger.MergeResult(inputs, output, maxFanIn: 10);

		// Assert
		var resultLines = File.ReadAllLines(output);
		var expected = new[]
		{
			"10. Apple",
			"1. Avocado",
			"2. Banana",
			"30. Cherry",
			"100. Zebra"
		};

		Assert.That(resultLines, Is.EqualTo(expected));
	}

	/// <summary>
	/// Verifies that the Multi-Pass logic triggers when input files > maxFanIn.
	/// It ensures intermediate files are created and the final result is sorted.
	/// </summary>
	[Test]
	public void MergeResult_MultiPass_SortsCorrectly()
	{
		// Arrange
		// We create 4 files, but set MaxFanIn to 2.
		// Expected behavior: 
		// Pass 1: Merge (1,2) -> tempA, Merge (3,4) -> tempB
		// Pass 2: Merge (tempA, tempB) -> result

		var f1 = CreateFile("c1.tmp", "4. D");
		var f2 = CreateFile("c2.tmp", "1. A");
		var f3 = CreateFile("c3.tmp", "3. C");
		var f4 = CreateFile("c4.tmp", "2. B");

		var output = Path.Combine(_testDir, "result_multi.txt");
		var inputs = new List<string> { f1, f2, f3, f4 };

		// Act
		MultiPassMerger.MergeResult(inputs, output, maxFanIn: 2);

		// Assert
		var resultLines = File.ReadAllLines(output);
		var expected = new[] { "1. A", "2. B", "3. C", "4. D" };

		Assert.That(resultLines, Is.EqualTo(expected));

		// Ensure input chunks were cleaned up
		Assert.False(File.Exists(f1), "Input chunk 1 should be deleted");
		Assert.False(File.Exists(f4), "Input chunk 4 should be deleted");
	}

	/// <summary>
	/// Verifies correct handling of lines with equal text parts (should sort by number).
	/// </summary>
	[Test]
	public void MergeResult_HandlesCollisions_ByNumber()
	{
		// Arrange
		var f1 = CreateFile("c1.tmp", "10. Apple", "20. Apple");
		var f2 = CreateFile("c2.tmp", "2. Apple", "5. Apple");

		var output = Path.Combine(_testDir, "result_collision.txt");

		// Act
		MultiPassMerger.MergeResult(new List<string> { f1, f2 }, output);

		// Assert
		var resultLines = File.ReadAllLines(output);
		var expected = new[]
		{
			"2. Apple",
			"5. Apple",
			"10. Apple",
			"20. Apple"
		};

		Assert.That(resultLines, Is.EqualTo(expected));
	}
}