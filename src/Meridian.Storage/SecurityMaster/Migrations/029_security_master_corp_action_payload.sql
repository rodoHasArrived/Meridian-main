-- Migration 029: Corporate Action Payload Envelope
-- Adds a generic JSONB payload keyed by event type so new corporate-action event types can carry
-- their economics without another nullable per-event-type column. The eight typed columns
-- (dividend_per_share, split_ratio, distribution_ratio, exchange_ratio,
-- subscription_price_per_share, rights_per_share, redemption_price_percent_of_par,
-- acquirer_security_id) remain authoritative for the event types that declared them; the payload
-- is the envelope for everything else (tender offers, crypto forks, returns of capital, principal
-- paydowns, option contract adjustments, delistings, and future event types). Well-known payload
-- keys per event type are documented in Meridian.Contracts CorporateActionPayloads.

alter table __SCHEMA__.corporate_actions
    add column if not exists payload jsonb null;
