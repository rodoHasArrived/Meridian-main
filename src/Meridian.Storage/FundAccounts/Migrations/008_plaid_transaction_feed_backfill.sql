-- Before 007, genuine Plaid sync deliveries were retained as complete statements. A bank name
-- and absent running balances do not prove connector provenance: operator CSV imports permit
-- both. Upgrade only the exact historical Plaid line IDs AND whole-delivery batch identity.
-- PlaidWorkstationService hashes UTF-8 material with MD5, then constructs a .NET Guid from its
-- bytes. The first three Guid fields therefore reverse the digest's byte order below.
-- Restrict references to unambiguous ASCII IDs so translate + C ordering reproduces
-- StringComparer.OrdinalIgnoreCase exactly. Missing, incomplete, case-tied, unsupported, or
-- otherwise ambiguous evidence stays a complete statement and requires explicit review.
with candidate_lines as (
    select batch.batch_id, batch.account_id, batch.line_count,
           line.account_id as line_account_id, line.statement_line_id,
           line.external_reference, line.running_balance,
           translate(line.external_reference, 'abcdefghijklmnopqrstuvwxyz',
                     'ABCDEFGHIJKLMNOPQRSTUVWXYZ') as sort_reference,
           md5('plaid-bank-line:' || line.external_reference) as line_digest
      from __SCHEMA__.bank_statement_batch batch
      join __SCHEMA__.bank_statement_line line on line.batch_id = batch.batch_id
     where not batch.transaction_feed and batch.bank_name = 'Plaid'
), verified_batches as (
    select batch_id, account_id,
           md5('plaid-bank-batch:' || account_id::text || ':' ||
               string_agg(external_reference, '|' order by sort_reference collate "C")) as batch_digest
      from candidate_lines
     group by batch_id, account_id, line_count
    having line_count > 0 and count(*) = line_count
       and count(distinct sort_reference collate "C") = count(*)
       and count(*) filter (where line_account_id = account_id
               and running_balance is null
               and external_reference collate "C" ~ '^[A-Za-z0-9_-]+$'
               and statement_line_id = (
                   substr(line_digest, 7, 2) || substr(line_digest, 5, 2) ||
                   substr(line_digest, 3, 2) || substr(line_digest, 1, 2) ||
                   substr(line_digest, 11, 2) || substr(line_digest, 9, 2) ||
                   substr(line_digest, 15, 2) || substr(line_digest, 13, 2) ||
                   substr(line_digest, 17, 16))::uuid) = count(*)
)
update __SCHEMA__.bank_statement_batch batch
   set transaction_feed = true
  from verified_batches verified
 where batch.batch_id = verified.batch_id
   and not batch.transaction_feed
   and batch.batch_id = (
       substr(verified.batch_digest, 7, 2) || substr(verified.batch_digest, 5, 2) ||
       substr(verified.batch_digest, 3, 2) || substr(verified.batch_digest, 1, 2) ||
       substr(verified.batch_digest, 11, 2) || substr(verified.batch_digest, 9, 2) ||
       substr(verified.batch_digest, 15, 2) || substr(verified.batch_digest, 13, 2) ||
       substr(verified.batch_digest, 17, 16))::uuid;
