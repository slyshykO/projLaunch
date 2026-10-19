CREATE TABLE projects (
    id TEXT PRIMARY KEY NOT NULL CHECK (length(trim(id)) > 0),
    name TEXT NOT NULL,
    last_opened TEXT NOT NULL,
    description TEXT NOT NULL,
    path TEXT NOT NULL,
    ide TEXT NOT NULL,
    environment_json TEXT NOT NULL,
    remote_json TEXT NOT NULL
);

CREATE TABLE data_migrations (
    name TEXT PRIMARY KEY NOT NULL,
    applied_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    item_count INTEGER NOT NULL CHECK (item_count >= 0)
);
