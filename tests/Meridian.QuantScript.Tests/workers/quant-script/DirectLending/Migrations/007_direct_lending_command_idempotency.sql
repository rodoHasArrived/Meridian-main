create unique index if not exists ux_loan_event_command_id
    on __SCHEMA__.loan_event (loan_id, command_id)
    where command_id is not null;
