using System.Text;

namespace FileSorter;

/// <summary>
/// Wraps a StreamReader to read a sorted chunk file line by line.
/// Acts as an iterator for the PriorityQueue during the Merge phase.
/// </summary>
public class ChunkStream : IDisposable
{
	private readonly StreamReader _reader;

	/// <summary>
	/// The current parsed row available for processing.
	/// Acts as the Priority Key for sorting.
	/// </summary>
	public Row CurrentRow;

	/// <summary>
	/// Indicates whether the stream still has data to read.
	/// If false, the chunk is exhausted.
	/// </summary>
	public bool HasData;

	public ChunkStream(string filePath, int bufferSize)
	{
		// FileOptions.SequentialScan is a hint to the OS caching system 
		// to read ahead, boosting performance on HDD during the merge phase.
		var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
		_reader = new StreamReader(fs, Encoding.UTF8, false, bufferSize);

		MoveNext();
	}

	/// <summary>
	/// Advances to the next valid row in the stream.
	/// Skips empty or malformed lines.
	/// </summary>
	public void MoveNext()
	{
		string? line;

		while ((line = _reader.ReadLine()) is not null)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			if (Row.TryParse(line, out var row))
			{
				CurrentRow = row;
				HasData = true;
				return;
			}
		}

		// End of stream reached
		HasData = false;
	}

	public void Dispose() => _reader.Dispose();
}