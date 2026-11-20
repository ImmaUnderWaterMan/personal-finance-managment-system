BEGIN;

CREATE TABLE public.users (
    id uuid NOT NULL,
    email varchar(256) NOT NULL,
    display_name varchar(100) NOT NULL,
    password text NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_users PRIMARY KEY (id),
    CONSTRAINT ck_users_id CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_users_email CHECK (char_length(btrim(email)) BETWEEN 3 AND 256),
    CONSTRAINT ck_users_display_name CHECK (char_length(btrim(display_name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_users_password CHECK (char_length(btrim(password)) > 0)
);

CREATE UNIQUE INDEX ux_users_email ON public.users (lower(btrim(email)));

ALTER TABLE public.accounts
    ADD CONSTRAINT fk_accounts_users FOREIGN KEY (user_id) REFERENCES public.users (id) ON DELETE RESTRICT,
    ADD CONSTRAINT uq_accounts_id_user UNIQUE (id, user_id);

COMMIT;
