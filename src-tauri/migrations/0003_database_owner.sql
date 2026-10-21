CREATE TABLE database_owner (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    process_id INTEGER NOT NULL,
    process_started INTEGER NOT NULL,
    executable TEXT NOT NULL,
    instance_token TEXT NOT NULL
);
