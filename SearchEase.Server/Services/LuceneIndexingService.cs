using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Options;
using SearchEase.Server.Configuration;
using Directory = System.IO.Directory;

namespace SearchEase.Server.Services;

public class LuceneIndexingService : BackgroundService
{
    private readonly IndexingConfiguration _config;
    private readonly ILogger<LuceneIndexingService> _logger;
    private readonly FSDirectory _directory;
    private readonly StandardAnalyzer _analyzer;
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

    public LuceneIndexingService(
        IOptions<IndexingConfiguration> config,
        ILogger<LuceneIndexingService> logger)
    {
        _config = config.Value;
        _logger = logger;

        var indexPath = Path.Combine(AppContext.BaseDirectory, _config.IndexPath);
        Directory.CreateDirectory(indexPath);

        _directory = FSDirectory.Open(indexPath);
        _analyzer = new StandardAnalyzer(AppLuceneVersion);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IndexFilesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(_config.IndexingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while indexing files");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // Wait before retrying
            }
        }
    }

    private async Task IndexFilesAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_config.FolderToIndex) || !Directory.Exists(_config.FolderToIndex))
        {
            _logger.LogWarning("Folder to index is not configured or does not exist: {Folder}", _config.FolderToIndex);
            return;
        }

        _logger.LogInformation("Starting indexing of folder: {Folder}", _config.FolderToIndex);

        using var writer = new IndexWriter(_directory, new IndexWriterConfig(AppLuceneVersion, _analyzer));
        writer.DeleteAll(); // Clear existing index

        var files = Directory.GetFiles(_config.FolderToIndex, "*.*", SearchOption.AllDirectories)
            .Where(f => _config.FileExtensionsToIndex.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var file in files)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var doc = await CreateDocumentAsync(file);
                writer.AddDocument(doc);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing file: {File}", file);
            }
        }

        writer.Commit();
        _logger.LogInformation("Completed indexing of {Count} files", files.Count());
    }

    private async Task<Document> CreateDocumentAsync(string filePath)
    {
        var doc = new Document
        {
            new StringField("path", filePath, Field.Store.YES),
            new StringField("filename", Path.GetFileName(filePath), Field.Store.YES),
            new StringField("extension", Path.GetExtension(filePath).ToLowerInvariant(), Field.Store.YES),
        };

        string content = await File.ReadAllTextAsync(filePath);
        doc.Add(new TextField("content", content, Field.Store.NO));

        return doc;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _analyzer?.Dispose();
        _directory?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
