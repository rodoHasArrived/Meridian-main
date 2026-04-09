<<<<<<< HEAD
=======
// Meridian CppTrader Host
//
// Length-prefixed JSON protocol host that mediates between the Meridian managed
// runtime and a CppTrader matching engine.
//
// Protocol:
//   Each frame = [4-byte LE int32 payload length][UTF-8 JSON payload]
//
// The managed C# runtime spawns this executable as a child process and
// communicates over stdin/stdout using the frame format above.
//
// When the upstream CppTrader library is vendored under native/vendor/CppTrader,
// define MERIDIAN_CPPTRADER_NATIVE=1 at compile time (see CMakeLists.txt) to
// replace the stub matching engine with CppTrader::Matching::MarketManager.

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <iomanip>
#include <iostream>
#include <map>
#include <mutex>
#include <optional>
#include <random>
#include <sstream>
#include <string>
#include <unordered_map>
#include <vector>

#include <nlohmann/json.hpp>

#ifdef MERIDIAN_CPPTRADER_NATIVE
// #include <matching/market_manager.h>  // CppTrader::Matching::MarketManager
#endif

using json = nlohmann::json;

// ── platform: set stdin/stdout to binary mode on Windows ────────────────────
#ifdef _WIN32
#include <fcntl.h>
#include <io.h>
static void set_binary_stdio()
{
    _setmode(_fileno(stdin),  _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
}
#else
static void set_binary_stdio() {}
#endif

// ── ISO-8601 UTC timestamp helper ────────────────────────────────────────────
static std::string utc_now_iso8601()
{
    using namespace std::chrono;
    auto tp  = system_clock::now();
    auto tt  = system_clock::to_time_t(tp);
    auto ms  = duration_cast<milliseconds>(tp.time_since_epoch()) % 1000;

    std::tm utc{};
#ifdef _WIN32
    gmtime_s(&utc, &tt);
#else
    gmtime_r(&tt, &utc);
#endif

    std::ostringstream oss;
    oss << std::put_time(&utc, "%Y-%m-%dT%H:%M:%S")
        << '.' << std::setw(3) << std::setfill('0') << ms.count()
        << "+00:00";
    return oss.str();
}

// ── UUID v4 generator ────────────────────────────────────────────────────────
static std::string new_uuid()
{
    static std::mt19937_64 rng{std::random_device{}()};
    static std::uniform_int_distribution<uint64_t> dist;

    uint64_t hi = dist(rng);
    uint64_t lo = dist(rng);

    // Set version 4 (bits 12-15 of hi = 0100)
    hi = (hi & ~(uint64_t{0xF} << 12)) | (uint64_t{4} << 12);
    // Set variant bits (bits 62-63 of lo = 10)
    lo = (lo & ~(uint64_t{3} << 62)) | (uint64_t{2} << 62);

    std::ostringstream oss;
    oss << std::hex << std::setfill('0')
        << std::setw(8) << ((hi >> 32) & 0xFFFFFFFF) << '-'
        << std::setw(4) << ((hi >> 16) & 0xFFFF)     << '-'
        << std::setw(4) << (hi & 0xFFFF)              << '-'
        << std::setw(4) << ((lo >> 48) & 0xFFFF)      << '-'
        << std::setw(12) << (lo & 0x0000FFFFFFFFFFFF);
    return oss.str();
}

// ── framing helpers ───────────────────────────────────────────────────────────

/// Read exactly `n` bytes from stdin into `buf`.  Returns false on EOF/error.
static bool read_exact(char* buf, std::size_t n)
{
    std::size_t total = 0;
    while (total < n)
    {
        std::cin.read(buf + total, static_cast<std::streamsize>(n - total));
        auto read = std::cin.gcount();
        if (read <= 0)
            return false;
        total += static_cast<std::size_t>(read);
    }
    return true;
}

/// Read the next length-prefixed JSON frame from stdin.
/// Returns an empty optional on clean EOF or a read error.
static std::optional<json> read_frame()
{
    std::array<uint8_t, 4> header{};
    if (!read_exact(reinterpret_cast<char*>(header.data()), 4))
        return std::nullopt;

    const int32_t length =
        static_cast<int32_t>(header[0])        |
        (static_cast<int32_t>(header[1]) << 8)  |
        (static_cast<int32_t>(header[2]) << 16) |
        (static_cast<int32_t>(header[3]) << 24);

    if (length <= 0)
        return std::nullopt;

    std::string payload(static_cast<std::size_t>(length), '\0');
    if (!read_exact(payload.data(), static_cast<std::size_t>(length)))
        return std::nullopt;

    return json::parse(payload, nullptr, /*allow_exceptions=*/false);
}

/// Write a length-prefixed JSON frame to stdout.
static void write_frame(const json& obj)
{
    const std::string payload = obj.dump();
    const int32_t     length  = static_cast<int32_t>(payload.size());

    std::array<uint8_t, 4> header{
        static_cast<uint8_t>(length & 0xFF),
        static_cast<uint8_t>((length >> 8)  & 0xFF),
        static_cast<uint8_t>((length >> 16) & 0xFF),
        static_cast<uint8_t>((length >> 24) & 0xFF)
    };

    std::cout.write(reinterpret_cast<const char*>(header.data()), 4);
    std::cout.write(payload.data(), static_cast<std::streamsize>(length));
    std::cout.flush();
}

/// Build a response envelope and write it.
static void send_response(
    const std::string& message_type,
    const std::string& request_id,
    const std::string& session_id,
    json               payload)
{
    write_frame(json{
        {"messageType", message_type},
        {"requestId",   request_id},
        {"sessionId",   session_id},
        {"payload",     std::move(payload)},
        {"timestamp",   utc_now_iso8601()}
    });
}

/// Build an unsolicited event envelope and write it.
static void send_event(
    const std::string& message_type,
    const std::string& session_id,
    json               payload)
{
    write_frame(json{
        {"messageType", message_type},
        {"sessionId",   session_id},
        {"payload",     std::move(payload)},
        {"timestamp",   utc_now_iso8601()}
    });
}

// ── stub / native matching engine ────────────────────────────────────────────

struct SymbolSpec
{
    std::string symbol;
    int         symbol_id{};
    int64_t     tick_size_nanos{};
    int64_t     qty_increment_nanos{};
    int         price_scale{};
    int64_t     lot_size_nanos{};
    std::string venue;
    std::string session_tz;
};

struct OrderEntry
{
    std::string order_id;
    std::string client_order_id;
    std::string symbol;
    std::string side;   // "buy" / "sell"
    std::string type;   // "market" / "limit" / "stop" / "stop-limit"
    std::string tif;
    int64_t     qty_nanos{};
    std::optional<int64_t> limit_price_nanos;
    std::optional<int64_t> stop_price_nanos;
    int64_t     filled_qty_nanos{};
    bool        active{true};
};

struct BookLevel
{
    int64_t  price_nanos{};
    int64_t  qty_nanos{};
    int64_t  sequence{};
};

struct OrderBook
{
    std::string          symbol;
    std::vector<BookLevel> bids;  // sorted descending by price
    std::vector<BookLevel> asks;  // sorted ascending  by price
    int64_t              sequence{};
};

class MatchingEngine
{
public:
    bool register_symbol(const SymbolSpec& spec)
    {
        const std::lock_guard lock{_mu};
        _symbols[spec.symbol] = spec;
        _books.emplace(spec.symbol, OrderBook{spec.symbol, {}, {}, 0});
        return true;
    }

    /// Submit an order.  Returns (accepted, failure_reason, fills).
    /// For simplicity the stub engine immediately fills market orders against synthetic
    /// mid-price liquidity and queues limit orders into the book.
    ///
    /// Replace this method body with CppTrader::Matching calls when the library is vendored.
    struct SubmitResult
    {
        bool        accepted{};
        std::string failure_reason;

        struct Fill
        {
            int64_t  filled_qty_nanos{};
            int64_t  cumulative_qty_nanos{};
            int64_t  avg_fill_price_nanos{};
            bool     is_terminal{};
        };
        std::vector<Fill> fills;
    };

    SubmitResult submit_order(OrderEntry entry)
    {
        const std::lock_guard lock{_mu};

        if (_symbols.find(entry.symbol) == _symbols.end())
            return {false, "Symbol not registered."};
        if (entry.qty_nanos <= 0)
            return {false, "Quantity must be positive."};

        SubmitResult result;
        result.accepted = true;

#ifdef MERIDIAN_CPPTRADER_NATIVE
        // TODO: delegate to CppTrader::Matching::MarketManager
        (void)entry;
#else
        // ── Stub engine: market orders fill instantly at synthetic mid-price ──
        auto& book = _books[entry.symbol];
        const auto& spec = _symbols[entry.symbol];

        if (entry.type == "market")
        {
            // Synthetic price = 100.00 in the host's nanos encoding:
            //   price_nanos = price_decimal / tick_size
            //   tick_size   = tick_size_nanos / 1e9
            //   => price_nanos = price_decimal * 1e9 / tick_size_nanos
            const double tick_size_d = spec.tick_size_nanos > 0
                ? static_cast<double>(spec.tick_size_nanos)
                : 1.0;
            const int64_t synthetic_price =
                static_cast<int64_t>(100.0 * 1e9 / tick_size_d);

            book.sequence++;
            SubmitResult::Fill fill{};
            fill.filled_qty_nanos      = entry.qty_nanos;
            fill.cumulative_qty_nanos  = entry.qty_nanos;
            fill.avg_fill_price_nanos  = synthetic_price;
            fill.is_terminal           = true;
            result.fills.push_back(fill);
        }
        else if (entry.type == "limit" && entry.limit_price_nanos)
        {
            // Add a resting limit order to the book and emit no immediate fill.
            BookLevel level{*entry.limit_price_nanos, entry.qty_nanos, ++book.sequence};
            if (entry.side == "buy")
            {
                book.bids.push_back(level);
                std::sort(book.bids.begin(), book.bids.end(),
                    [](const BookLevel& a, const BookLevel& b){ return a.price_nanos > b.price_nanos; });
            }
            else
            {
                book.asks.push_back(level);
                std::sort(book.asks.begin(), book.asks.end(),
                    [](const BookLevel& a, const BookLevel& b){ return a.price_nanos < b.price_nanos; });
            }
        }
        // stop / stop-limit orders are accepted but not yet activated (stub).

        _orders[entry.order_id] = std::move(entry);
#endif
        return result;
    }

    bool cancel_order(const std::string& order_id, std::string& failure_reason)
    {
        const std::lock_guard lock{_mu};
        auto it = _orders.find(order_id);
        if (it == _orders.end())
        {
            failure_reason = "Order not found.";
            return false;
        }
        if (!it->second.active)
        {
            failure_reason = "Order is already terminal.";
            return false;
        }
        it->second.active = false;
        return true;
    }

    /// Build a snapshot payload for the managed side.
    json get_snapshot(const std::string& symbol)
    {
        const std::lock_guard lock{_mu};
        auto it = _books.find(symbol);
        if (it == _books.end())
            return json{{"snapshot", nullptr}};

        auto& book = it->second;
        auto& spec = _symbols[symbol];

        const double tick = spec.tick_size_nanos > 0
            ? static_cast<double>(spec.tick_size_nanos) / 1e9
            : 0.01;
        const double qty_unit = spec.qty_increment_nanos > 0
            ? static_cast<double>(spec.qty_increment_nanos) / 1e9
            : 1.0;

        json bids_arr = json::array();
        for (const auto& lvl : book.bids)
            bids_arr.push_back({{"price", lvl.price_nanos * tick},
                                {"size",  lvl.qty_nanos  * qty_unit}});

        json asks_arr = json::array();
        for (const auto& lvl : book.asks)
            asks_arr.push_back({{"price", lvl.price_nanos * tick},
                                {"size",  lvl.qty_nanos  * qty_unit}});

        std::optional<double> mid, micro_price, imbalance;
        if (!book.bids.empty() && !book.asks.empty())
        {
            const double bp = book.bids.front().price_nanos * tick;
            const double ap = book.asks.front().price_nanos * tick;
            mid = (bp + ap) / 2.0;

            const double bv = static_cast<double>(book.bids.front().qty_nanos);
            const double av = static_cast<double>(book.asks.front().qty_nanos);
            if (bv + av > 0)
            {
                micro_price = (ap * bv + bp * av) / (bv + av);
                imbalance   = (bv - av) / (bv + av);
            }
        }

        json snap;
        snap["symbol"]         = book.symbol;
        snap["bids"]           = bids_arr;
        snap["asks"]           = asks_arr;
        snap["midPrice"]       = mid       ? json(*mid)        : json(nullptr);
        snap["microPrice"]     = micro_price ? json(*micro_price) : json(nullptr);
        snap["imbalance"]      = imbalance ? json(*imbalance)  : json(nullptr);
        snap["sequenceNumber"] = book.sequence;
        snap["venue"]          = spec.venue.empty() ? json(nullptr) : json(spec.venue);
        snap["timestamp"]      = utc_now_iso8601();

        return json{{"snapshot", snap}};
    }

    double get_tick_size_decimal(const std::string& symbol)
    {
        const std::lock_guard lock{_mu};
        auto it = _symbols.find(symbol);
        if (it == _symbols.end() || it->second.tick_size_nanos <= 0)
            return 0.01;

        return static_cast<double>(it->second.tick_size_nanos) / 1e9;
    }

private:
    std::mutex                                   _mu;
    std::unordered_map<std::string, SymbolSpec>  _symbols;
    std::unordered_map<std::string, OrderBook>   _books;
    std::unordered_map<std::string, OrderEntry>  _orders;
};

// ── session state ─────────────────────────────────────────────────────────────

struct Session
{
    std::string       session_id;
    std::string       session_kind; // "Execution" / "Replay" / "Ingest"
    MatchingEngine    engine;
};

// ── message handlers ──────────────────────────────────────────────────────────

static std::unordered_map<std::string, Session> g_sessions;

static void handle_create_session(const json& envelope)
{
    const std::string req_id = envelope.value("requestId", "");
    const auto&       payload = envelope["payload"];

    const std::string session_id = new_uuid();
    const std::string session_kind = payload.value("sessionKind", "Execution");
    auto [it, inserted] = g_sessions.try_emplace(session_id);
    auto& session = it->second;
    session.session_id = session_id;
    session.session_kind = session_kind;

    send_response("createSessionResponse", req_id, session_id, {
        {"sessionId",   session_id},
        {"sessionKind", session_kind},
        {"createdAt",   utc_now_iso8601()}
    });
}

static void handle_register_symbol(const json& envelope)
{
    const std::string req_id    = envelope.value("requestId", "");
    const std::string sess_id   = envelope.value("sessionId", "");
    const auto&       payload   = envelope["payload"];

    auto it = g_sessions.find(sess_id);
    if (it == g_sessions.end())
    {
        send_response("registerSymbolResponse", req_id, sess_id, {
            {"symbol",        payload.value("symbol", "")},
            {"registered",    false},
            {"failureReason", "Unknown session."}
        });
        return;
    }

    SymbolSpec spec;
    spec.symbol                = payload.value("symbol", "");
    spec.symbol_id             = payload.value("symbolId", 0);
    spec.tick_size_nanos       = payload.value("tickSizeNanos", int64_t{10'000'000});
    spec.qty_increment_nanos   = payload.value("quantityIncrementNanos", int64_t{1'000'000'000});
    spec.price_scale           = payload.value("priceScale", 2);
    spec.lot_size_nanos        = payload.value("lotSizeNanos", int64_t{1'000'000'000});
    spec.venue                 = payload.value("venue", std::string{});
    spec.session_tz            = payload.value("sessionTimeZone", std::string{"UTC"});

    const bool ok = it->second.engine.register_symbol(spec);

    send_response("registerSymbolResponse", req_id, sess_id, {
        {"symbol",        spec.symbol},
        {"registered",    ok},
        {"failureReason", ok ? json(nullptr) : json("Internal error.")}
    });
}

static void handle_submit_order(const json& envelope)
{
    const std::string req_id  = envelope.value("requestId", "");
    const std::string sess_id = envelope.value("sessionId", "");
    const auto&       payload = envelope["payload"];

    auto it = g_sessions.find(sess_id);
    if (it == g_sessions.end())
    {
        send_response("submitOrderResponse", req_id, sess_id, {
            {"orderId",       payload.value("orderId", "")},
            {"clientOrderId", payload.value("clientOrderId", "")},
            {"symbol",        payload.value("symbol", "")},
            {"accepted",      false},
            {"failureReason", "Unknown session."},
            {"timestamp",     utc_now_iso8601()}
        });
        return;
    }

    OrderEntry entry;
    entry.order_id          = payload.value("orderId", new_uuid());
    entry.client_order_id   = payload.value("clientOrderId", entry.order_id);
    entry.symbol            = payload.value("symbol", "");
    entry.side              = payload.value("side", "buy");
    entry.type              = payload.value("orderType", "market");
    entry.tif               = payload.value("timeInForce", "day");
    entry.qty_nanos         = payload.value("quantityNanos", int64_t{0});

    if (!payload["limitPriceNanos"].is_null())
        entry.limit_price_nanos = payload["limitPriceNanos"].get<int64_t>();
    if (!payload["stopPriceNanos"].is_null())
        entry.stop_price_nanos = payload["stopPriceNanos"].get<int64_t>();

    auto result = it->second.engine.submit_order(entry);

    // Acknowledge the submission
    send_response("submitOrderResponse", req_id, sess_id, {
        {"orderId",       entry.order_id},
        {"clientOrderId", entry.client_order_id},
        {"symbol",        entry.symbol},
        {"accepted",      result.accepted},
        {"failureReason", result.accepted ? json(nullptr) : json(result.failure_reason)},
        {"timestamp",     utc_now_iso8601()}
    });

    if (!result.accepted)
        return;

    // Emit accepted event (asynchronous confirmation from matching engine)
    send_event("accepted", sess_id, {
        {"orderId",       entry.order_id},
        {"clientOrderId", entry.client_order_id},
        {"symbol",        entry.symbol},
        {"timestamp",     utc_now_iso8601()}
    });

    // Emit fills, if any.
    const double tick_size = it->second.engine.get_tick_size_decimal(entry.symbol);
    for (const auto& fill : result.fills)
    {
        const bool is_full = fill.is_terminal;
        send_event("execution", sess_id, {
            {"orderId",                    entry.order_id},
            {"clientOrderId",              entry.client_order_id},
            {"symbol",                     entry.symbol},
            {"filledQuantityNanos",        fill.filled_qty_nanos},
            {"cumulativeFilledQuantityNanos", fill.cumulative_qty_nanos},
            // Convert price_nanos back to decimal: price = price_nanos * tick_size
            {"averageFillPrice",           fill.avg_fill_price_nanos * tick_size},
            {"isTerminal",                 is_full},
            {"timestamp",                  utc_now_iso8601()}
        });
    }
}

static void handle_cancel_order(const json& envelope)
{
    const std::string req_id  = envelope.value("requestId", "");
    const std::string sess_id = envelope.value("sessionId", "");
    const auto&       payload = envelope["payload"];
    const std::string order_id = payload.value("orderId", "");

    auto it = g_sessions.find(sess_id);
    if (it == g_sessions.end())
    {
        send_response("cancelOrderResponse", req_id, sess_id, {
            {"orderId",       order_id},
            {"accepted",      false},
            {"failureReason", "Unknown session."},
            {"timestamp",     utc_now_iso8601()}
        });
        return;
    }

    std::string failure_reason;
    const bool ok = it->second.engine.cancel_order(order_id, failure_reason);

    send_response("cancelOrderResponse", req_id, sess_id, {
        {"orderId",       order_id},
        {"accepted",      ok},
        {"failureReason", ok ? json(nullptr) : json(failure_reason)},
        {"timestamp",     utc_now_iso8601()}
    });

    if (ok)
    {
        send_event("cancelled", sess_id, {
            {"orderId",       order_id},
            {"clientOrderId", order_id},
            {"symbol",        ""},         // symbol is stored in the engine; sufficient for the managed side
            {"timestamp",     utc_now_iso8601()}
        });
    }
}

static void handle_get_snapshot(const json& envelope)
{
    const std::string req_id  = envelope.value("requestId", "");
    const std::string sess_id = envelope.value("sessionId", "");
    const auto&       payload = envelope["payload"];
    const std::string symbol  = payload.value("symbol", "");

    auto it = g_sessions.find(sess_id);
    if (it == g_sessions.end())
    {
        send_response("getSnapshotResponse", req_id, sess_id, {{"snapshot", nullptr}});
        return;
    }

    send_response("getSnapshotResponse", req_id, sess_id,
                  it->second.engine.get_snapshot(symbol));
}

static void handle_heartbeat(const json& envelope)
{
    const std::string req_id  = envelope.value("requestId", "");
    const std::string sess_id = envelope.value("sessionId", "");
    const auto&       payload = envelope["payload"];
    const std::string host_id = payload.value("hostId", "");

    send_response("heartbeatResponse", req_id, sess_id, {
        {"hostId",    host_id},
        {"timestamp", utc_now_iso8601()}
    });
}

// ── main event loop ───────────────────────────────────────────────────────────

int main()
{
    set_binary_stdio();

    // Disable I/O synchronization for throughput on the protocol streams.
    std::ios_base::sync_with_stdio(false);
    std::cin.tie(nullptr);

    while (true)
    {
        auto frame_opt = read_frame();
        if (!frame_opt)
            break;  // clean EOF or parse error

        const auto& env = *frame_opt;
        if (!env.is_object())
            continue;

        const std::string msg_type = env.value("messageType", "");

        if      (msg_type == "createSession")    handle_create_session(env);
        else if (msg_type == "registerSymbol")   handle_register_symbol(env);
        else if (msg_type == "submitOrder")      handle_submit_order(env);
        else if (msg_type == "cancelOrder")      handle_cancel_order(env);
        else if (msg_type == "getSnapshot")      handle_get_snapshot(env);
        else if (msg_type == "heartbeat")        handle_heartbeat(env);
        // Unknown message types are silently ignored.
    }

    return 0;
}
>>>>>>> d5ab6a6bf3983ec9a9f290c5b8296eeb2fbc46a3
