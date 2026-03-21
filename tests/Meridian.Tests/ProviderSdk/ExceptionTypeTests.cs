using FluentAssertions;
using Meridian.Application.Exceptions;
using Xunit;

namespace Meridian.Tests.ProviderSdk;

/// <summary>
/// Tests for custom exception types ensuring correct property propagation and hierarchy.
/// </summary>
public sealed class ExceptionTypeTests
{
    #region MeridianException (base)

    [Fact]
    public void MeridianException_MessageOnly_SetsMessage()
    {
        var ex = new MeridianException("test error");

        ex.Message.Should().Be("test error");
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void MeridianException_WithInnerException_PropagatesBoth()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new MeridianException("outer", inner);

        ex.Message.Should().Be("outer");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void MeridianException_IsException()
    {
        var ex = new MeridianException("test");

        ex.Should().BeAssignableTo<Exception>();
    }

    #endregion

    #region ConfigurationException

    [Fact]
    public void ConfigurationException_WithProperties_SetsAllFields()
    {
        var ex = new ConfigurationException("bad config", configPath: "/etc/config.json", fieldName: "DataSource");

        ex.Message.Should().Be("bad config");
        ex.ConfigPath.Should().Be("/etc/config.json");
        ex.FieldName.Should().Be("DataSource");
    }

    [Fact]
    public void ConfigurationException_MessageOnly_NullOptionalProperties()
    {
        var ex = new ConfigurationException("missing field");

        ex.ConfigPath.Should().BeNull();
        ex.FieldName.Should().BeNull();
    }

    [Fact]
    public void ConfigurationException_InheritsFromBase()
    {
        var ex = new ConfigurationException("test");

        ex.Should().BeAssignableTo<MeridianException>();
    }

    #endregion

    #region ConnectionException

    [Fact]
    public void ConnectionException_WithProperties_SetsAllFields()
    {
        var ex = new ConnectionException("connection failed", provider: "IB", host: "127.0.0.1", port: 7497);

        ex.Message.Should().Be("connection failed");
        ex.Provider.Should().Be("IB");
        ex.Host.Should().Be("127.0.0.1");
        ex.Port.Should().Be(7497);
    }

    [Fact]
    public void ConnectionException_MessageOnly_NullOptionalProperties()
    {
        var ex = new ConnectionException("timeout");

        ex.Provider.Should().BeNull();
        ex.Host.Should().BeNull();
        ex.Port.Should().BeNull();
    }

    [Fact]
    public void ConnectionException_WithInnerException_Propagates()
    {
        var inner = new TimeoutException("timed out");
        var ex = new ConnectionException("connection error", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    #endregion

    #region DataProviderException

    [Fact]
    public void DataProviderException_WithProperties_SetsAllFields()
    {
        var ex = new DataProviderException("provider error", provider: "Alpaca", symbol: "SPY");

        ex.Message.Should().Be("provider error");
        ex.Provider.Should().Be("Alpaca");
        ex.Symbol.Should().Be("SPY");
    }

    [Fact]
    public void DataProviderException_MessageOnly_NullOptionalProperties()
    {
        var ex = new DataProviderException("generic error");

        ex.Provider.Should().BeNull();
        ex.Symbol.Should().BeNull();
    }

    #endregion

    #region RateLimitException

    [Fact]
    public void RateLimitException_WithAllProperties_SetsFields()
    {
        var retryAfter = TimeSpan.FromSeconds(30);
        var ex = new RateLimitException(
            "rate limited",
            provider: "Polygon",
            symbol: "AAPL",
            retryAfter: retryAfter,
            remainingRequests: 0,
            requestLimit: 200);

        ex.Message.Should().Be("rate limited");
        ex.Provider.Should().Be("Polygon");
        ex.Symbol.Should().Be("AAPL");
        ex.RetryAfter.Should().Be(retryAfter);
        ex.RemainingRequests.Should().Be(0);
        ex.RequestLimit.Should().Be(200);
    }

    [Fact]
    public void RateLimitException_InheritsFromDataProviderException()
    {
        var ex = new RateLimitException("test");

        ex.Should().BeAssignableTo<DataProviderException>();
        ex.Should().BeAssignableTo<MeridianException>();
    }

    [Fact]
    public void RateLimitException_MessageOnly_NullOptionalProperties()
    {
        var ex = new RateLimitException("throttled");

        ex.RetryAfter.Should().BeNull();
        ex.RemainingRequests.Should().BeNull();
        ex.RequestLimit.Should().BeNull();
    }

    #endregion

    #region StorageException

    [Fact]
    public void StorageException_WithPath_SetsPath()
    {
        var ex = new StorageException("write failed", path: "/data/live/SPY.jsonl");

        ex.Message.Should().Be("write failed");
        ex.Path.Should().Be("/data/live/SPY.jsonl");
    }

    [Fact]
    public void StorageException_MessageOnly_NullPath()
    {
        var ex = new StorageException("disk full");

        ex.Path.Should().BeNull();
    }

    [Fact]
    public void StorageException_InheritsFromBase()
    {
        var ex = new StorageException("test");

        ex.Should().BeAssignableTo<MeridianException>();
    }

    #endregion

    #region ValidationException

    [Fact]
    public void ValidationException_WithErrors_SetsAllFields()
    {
        var errors = new[]
        {
            new ValidationError("INVALID_SYMBOL", "Symbol format invalid", Field: "Symbol", AttemptedValue: "!!!"),
            new ValidationError("MISSING_DATE", "Date is required", Field: "FromDate")
        };

        var ex = new ValidationException("validation failed", errors, entityType: "BackfillRequest", entityId: "req-1");

        ex.Message.Should().Be("validation failed");
        ex.Errors.Should().HaveCount(2);
        ex.Errors[0].Code.Should().Be("INVALID_SYMBOL");
        ex.Errors[0].Field.Should().Be("Symbol");
        ex.Errors[0].AttemptedValue.Should().Be("!!!");
        ex.Errors[1].Code.Should().Be("MISSING_DATE");
        ex.EntityType.Should().Be("BackfillRequest");
        ex.EntityId.Should().Be("req-1");
    }

    [Fact]
    public void ValidationException_MessageOnly_EmptyErrors()
    {
        var ex = new ValidationException("invalid input");

        ex.Errors.Should().BeEmpty();
        ex.EntityType.Should().BeNull();
        ex.EntityId.Should().BeNull();
    }

    [Fact]
    public void ValidationException_InheritsFromBase()
    {
        var ex = new ValidationException("test");

        ex.Should().BeAssignableTo<MeridianException>();
    }

    #endregion

    #region OperationTimeoutException

    [Fact]
    public void OperationTimeoutException_WithProperties_SetsAllFields()
    {
        var timeout = TimeSpan.FromSeconds(30);
        var ex = new OperationTimeoutException(
            "operation timed out",
            operationName: "GetDailyBars",
            timeout: timeout,
            provider: "Stooq");

        ex.Message.Should().Be("operation timed out");
        ex.OperationName.Should().Be("GetDailyBars");
        ex.Timeout.Should().Be(timeout);
        ex.Provider.Should().Be("Stooq");
    }

    [Fact]
    public void OperationTimeoutException_MessageOnly_NullOptionalProperties()
    {
        var ex = new OperationTimeoutException("timeout");

        ex.OperationName.Should().BeNull();
        ex.Timeout.Should().BeNull();
        ex.Provider.Should().BeNull();
    }

    [Fact]
    public void OperationTimeoutException_InheritsFromBase()
    {
        var ex = new OperationTimeoutException("test");

        ex.Should().BeAssignableTo<MeridianException>();
    }

    #endregion

    #region SequenceValidationException

    [Fact]
    public void SequenceValidationException_WithProperties_SetsAllFields()
    {
        var ex = new SequenceValidationException(
            "sequence gap detected",
            symbol: "SPY",
            expectedSequence: 100,
            actualSequence: 105,
            validationType: SequenceValidationType.Gap);

        ex.Message.Should().Be("sequence gap detected");
        ex.Symbol.Should().Be("SPY");
        ex.ExpectedSequence.Should().Be(100);
        ex.ActualSequence.Should().Be(105);
        ex.ValidationType.Should().Be(SequenceValidationType.Gap);
    }

    [Fact]
    public void SequenceValidationException_MessageOnly_DefaultValidationType()
    {
        var ex = new SequenceValidationException("bad sequence");

        ex.Symbol.Should().BeNull();
        ex.ExpectedSequence.Should().BeNull();
        ex.ActualSequence.Should().BeNull();
        ex.ValidationType.Should().Be(SequenceValidationType.Unknown);
    }

    [Fact]
    public void SequenceValidationException_AllValidationTypes_AreDistinct()
    {
        var types = Enum.GetValues<SequenceValidationType>();

        types.Should().HaveCountGreaterThanOrEqualTo(4);
        types.Should().Contain(SequenceValidationType.Gap);
        types.Should().Contain(SequenceValidationType.OutOfOrder);
        types.Should().Contain(SequenceValidationType.Duplicate);
        types.Should().Contain(SequenceValidationType.Reset);
    }

    #endregion

    #region Exception Hierarchy

    [Fact]
    public void AllCustomExceptions_InheritFromMeridianException()
    {
        typeof(ConfigurationException).Should().BeDerivedFrom<MeridianException>();
        typeof(ConnectionException).Should().BeDerivedFrom<MeridianException>();
        typeof(DataProviderException).Should().BeDerivedFrom<MeridianException>();
        typeof(RateLimitException).Should().BeDerivedFrom<DataProviderException>();
        typeof(StorageException).Should().BeDerivedFrom<MeridianException>();
        typeof(ValidationException).Should().BeDerivedFrom<MeridianException>();
        typeof(OperationTimeoutException).Should().BeDerivedFrom<MeridianException>();
        typeof(SequenceValidationException).Should().BeDerivedFrom<MeridianException>();
    }

    [Fact]
    public void SealedExceptions_AreSealed()
    {
        typeof(ConfigurationException).Should().BeSealed();
        typeof(ConnectionException).Should().BeSealed();
        typeof(RateLimitException).Should().BeSealed();
        typeof(StorageException).Should().BeSealed();
        typeof(ValidationException).Should().BeSealed();
        typeof(OperationTimeoutException).Should().BeSealed();
        typeof(SequenceValidationException).Should().BeSealed();
    }

    #endregion
}
