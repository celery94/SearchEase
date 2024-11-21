using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Lucene.Net.Search.Highlight;
using Microsoft.Extensions.Options;
using SearchEase.Server.Configuration;
using Directory = Lucene.Net.Store.Directory;
using System.IO;

namespace SearchEase.Server.Services;

public class SearchService
{
    private readonly IndexingConfiguration _config;
    private readonly ILogger<SearchService> _logger;
    private readonly Directory _directory;
    private readonly StandardAnalyzer _analyzer;
    private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
    private const int MaxSnippetLength = 150;
    private const int MaxFragments = 5;

    public class SearchResult
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public List<string> ContentSnippets { get; set; } = new();
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        public float Score { get; set; }
    }

    public SearchService(
        IOptions<IndexingConfiguration> config,
        ILogger<SearchService> logger)
    {
        _config = config.Value;
        _logger = logger;

        var indexPath = Path.Combine(AppContext.BaseDirectory, _config.IndexPath);
        _directory = FSDirectory.Open(indexPath);
        _analyzer = new StandardAnalyzer(AppLuceneVersion);
    }

    public async Task<IEnumerable<SearchResult>> SearchAsync(string searchTerm, int maxResults = 10)
    {
        try
        {
            using var reader = DirectoryReader.Open(_directory);
            var searcher = new IndexSearcher(reader);

            // Create a query that searches both filename and content
            var queryParser = new MultiFieldQueryParser(
                AppLuceneVersion,
                new[] { "filename", "content" },
                _analyzer);

            var query = queryParser.Parse(searchTerm);
            var hits = searcher.Search(query, maxResults).ScoreDocs;

            // Setup highlighter with HTML formatting
            var scorer = new QueryScorer(query);
            var formatter = new SimpleHTMLFormatter("<b>", "</b>");
            var highlighter = new Highlighter(formatter, scorer)
            {
                TextFragmenter = new SimpleFragmenter(MaxSnippetLength)
            };
            highlighter.MaxDocCharsToAnalyze = 100000; // Increase max chars to analyze if needed

            var results = new List<SearchResult>();
            foreach (var hit in hits)
            {
                var doc = searcher.Doc(hit.Doc);
                var filePath = doc.Get("path");
                var fileInfo = new FileInfo(filePath);
                var content = doc.Get("content") ?? string.Empty;

                // Need to create new TokenStream for each GetBestFragment call
                var tokenStream = TokenSources.GetAnyTokenStream(reader, hit.Doc, "content", doc, _analyzer);
                var fragments = highlighter.GetBestFragments(tokenStream, content, MaxFragments).ToList();

                // If no highlighted fragments found or not enough, take sentences
                if (fragments.Count == 0)
                {
                    fragments.AddRange(
                        content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .Take(MaxFragments)
                            .Select(s => s + ".")
                    );

                    if (fragments.Count == 0 && !string.IsNullOrEmpty(content))
                    {
                        fragments.Add(content.Substring(0, Math.Min(content.Length, MaxSnippetLength)) + "...");
                    }
                }

                results.Add(new SearchResult
                {
                    FileName = doc.Get("filename"),
                    FilePath = filePath,
                    FileExtension = doc.Get("extension"),
                    ContentSnippets = fragments,
                    FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                    LastModified = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.MinValue,
                    Score = hit.Score
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching with term: {SearchTerm}", searchTerm);
            throw;
        }
    }

    public void Dispose()
    {
        _analyzer?.Dispose();
        _directory?.Dispose();
    }
}
