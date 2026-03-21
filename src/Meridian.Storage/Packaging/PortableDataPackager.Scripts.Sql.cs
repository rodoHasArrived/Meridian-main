using System.Text;

namespace Meridian.Storage.Packaging;

/// <summary>
/// Database import script generation (PostgreSQL, ClickHouse, DuckDB).
/// </summary>
public sealed partial class PortableDataPackager
{
    private string GeneratePostgreSqlImport(PackageManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- PostgreSQL Import Script");
        sb.AppendLine($"-- Package: {manifest.Name}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("-- Create tables");
        sb.AppendLine("CREATE TABLE IF NOT EXISTS trades (");
        sb.AppendLine("    id SERIAL PRIMARY KEY,");
        sb.AppendLine("    timestamp TIMESTAMPTZ NOT NULL,");
        sb.AppendLine("    symbol VARCHAR(20) NOT NULL,");
        sb.AppendLine("    price DECIMAL(18, 8) NOT NULL,");
        sb.AppendLine("    size BIGINT NOT NULL,");
        sb.AppendLine("    side VARCHAR(10),");
        sb.AppendLine("    exchange VARCHAR(20),");
        sb.AppendLine("    sequence_number BIGINT");
        sb.AppendLine(");");
        sb.AppendLine();
        sb.AppendLine("CREATE INDEX IF NOT EXISTS idx_trades_symbol_time ON trades(symbol, timestamp);");
        sb.AppendLine();
        sb.AppendLine("-- Note: For JSONL files, use PostgreSQL's COPY with JSON processing");
        sb.AppendLine("-- or load via Python/psycopg2");

        return sb.ToString();
    }

    private string GenerateClickHouseImport(PackageManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- ClickHouse Import Script");
        sb.AppendLine($"-- Package: {manifest.Name}");
        sb.AppendLine();
        sb.AppendLine("CREATE TABLE IF NOT EXISTS trades (");
        sb.AppendLine("    timestamp DateTime64(9),");
        sb.AppendLine("    symbol LowCardinality(String),");
        sb.AppendLine("    price Decimal64(8),");
        sb.AppendLine("    size UInt64,");
        sb.AppendLine("    side LowCardinality(String),");
        sb.AppendLine("    exchange LowCardinality(String)");
        sb.AppendLine(") ENGINE = MergeTree()");
        sb.AppendLine("ORDER BY (symbol, timestamp);");

        return sb.ToString();
    }

    private string GenerateDuckDbImport(PackageManifest manifest)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- DuckDB Import Script");
        sb.AppendLine($"-- Package: {manifest.Name}");
        sb.AppendLine($"-- Package ID: {manifest.PackageId}");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("-- Usage: duckdb marketdata.db < import_duckdb.sql");
        sb.AppendLine("-- Or interactively: duckdb marketdata.db");
        sb.AppendLine("--                   .read import_duckdb.sql");
        sb.AppendLine();

        // Trades table with proper schema
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Create trades table with explicit schema");
        sb.AppendLine("-- ============================================");
        sb.AppendLine("CREATE TABLE IF NOT EXISTS trades (");
        sb.AppendLine("    timestamp TIMESTAMPTZ NOT NULL,");
        sb.AppendLine("    symbol VARCHAR NOT NULL,");
        sb.AppendLine("    price DECIMAL(18, 8) NOT NULL,");
        sb.AppendLine("    size BIGINT NOT NULL,");
        sb.AppendLine("    side VARCHAR(10),");
        sb.AppendLine("    exchange VARCHAR(20),");
        sb.AppendLine("    sequence_number BIGINT,");
        sb.AppendLine("    stream_id VARCHAR(50)");
        sb.AppendLine(");");
        sb.AppendLine();

        // Quotes table
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Create quotes table");
        sb.AppendLine("-- ============================================");
        sb.AppendLine("CREATE TABLE IF NOT EXISTS quotes (");
        sb.AppendLine("    timestamp TIMESTAMPTZ NOT NULL,");
        sb.AppendLine("    symbol VARCHAR NOT NULL,");
        sb.AppendLine("    bid_price DECIMAL(18, 8),");
        sb.AppendLine("    bid_size BIGINT,");
        sb.AppendLine("    ask_price DECIMAL(18, 8),");
        sb.AppendLine("    ask_size BIGINT,");
        sb.AppendLine("    exchange VARCHAR(20)");
        sb.AppendLine(");");
        sb.AppendLine();

        // Bars table
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Create bars (OHLCV) table");
        sb.AppendLine("-- ============================================");
        sb.AppendLine("CREATE TABLE IF NOT EXISTS bars (");
        sb.AppendLine("    timestamp TIMESTAMPTZ NOT NULL,");
        sb.AppendLine("    symbol VARCHAR NOT NULL,");
        sb.AppendLine("    open DECIMAL(18, 8) NOT NULL,");
        sb.AppendLine("    high DECIMAL(18, 8) NOT NULL,");
        sb.AppendLine("    low DECIMAL(18, 8) NOT NULL,");
        sb.AppendLine("    close DECIMAL(18, 8) NOT NULL,");
        sb.AppendLine("    volume BIGINT NOT NULL,");
        sb.AppendLine("    vwap DECIMAL(18, 8),");
        sb.AppendLine("    trade_count INTEGER");
        sb.AppendLine(");");
        sb.AppendLine();

        // Import instructions for different file formats
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Import data from JSONL files");
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Option 1: Auto-detect schema from JSONL");
        sb.AppendLine("-- INSERT INTO trades SELECT * FROM read_json_auto('data/**/trades*.jsonl');");
        sb.AppendLine();
        sb.AppendLine("-- Option 2: Explicit column mapping for trades");
        sb.AppendLine("INSERT INTO trades (timestamp, symbol, price, size, side, exchange, sequence_number, stream_id)");
        sb.AppendLine("SELECT");
        sb.AppendLine("    epoch_ms(CAST(Timestamp AS BIGINT)) AS timestamp,");
        sb.AppendLine("    Symbol AS symbol,");
        sb.AppendLine("    CAST(Price AS DECIMAL(18,8)) AS price,");
        sb.AppendLine("    CAST(Size AS BIGINT) AS size,");
        sb.AppendLine("    Aggressor AS side,");
        sb.AppendLine("    Venue AS exchange,");
        sb.AppendLine("    CAST(SequenceNumber AS BIGINT) AS sequence_number,");
        sb.AppendLine("    StreamId AS stream_id");
        sb.AppendLine("FROM read_json_auto('data/**/trades*.jsonl', ignore_errors=true);");
        sb.AppendLine();

        // Parquet support
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Import from Parquet files (if available)");
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- INSERT INTO trades SELECT * FROM read_parquet('data/**/*.parquet');");
        sb.AppendLine();

        // Indexes
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Create indexes for query performance");
        sb.AppendLine("-- ============================================");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS idx_trades_symbol ON trades(symbol);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS idx_trades_timestamp ON trades(timestamp);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS idx_quotes_symbol ON quotes(symbol);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS idx_quotes_timestamp ON quotes(timestamp);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS idx_bars_symbol ON bars(symbol);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS idx_bars_timestamp ON bars(timestamp);");
        sb.AppendLine();

        // Summary queries
        sb.AppendLine("-- ============================================");
        sb.AppendLine("-- Verify import with summary queries");
        sb.AppendLine("-- ============================================");
        sb.AppendLine("SELECT 'trades' AS table_name, COUNT(*) AS row_count FROM trades");
        sb.AppendLine("UNION ALL");
        sb.AppendLine("SELECT 'quotes', COUNT(*) FROM quotes");
        sb.AppendLine("UNION ALL");
        sb.AppendLine("SELECT 'bars', COUNT(*) FROM bars;");
        sb.AppendLine();
        sb.AppendLine("-- Show symbols and date ranges");
        sb.AppendLine("SELECT symbol, MIN(timestamp) AS first_trade, MAX(timestamp) AS last_trade, COUNT(*) AS trade_count");
        sb.AppendLine("FROM trades GROUP BY symbol ORDER BY symbol;");

        return sb.ToString();
    }
}
