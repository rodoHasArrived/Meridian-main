using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Issues short-lived, single-use launch claims so the desktop workstation never trusts
/// browser-selected identity or navigation state directly from process arguments.
/// </summary>
public sealed class DesktopLaunchTicketService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public DesktopLaunchTicketService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Issue(string username, string page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(page);

        PruneExpired();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = _timeProvider.GetUtcNow() + TicketLifetime;
        _tickets[token] = new Ticket(username.Trim(), page.Trim(), expiresAt);
        return token;
    }

    public DesktopLaunchTicketRedemptionDto? Redeem(IPAddress? remoteAddress, string token)
    {
        if (remoteAddress is not null && !IPAddress.IsLoopback(remoteAddress))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(token) || !_tickets.TryRemove(token, out var ticket))
        {
            return null;
        }

        if (ticket.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return null;
        }

        return new DesktopLaunchTicketRedemptionDto(
            ticket.Username,
            ticket.Page,
            ticket.ExpiresAtUtc);
    }

    private void PruneExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var (token, ticket) in _tickets)
        {
            if (ticket.ExpiresAtUtc <= now)
            {
                _tickets.TryRemove(token, out _);
            }
        }
    }

    private sealed record Ticket(string Username, string Page, DateTimeOffset ExpiresAtUtc);
}
