-- Row-level immutability does not cover TRUNCATE or a cascading truncation.
-- Keep certified packages, mapping revisions, decisions and provider observations append-only
-- at that statement boundary too. Mutable destination policy and outbox lease/status rows retain
-- their existing lifecycle; they cannot cascade away an immutable observation.

drop trigger if exists external_gl_mapping_no_truncate on __SCHEMA__.external_gl_mapping_profiles;
create trigger external_gl_mapping_no_truncate
    before truncate on __SCHEMA__.external_gl_mapping_profiles
    for each statement execute function __SCHEMA__.refuse_external_gl_evidence_mutation();

drop trigger if exists external_gl_package_no_truncate on __SCHEMA__.external_gl_posting_packages;
create trigger external_gl_package_no_truncate
    before truncate on __SCHEMA__.external_gl_posting_packages
    for each statement execute function __SCHEMA__.refuse_external_gl_evidence_mutation();

drop trigger if exists external_gl_decision_no_truncate on __SCHEMA__.external_gl_posting_decisions;
create trigger external_gl_decision_no_truncate
    before truncate on __SCHEMA__.external_gl_posting_decisions
    for each statement execute function __SCHEMA__.refuse_external_gl_evidence_mutation();

drop trigger if exists external_gl_receipt_no_truncate on __SCHEMA__.external_gl_posting_receipts;
create trigger external_gl_receipt_no_truncate
    before truncate on __SCHEMA__.external_gl_posting_receipts
    for each statement execute function __SCHEMA__.refuse_external_gl_evidence_mutation();
