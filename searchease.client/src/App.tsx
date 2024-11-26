import { useState, useEffect, useCallback } from 'react';
import './App.css';

interface SearchResult {
  fileName: string;
  filePath: string;
  fileExtension: string;
  contentSnippets: string[];
  fileSize: number;
  lastModified: string;
  score: number;
}

// Add TypeScript interface for the electron API
declare global {
  interface Window {
    electronAPI: {
      openFile: (filePath: string) => Promise<{ success: boolean; error?: string }>;
    };
  }
}

function App() {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResult[]>([]);
  const [isLoading, setIsLoading] = useState(false);

  // Debounced search function
  const debouncedSearch = useCallback(
    async (searchQuery: string) => {
      if (!searchQuery.trim()) {
        setResults([]);
        return;
      }

      setIsLoading(true);
      try {
        const response = await fetch(`api/Search?query=${encodeURIComponent(searchQuery)}&maxResults=10`);
        if (response.ok) {
          const data = await response.json();
          setResults(data);
        } else {
          console.error('Search failed:', response.statusText);
        }
      } catch (error) {
        console.error('Search error:', error);
      } finally {
        setIsLoading(false);
      }
    },
    []
  );

  // Effect to trigger search when query changes
  useEffect(() => {
    const timeoutId = setTimeout(() => {
      debouncedSearch(query);
    }, 300); // Wait for 300ms after last keystroke

    return () => clearTimeout(timeoutId);
  }, [query, debouncedSearch]);

  const formatFileSize = (bytes: number) => {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const formatDate = (dateString: string) => {
    return new Date(dateString).toLocaleString();
  };

  const handleFilePathClick = async (filePath: string) => {
    try {
      const result = await window.electronAPI.openFile(filePath);
      if (!result.success) {
        console.error('Failed to open file:', result.error);
      }
    } catch (error) {
      console.error('Error opening file:', error);
    }
  };

  return (
    <div className="container">
      <h1>SearchEase</h1>
      
      <div className="search-box">
        <input
          type="text"
          className="search-input"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Start typing to search..."
        />
        {isLoading && <div className="loading-indicator">Searching...</div>}
      </div>

      <div className="results">
        {results.map((result, index) => (
          <div key={index} className="result-item">
            <div className="result-header">
              <div className="file-info">
                <h3 className="file-name">{result.fileName}</h3>
                <p className="file-path" 
                  onClick={() => handleFilePathClick(result.filePath)}
                  style={{ cursor: 'pointer', textDecoration: 'underline' }}
                >
                  {result.filePath}
                </p>
                <p className="file-meta">
                  {formatFileSize(result.fileSize)} • Last modified: {formatDate(result.lastModified)}
                </p>
              </div>
              <div className="score">
                Score: {result.score.toFixed(2)}
              </div>
            </div>
            
            <div className="snippets">
              {result.contentSnippets.map((snippet, i) => (
                <div 
                  key={i} 
                  className="snippet"
                  dangerouslySetInnerHTML={{ __html: snippet }}
                />
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export default App;