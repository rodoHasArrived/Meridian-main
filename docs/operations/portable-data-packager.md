# Portable Data Packager

The Portable Data Packager allows you to create self-contained, shareable data packages from your collected market data. These packages include all necessary metadata, checksums, and documentation for data portability and integrity verification.

## Overview

The Portable Data Packager provides:

- **Self-contained packages**: ZIP or TAR.GZ archives with all data and metadata
- **Comprehensive manifests**: JSON manifests with checksums, schemas, and quality metrics
- **Loader scripts**: Ready-to-use Python and R scripts for data loading
- **Data dictionaries**: Auto-generated documentation for all event types
- **Integrity verification**: SHA256 checksums for all files
- **Flexible filtering**: Package by symbol, date range, or event type

## Quick Start

### Creating a Package

```bash
# Create a package with all data
Meridian --package

# Create a package with specific symbols
Meridian --package --package-symbols AAPL,MSFT,SPY

# Create a package with date range
Meridian --package --package-from 2024-01-01 --package-to 2024-12-31

# Create a named package with maximum compression
Meridian --package --package-name my-research-data --package-compression max
```

### Importing a Package

```bash
# Import a package to the default data directory
Meridian --import-package ./packages/my-data.zip

# Import to a specific directory
Meridian --import-package ./packages/my-data.zip --import-destination /data/imported

# Import without checksum validation (faster, less safe)
Meridian --import-package ./packages/my-data.zip --skip-validation
```

### Viewing Package Contents

```bash
# List package contents
Meridian --list-package ./packages/my-data.zip

# Validate package integrity
Meridian --validate-package ./packages/my-data.zip
```

## Command Line Options

### Package Creation Options

| Option | Description | Default |
|--------|-------------|---------|
| `--package` | Create a portable data package | - |
| `--package-name <name>` | Package name | `market-data-YYYYMMDD` |
| `--package-description <text>` | Package description | - |
| `--package-output <path>` | Output directory | `packages` |
| `--package-symbols <list>` | Comma-separated symbols to include | All symbols |
| `--package-events <list>` | Event types (Trade,BboQuote,L2Snapshot) | All types |
| `--package-from <date>` | Start date (YYYY-MM-DD) | No limit |
| `--package-to <date>` | End date (YYYY-MM-DD) | No limit |
| `--package-format <fmt>` | Format: zip, tar.gz | `zip` |
| `--package-compression <level>` | Compression: none, fast, balanced, max | `balanced` |
| `--no-quality-report` | Exclude quality report | Include |
| `--no-data-dictionary` | Exclude data dictionary | Include |
| `--no-loader-scripts` | Exclude loader scripts | Include |
| `--skip-checksums` | Skip checksum verification | Verify |

### Import Options

| Option | Description | Default |
|--------|-------------|---------|
| `--import-package <path>` | Import a package | - |
| `--import-destination <path>` | Destination directory | Data root |
| `--skip-validation` | Skip checksum validation | Validate |
| `--merge` | Merge with existing data | Overwrite |

## HTTP API

The Portable Data Packager is also available via HTTP API when running with `--ui`:

### Create Package

```http
POST /api/packaging/create
Content-Type: application/json

{
  "name": "my-package",
  "description": "Research data for Q1 2024",
  "symbols": ["AAPL", "MSFT"],
  "eventTypes": ["Trade", "BboQuote"],
  "startDate": "2024-01-01",
  "endDate": "2024-03-31",
  "format": "zip",
  "compressionLevel": "balanced",
  "includeQualityReport": true,
  "includeLoaderScripts": true
}
```

### Import Package

```http
POST /api/packaging/import
Content-Type: application/json

{
  "packagePath": "/path/to/package.zip",
  "destinationDirectory": "/data/imported",
  "validateChecksums": true
}
```

### Validate Package

```http
POST /api/packaging/validate
Content-Type: application/json

{
  "packagePath": "/path/to/package.zip"
}
```

### List Package Contents

```http
GET /api/packaging/contents?path=/path/to/package.zip
```

### List Available Packages

```http
GET /api/packaging/list
```

### Download Package

```http
GET /api/packaging/download/{fileName}
```

## Package Structure

A portable package has the following structure:

```
my-package_20240103_120000.zip
├── manifest.json           # Package manifest with checksums
├── README.md               # Package documentation
├── data/                   # Market data files
│   └── 2024-01-03/
│       └── AAPL/
│           └── Trade/
│               └── AAPL_Trade_2024-01-03.jsonl
├── metadata/               # Data dictionary and schemas
│   └── data_dictionary.md
└── scripts/                # Loader scripts
    ├── load_data.py
    └── load_data.R
```

## Manifest Format

The `manifest.json` file contains comprehensive metadata:

