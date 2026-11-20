BEGIN;
 
CREATE TABLE public.transactions (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    request_id uuid NOT NULL,
    account_id uuid NOT NULL,
    category_id uuid NOT NULL,
    type integer NOT NULL,
    amount numeric(18, 2) NOT NULL,
    operation_date date NOT NULL,
    comment varchar(150),
    CONSTRAINT pk_transactions PRIMARY KEY (id),
    CONSTRAINT uq_transactions_user_request UNIQUE (user_id, request_id),
    CONSTRAINT fk_transactions_accounts FOREIGN KEY (account_id, user_id)
        REFERENCES public.accounts (id, user_id) ON DELETE RESTRICT,
    CONSTRAINT fk_transactions_categories FOREIGN KEY (category_id, user_id, type)
        REFERENCES public.categories (id, user_id, type) ON DELETE RESTRICT,
    CONSTRAINT ck_transactions_request_id CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_transactions_type CHECK (type IN (0, 1)),
    CONSTRAINT ck_transactions_amount CHECK (amount > 0)
);

CREATE INDEX ix_transactions_user_date ON public.transactions (user_id, operation_date);
CREATE INDEX ix_transactions_account_user ON public.transactions (account_id, user_id);
CREATE INDEX ix_transactions_category_user_date ON public.transactions (category_id, user_id, operation_date);

COMMIT;
