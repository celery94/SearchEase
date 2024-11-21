using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Options;
using NPOI.XWPF.UserModel;
using SearchEase.Server.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;
using Directory = System.IO.Directory;
using Document = Lucene.Net.Documents.Document;

namespace SearchEase.Server.Services;

public class LuceneIndexingService : BackgroundService
{
    private readonly IndexingConfiguration _config;
    private readonly ILogger<LuceneIndexingService> _logger;
    private readonly FSDirectory _directory;
    private readonly StandardAnalyzer _analyzer;
    private readonly ConcurrentDictionary<string, DateTime> _fileIndexTimes;
    private readonly string _indexTimesPath;
    private FileSystemWatcher _watcher;

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
        _indexTimesPath = Path.Combine(indexPath, "index_times.json");
        _fileIndexTimes = LoadIndexTimes();

        // Initialize file system watcher
        InitializeFileWatcher();
    }

    private ConcurrentDictionary<string, DateTime> LoadIndexTimes()
    {
        try
        {
            if (File.Exists(_indexTimesPath))
            {
                var json = File.ReadAllText(_indexTimesPath);
                var indexTimes = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);
                return new ConcurrentDictionary<string, DateTime>(indexTimes ?? new Dictionary<string, DateTime>());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading index times from {Path}", _indexTimesPath);
        }

        return new ConcurrentDictionary<string, DateTime>();
    }

    private void SaveIndexTimes()
    {
        try
        {
            var json = JsonSerializer.Serialize(_fileIndexTimes, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_indexTimesPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving index times to {Path}", _indexTimesPath);
        }
    }

    private void InitializeFileWatcher()
    {
        _watcher = new FileSystemWatcher(_config.FolderToIndex)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IndexFilesAsync();
                SaveIndexTimes(); // Save after each indexing cycle
                await Task.Delay(TimeSpan.FromSeconds(_config.IndexingIntervalSeconds), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during indexing");
            }
        }
    }

    private async Task IndexFilesAsync()
    {
        if (!Directory.Exists(_config.FolderToIndex))
        {
            _logger.LogWarning("Directory to index does not exist: {Directory}", _config.FolderToIndex);
            return;
        }

        _logger.LogInformation("Starting indexing of folder: {Folder}", _config.FolderToIndex);

        var files = Directory.GetFiles(_config.FolderToIndex, "*.*", SearchOption.AllDirectories)
            .Where(f => _config.FileExtensionsToIndex.Contains(Path.GetExtension(f).ToLowerInvariant()));

        foreach (var file in files)
        {
            if (ShouldIndexFile(file))
            {
                await IndexSingleFileAsync(file);
            }
        }

        // Clean up index times for files that no longer exist
        var nonExistentFiles = _fileIndexTimes.Keys.Where(path => !File.Exists(path)).ToList();
        foreach (var path in nonExistentFiles)
        {
            _fileIndexTimes.TryRemove(path, out _);
        }

        SaveIndexTimes(); // Save after bulk operations
        _logger.LogInformation("Completed indexing of {Count} files", files.Count());
    }

    private bool ShouldIndexFile(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        var lastModified = File.GetLastWriteTime(filePath);
        if (_fileIndexTimes.TryGetValue(filePath, out var lastIndexed))
        {
            return lastModified > lastIndexed;
        }
        return true;
    }

    private async Task IndexSingleFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;

            var fileInfo = new FileInfo(filePath);
            var content = await ExtractContentAsync(filePath);

            using var writer = new IndexWriter(_directory, new IndexWriterConfig(AppLuceneVersion, _analyzer));

            // Delete existing document if any
            writer.DeleteDocuments(new Term("path", filePath));

            // Create new document
            var doc = new Document
            {
                new StringField("path", filePath, Field.Store.YES),
                new StringField("filename", Path.GetFileName(filePath), Field.Store.YES),
                new StringField("extension", Path.GetExtension(filePath).ToLowerInvariant(), Field.Store.YES),
                new TextField("content", content, Field.Store.YES),
                new Int64Field("size", fileInfo.Length, Field.Store.YES),
                new Int64Field("lastModified", fileInfo.LastWriteTime.Ticks, Field.Store.YES),
                new Int64Field("indexedTime", DateTime.UtcNow.Ticks, Field.Store.YES)
            };

            writer.AddDocument(doc);
            writer.Commit();

            // Update index time
            _fileIndexTimes.AddOrUpdate(filePath, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
            SaveIndexTimes(); // Save after individual file updates
            _logger.LogInformation("Indexed file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing file: {FilePath}", filePath);
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Add small delay to ensure file is not locked
            await Task.Delay(100);
            await IndexSingleFileAsync(e.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file change for {FilePath}", e.FullPath);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        try
        {
            DeleteFileFromIndex(e.FullPath);
            _fileIndexTimes.TryRemove(e.FullPath, out _);
            SaveIndexTimes(); // Save after deletion
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file deletion for {FilePath}", e.FullPath);
        }
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            DeleteFileFromIndex(e.OldFullPath);
            _fileIndexTimes.TryRemove(e.OldFullPath, out _);
            await IndexSingleFileAsync(e.FullPath);
            SaveIndexTimes(); // Save after rename
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file rename from {OldPath} to {NewPath}", e.OldFullPath, e.FullPath);
        }
    }

    private void DeleteFileFromIndex(string filePath)
    {
        using var writer = new IndexWriter(_directory, new IndexWriterConfig(AppLuceneVersion, _analyzer));
        writer.DeleteDocuments(new Term("path", filePath));
        writer.Commit();
    }

    private async Task<string> ExtractContentAsync(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        try
        {
            switch (extension)
            {
                case ".docx":
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    {
                        var document = new XWPFDocument(stream);
                        var paragraphs = document.Paragraphs.Select(p => p.Text);
                        var tables = document.Tables.SelectMany(table =>
                            table.Rows.SelectMany(row =>
                                row.GetTableCells().Select(cell => cell.GetText())));

                        return string.Join("\n", paragraphs.Concat(tables));
                    }

                case ".txt":
                case ".md":
                case ".json":
                case ".xml":
                    return await File.ReadAllTextAsync(filePath);

                default:
                    _logger.LogWarning("Unsupported file type: {Extension}", extension);
                    return string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting content from file: {FilePath}", filePath);
            return string.Empty;
        }
    }

    public override void Dispose()
    {
        SaveIndexTimes(); // Save on service shutdown
        _watcher?.Dispose();
        _analyzer?.Dispose();
        _directory?.Dispose();
        base.Dispose();
    }
}
