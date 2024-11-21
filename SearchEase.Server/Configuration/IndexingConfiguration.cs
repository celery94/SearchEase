namespace SearchEase.Server.Configuration;

public class IndexingConfiguration
{
    public string IndexPath { get; set; } = "LuceneIndex";
    public string FolderToIndex { get; set; } = string.Empty;
    public int IndexingIntervalSeconds { get; set; } = 300; // 5 minutes
    public string[] FileExtensionsToIndex { get; set; } = new[] { ".txt", ".md", ".cs", ".json" };
}
