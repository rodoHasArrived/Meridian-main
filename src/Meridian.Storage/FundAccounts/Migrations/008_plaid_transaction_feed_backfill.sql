-- Plaid deliveries retained before 007 added transaction_feed defaulted to complete statements.
-- The sync now redelivers their transactions as feed deltas, which a complete statement's guards
-- refuse, so the sync could never advance. They are recognised by the bank name the sync records
-- and by carrying no statement balance; a batch with a statement balance stays a statement.
update __SCHEMA__.bank_statement_batch batch
   set transaction_feed = true
 where not batch.transaction_feed
   and batch.bank_name = 'Plaid'
   and not exists (select 1 from __SCHEMA__.bank_statement_line line
                    where line.batch_id = batch.batch_id and line.running_balance is not null);