```json
{
  "packageVersion": "1.0.0",
  "packageId": "abc123def456",
  "name": "my-package",
  "description": "Research data package",
  "createdAt": "2024-01-03T12:00:00Z",
  "creatorVersion": "1.0.0",
  "format": "Zip",
  "compression": "Deflate",
  "packageChecksum": "sha256:abc123...",
  "packageSizeBytes": 1048576,
  "uncompressedSizeBytes": 5242880,
  "dateRange": {
    "start": "2024-01-01T00:00:00Z",
    "end": "2024-03-31T23:59:59Z",
    "tradingDays": 63,
    "calendarDays": 90
  },
  "symbols": ["AAPL", "MSFT", "SPY"],
  "eventTypes": ["Trade", "BboQuote"],
  "totalFiles": 189,
  "totalEvents": 5000000,
  "files": [
    {
      "path": "data/2024-01-03/AAPL/Trade/data.jsonl",
      "symbol": "AAPL",
      "eventType": "Trade",
      "date": "2024-01-03T00:00:00Z",
      "format": "jsonl",
      "sizeBytes": 10240,
      "eventCount": 1500,
      "checksumSha256": "def456..."
    }
  ],
  "schemas": {
    "Trade": {
      "eventType": "Trade",
      "version": "1.0",
      "fields": [
        { "name": "Timestamp", "type": "datetime", "description": "Event timestamp in UTC" },
        { "name": "Symbol", "type": "string", "description": "Ticker symbol" },
        { "name": "Price", "type": "decimal", "description": "Trade price" },
        { "name": "Size", "type": "long", "description": "Trade size in shares" }
      ]
    }
  }
}
```

## Using Loader Scripts

### Python

The generated `load_data.py` script provides convenient data loading:

```python
from scripts.load_data import load_data, get_symbols, get_event_types

# List available data
print(f"Symbols: {get_symbols()}")
print(f"Event Types: {get_event_types()}")

# Load all data
df = load_data()

# Load specific symbol
aapl = load_data(symbol="AAPL")

# Load specific event type
trades = load_data(event_type="Trade")

# Load with filters
filtered = load_data(symbol="AAPL", event_type="Trade", date="2024-01-15")
```

### R

The generated `load_data.R` script works with tidyverse:

```r
source("scripts/load_data.R")

# List available data
get_symbols()
get_event_types()

# Load all data
df <- load_data()

# Load with filters
aapl_trades <- load_data(symbol = "AAPL", event_type = "Trade")
```

## Compression Levels

| Level | Algorithm | Speed | Size | Use Case |
|-------|-----------|-------|------|----------|
| `none` | None | Fastest | Largest | Debugging, immediate analysis |
| `fast` | Deflate L1 | Fast | Large | Quick packaging |
| `balanced` | Deflate L6 | Medium | Medium | Default, good balance |
| `maximum` | Deflate L9 | Slow | Smallest | Long-term archival |

## Internal Layouts

The `internalLayout` option controls how files are organized within the package:

| Layout | Pattern | Best For |
|--------|---------|----------|
| `ByDate` | `data/{date}/{symbol}/{type}/` | Time-based analysis |
| `BySymbol` | `data/{symbol}/{type}/{date}/` | Symbol-focused research |
| `ByType` | `data/{type}/{symbol}/{date}/` | Event type analysis |
| `Flat` | `data/{symbol}_{type}_{date}.jsonl` | Simple flat structure |

## Best Practices

### Creating Packages

1. **Use descriptive names**: Include date range and purpose in the name
2. **Add descriptions**: Help future users understand the package contents
3. **Enable checksums**: Always verify data integrity (default)
4. **Include documentation**: Keep loader scripts and data dictionary enabled

### Sharing Packages

1. **Verify before sharing**: Run `--validate-package` before distribution
2. **Document filters**: Note any symbols, dates, or types that were excluded
3. **Consider size**: Use maximum compression for large packages being transferred
4. **Include metadata**: Use tags and custom metadata for categorization

### Importing Packages

1. **Validate first**: Always validate packages from external sources
2. **Check space**: Ensure sufficient disk space for uncompressed data
3. **Use separate directory**: Import to a dedicated directory before merging
4. **Verify checksums**: Never skip validation for external packages

## Troubleshooting

### Package Creation Fails

```
Error: No data files found matching the specified criteria
```

**Solution**: Check that:
- The data root directory contains data files
- Symbol and date filters match existing data
- File naming conventions are correct

### Import Validation Fails

```
Error: Some files failed checksum validation
```

**Solution**:
- The package may be corrupted - re-download or re-create
- Check disk space during extraction
- Use `--skip-validation` only if you trust the source

### Large Package Performance

For packages over 10GB:
- Use `tar.gz` format (better streaming support)
- Consider splitting by date range
- Use `maximum` compression to reduce transfer time

## API Reference

See the full API documentation at `/api/docs` when running with `--ui`.

## Related Documentation

- [Storage Architecture](../architecture/storage-design.md)
- [Data Export Guide](../HELP.md#analysis-ready-exports)
- [CLI Reference](../HELP.md#command-line-usage)
