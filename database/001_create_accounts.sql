BEGIN;

CREATE TABLE public.accounts (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    name varchar(100) NOT NULL,
    opening_balance numeric(18, 2) NOT NULL,
    is_archived boolean NOT NULL DEFAULT false,
    CONSTRAINT pk_accounts PRIMARY KEY (id),
    CONSTRAINT ck_accounts_name CHECK (char_length(btrim(name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_accounts_user_id CHECK (user_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

CREATE INDEX ix_accounts_user_id ON public.accounts (user_id);

COMMIT;
