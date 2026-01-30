using FileSorter;

namespace Tests;

[TestFixture]
public class ChunkStreamTests : BaseTest
{
	/// <summary>
	/// Verifies that a clean file with valid rows is read sequentially.
	/// </summary>
	[Test]
	public void Constructor_And_MoveNext_ReadValidLinesCorrectly()
	{
		// Arrange
		var filePath = CreateFile("valid_data.tmp",
			"1. Apple",
			"10. Banana",
			"2. Cherry");

		// Act
		using var stream = new ChunkStream(filePath, 1024);

		// Assert
		Assert.True(stream.HasData, "Stream should have data initially.");
		Assert.That(stream.CurrentRow.Number, Is.EqualTo(1));
		Assert.That(stream.CurrentRow.FullLine, Is.EqualTo("1. Apple"));

		stream.MoveNext();
		Assert.True(stream.HasData);
		Assert.That(stream.CurrentRow.Number, Is.EqualTo(10));
		Assert.That(stream.CurrentRow.FullLine, Is.EqualTo("10. Banana"));

		stream.MoveNext();
		Assert.True(stream.HasData);
		Assert.That(stream.CurrentRow.Number, Is.EqualTo(2));
		Assert.That(stream.CurrentRow.FullLine, Is.EqualTo("2. Cherry"));

		stream.MoveNext();
		Assert.False(stream.HasData, "Stream should indicate no data after EOF.");
	}

	/// <summary>
	/// Verifies that empty lines and unparseable garbage are skipped without errors.
	/// </summary>
	[Test]
	public void MoveNext_SkipsEmptyLines_And_Garbage()
	{
		// Arrange
		var filePath = CreateFile("dirty_data.tmp",
			"1. First",
			"",                 // Empty
            "   ",              // Whitespace
            "InvalidLine",      // No number
            "123 NoDot",        // Format error
            ". NoNumber",       // Format error
            "2. Second"
		);


		// Act
		using var stream = new ChunkStream(filePath, 1024);

		// Assert
		Assert.True(stream.HasData);
		Assert.That(stream.CurrentRow.Number, Is.EqualTo(1));

		stream.MoveNext();
		Assert.True(stream.HasData);
		Assert.That(stream.CurrentRow.Number, Is.EqualTo(2));

		stream.MoveNext();
		Assert.False(stream.HasData);
	}

	/// <summary>
	/// Verifies that an empty file results in HasData = false immediately.
	/// </summary>
	[Test]
	public void Constructor_HandlesEmptyFile()
	{
		// Arrange
		var filePath = CreateFile("empty.tmp");

		// Act
		using var stream = new ChunkStream(filePath, 1024);

		// Assert
		Assert.False(stream.HasData, "Empty file should result in no data.");
	}

	/// <summary>
	/// Verifies that a file containing ONLY garbage results in HasData = false.
	/// </summary>
	[Test]
	public void Constructor_HandlesFileWithOnlyGarbage()
	{
		// Arrange
		var filePath = CreateFile("garbage.tmp", "Bad1", "Bad2", "   ");

		// Act
		using var stream = new ChunkStream(filePath, 1024);

		// Assert
		Assert.False(stream.HasData, "File with no valid rows should result in no data.");
	}

	/// <summary>
	/// Verifies correct behavior with a very small buffer to ensure 
	/// no data is lost across buffer boundaries.
	/// </summary>
	[Test]
	public void Read_WithSmallBuffer_WorksCorrectly()
	{
		// Arrange
		var lines = CreateFile("small_buf.tmp", "1. LongStringData", "2. Data");

		// Act
		using var stream = new ChunkStream(lines, 4);

		// Assert
		Assert.True(stream.HasData);
		Assert.That(stream.CurrentRow.Number, Is.EqualTo(1));

		stream.MoveNext();
		Assert.True(stream.HasData);
		Assert.That(stream.CurrentRow.Number, Is.EqualTo(2));
	}

	/// <summary>
	/// Verifies that the constructor throws FileNotFoundException if the file is missing.
	/// </summary>
	[Test]
	public void Constructor_Throws_IfFileDoesNotExist()
	{
		Assert.Throws<FileNotFoundException>(() =>
			new ChunkStream("NonExistentFile.tmp", 1024));
	}
}