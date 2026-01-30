using FileSorter;

namespace Tests;

[TestFixture]
public class RowTests
{
	[Test]
	public void TryParse_ValidFormat_ReturnsTrueAndParsesCorrectly()
	{
		string input = "415. Apple";

		bool result = Row.TryParse(input, out var row);

		Assert.True(result);
		Assert.That(row.Number, Is.EqualTo(415));
		Assert.That(row.FullLine, Is.EqualTo(input));

		Assert.That(row.FullLine[row.TextStartIndex], Is.EqualTo('A'));
	}

	[Test]
	public void TryParse_NoSpaceAfterDot_ParsesCorrectly()
	{
		string input = "1.Apple";

		bool result = Row.TryParse(input, out var row);

		Assert.True(result);
		Assert.That(row.Number, Is.EqualTo(1));
		Assert.That(row.FullLine[row.TextStartIndex], Is.EqualTo('A'));
	}

	[Test]
	public void TryParse_InvalidFormat_ReturnsFalse()
	{
		Assert.False(Row.TryParse("Not a number. Text", out _));
		Assert.False(Row.TryParse("123 No dot here", out _));
		Assert.False(Row.TryParse("", out _));
	}

	[Test]
	public void CompareTo_SortsAlphabetically_ByTextFirst()
	{
		// Arrange
		Row r1 = Parse("1. Apple");
		Row r2 = Parse("1. Banana");

		// Act & Assert
		Assert.That(r1.CompareTo(r2), Is.LessThan(0));
		Assert.That(r2.CompareTo(r1), Is.GreaterThan(0));
	}

	[Test]
	public void CompareTo_SortsByNumber_WhenTextIsEqual()
	{
		// Arrange
		Row r1 = Parse("10. Apple");
		Row r2 = Parse("2. Apple");

		// Act & Assert
		Assert.That(r2.CompareTo(r1), Is.LessThan(0));
		Assert.That(r1.CompareTo(r2), Is.GreaterThan(0));
	}

	[Test]
	public void CompareTo_SortsCaseSensitive_Ordinal()
	{
		// Ordinal: 'Z' (90) < 'a' (97)
		Row r1 = Parse("1. Zebra");
		Row r2 = Parse("1. apple");

		Assert.That(r1.CompareTo(r2), Is.LessThan(0));
	}

	private Row Parse(string s)
	{
		Row.TryParse(s, out var r);
		return r;
	}
}