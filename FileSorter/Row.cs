namespace FileSorter;

/// <summary>
/// Represents a parsed line from the generated file.
/// Designed as a lightweight struct to minimize GC pressure during processing.
/// Implements custom comparison logic: Text part first (Alphabetical), then Number part.
/// </summary>
public readonly struct Row : IComparable<Row>
{
	/// <summary>
	/// The numeric part of the line (e.g., 415 in "415. Apple").
	/// </summary>
	public readonly long Number;

	/// <summary>
	/// The raw original line string. We keep the reference to avoid creating new substrings.
	/// </summary>
	public readonly string FullLine;

	/// <summary>
	/// The starting index of the text part within <see cref="FullLine"/>.
	/// Used to slice the string as a Span during comparison.
	/// </summary>
	public readonly int TextStartIndex;

	public Row(long number, string fullLine, int textStartIndex)
	{
		Number = number;
		FullLine = fullLine;
		TextStartIndex = textStartIndex;
	}

	/// <summary>
	/// Compares this row with another based on the sorting rules:
	/// 1. Alphabetical (Ordinal) comparison of the text part.
	/// 2. Numerical comparison of the number part (if text parts are equal).
	/// </summary>
	public int CompareTo(Row other)
	{
		// Zero-allocation slicing using Spans
		var thisSpan = FullLine.AsSpan(TextStartIndex);
		var otherSpan = other.FullLine.AsSpan(other.TextStartIndex);

		// Ordinal comparison is the fastest and most stable for standard ASCII/UTF8 data
		int strComp = thisSpan.CompareTo(otherSpan, StringComparison.Ordinal);

		if (strComp == 0)
		{
			return Number.CompareTo(other.Number);
		}

		return strComp;
	}

	/// <summary>
	/// Attempts to parse a raw line into a Row struct without allocating new strings (Zero-Allocation).
	/// Expected format: "NUMBER. TEXT" (e.g., "123. Apple is red").
	/// </summary>
	public static bool TryParse(string line, out Row row)
	{
		row = default;

		// 1. Find the delimiter
		int dotIndex = line.IndexOf('.');
		if (dotIndex == -1)
		{
			return false;
		}

		// 2. Parse number using Span to avoid 'Substring' allocation
		ReadOnlySpan<char> numberSpan = line.AsSpan(0, dotIndex);
		if (!long.TryParse(numberSpan, out long number))
		{
			return false;
		}

		// 3. Determine where the text starts
		// Skip ". " (2 chars) if present, otherwise handle boundary cases
		int textStart = dotIndex + 1;
		if (textStart < line.Length && line[textStart] == ' ')
		{
			textStart++;
		}

		row = new Row(number, line, textStart);
		return true;
	}
}