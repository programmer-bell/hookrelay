-- HookRelay initial schema.
-- All statements are idempotent (`IF NOT EXISTS`) so re-applying this file
-- (for example after a Render branch switch) is a no-op instead of an error.

CREATE TABLE IF NOT EXISTS endpoints (
    id uuid PRIMARY KEY,
    slug text NOT NULL UNIQUE,
    name text NOT NULL,
    target_url text NOT NULL,
    signing_secret text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS captured_requests (
    id uuid PRIMARY KEY,
    endpoint_id uuid NOT NULL REFERENCES endpoints (id) ON DELETE CASCADE,
    method text NOT NULL,
    headers jsonb NOT NULL,
    body text,
    query text,
    idempotency_key text,
    received_at timestamptz NOT NULL DEFAULT now()
);

-- Lookup of requests for an endpoint's inspector, newest first.
CREATE INDEX IF NOT EXISTS idx_captured_requests_endpoint_received
    ON captured_requests (endpoint_id, received_at DESC);

-- Idempotency guard: one idempotency key per endpoint.
CREATE UNIQUE INDEX IF NOT EXISTS uq_captured_requests_endpoint_idempotency
    ON captured_requests (endpoint_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE TABLE IF NOT EXISTS deliveries (
    id uuid PRIMARY KEY,
    request_id uuid NOT NULL REFERENCES captured_requests (id) ON DELETE CASCADE,
    status text NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'delivering', 'succeeded', 'dead')),
    attempt_count int NOT NULL DEFAULT 0,
    next_attempt_at timestamptz,
    last_status_code int,
    last_error text,
    updated_at timestamptz NOT NULL DEFAULT now()
);

-- Queue claim lookup (SKIP LOCKED) and retry scheduling.
CREATE INDEX IF NOT EXISTS idx_deliveries_status_next_attempt
    ON deliveries (status, next_attempt_at);

CREATE TABLE IF NOT EXISTS delivery_attempts (
    id uuid PRIMARY KEY,
    delivery_id uuid NOT NULL REFERENCES deliveries (id) ON DELETE CASCADE,
    attempted_at timestamptz NOT NULL DEFAULT now(),
    status_code int,
    error text,
    duration_ms int
);