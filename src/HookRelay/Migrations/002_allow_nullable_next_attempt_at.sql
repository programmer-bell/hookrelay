-- Terminal delivery states ('succeeded'/'dead') carry no next attempt.
ALTER TABLE deliveries
    ALTER COLUMN next_attempt_at DROP NOT NULL;