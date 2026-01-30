using Shared;

namespace Tests;

public abstract class BaseTest
{
	public string _testDir;

	[SetUp]
	public void Setup()
	{
		_testDir = Path.Combine(Path.GetTempPath(), "MergerTests_" + Guid.NewGuid());
		Directory.CreateDirectory(_testDir);

		AppLogger.EnableDebug = false;
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_testDir))
		{
			Directory.Delete(_testDir, true);
		}
	}

	public string CreateFile(string name, params string[] lines)
	{
		string path = Path.Combine(_testDir, name);
		File.WriteAllLines(path, lines);
		return path;
	}
}
