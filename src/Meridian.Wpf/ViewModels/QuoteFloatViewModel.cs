using System;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Threading;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for a floating tear-off quote panel.
/// Polls the live quote endpoint for a single symbol and tracks price direction.
/// </summary>
public sealed class QuoteFloatViewModel : BindableBase, IDisposable
{
    // Static readonly brushes — never call FindResource() per tick
    private static readonly SolidColorBrush UptickBrush = CreateFrozen(Color.FromRgb(63, 185, 80));
    private static readonly SolidColorBrush DowntickBrush = CreateFrozen(Color.FromRgb(244, 67, 54));
    private static readonly SolidColorBrush NeutralBrush = CreateFrozen(Color.FromRgb(200, 200, 210));

    private static SolidColorBrush CreateFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly HttpClient _httpClient = new();
    private readonly DispatcherTimer _pollTimer;
    private readonly string _baseUrl;
    private decimal _previousLastPrice;
    private bool _disposed;

    private string _symbol = string.Empty;
    public string Symbol { get => _symbol; private set => SetProperty(ref _symbol, value); }

    private decimal _lastPrice;
    public decimal LastPrice { get => _lastPrice; private set => SetProperty(ref _lastPrice, value); }

    private decimal _bidPrice;
    public decimal BidPrice { get => _bidPrice; private set => SetProperty(ref _bidPrice, value); }

    private decimal _askPrice;
    public decimal AskPrice { get => _askPrice; private set => SetProperty(ref _askPrice, value); }

    private Brush _priceDirectionBrush = NeutralBrush;
    public Brush PriceDirectionBrush
    {
        get => _priceDirectionBrush;
        private set => SetProperty(ref _priceDirectionBrush, value);
    }

    public QuoteFloatViewModel(string symbol)
    {
        _symbol = symbol;
        _baseUrl = WpfServices.StatusService.Instance.BaseUrl;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _pollTimer.Tick += async (_, _) => await PollQuoteAsync();
        _pollTimer.Start();
    }

    private async System.Threading.Tasks.Task PollQuoteAsync()
    {
        if (_disposed) return;
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/api/live/{Uri.EscapeDataString(_symbol)}/quote");

            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            var quote = JsonSerializer.Deserialize<JsonElement>(json);

            decimal bid = 0, ask = 0, last = 0;
            if (quote.TryGetProperty("bid", out var b)) bid = b.GetDecimal();
            if (quote.TryGetProperty("ask", out var a)) ask = a.GetDecimal();
            if (quote.TryGetProperty("last", out var l)) last = l.GetDecimal();
            else if (bid > 0 && ask > 0) last = (bid + ask) / 2m;

            BidPrice = bid;
            AskPrice = ask;

            if (last > 0)
            {
                PriceDirectionBrush = last > _previousLastPrice ? UptickBrush
                    : last < _previousLastPrice ? DowntickBrush
                    : NeutralBrush;
                _previousLastPrice = last;
                LastPrice = last;
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) { }
        catch (JsonException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer.Stop();
        _httpClient.Dispose();
    }
}
