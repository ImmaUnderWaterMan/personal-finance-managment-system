BEGIN;
 
CREATE TABLE public.budgets (
    user_id uuid NOT NULL,
    category_id uuid NOT NULL,
    year integer NOT NULL,
    month integer NOT NULL,
    spending_limit numeric(18, 2) NOT NULL,
    category_type integer NOT NULL DEFAULT 1,
    CONSTRAINT pk_budgets PRIMARY KEY (user_id, category_id, year, month),
    CONSTRAINT fk_budgets_categories FOREIGN KEY (category_id, user_id, category_type)
        REFERENCES public.categories (id, user_id, type) ON DELETE RESTRICT,
    CONSTRAINT ck_budgets_category_type CHECK (category_type = 1),
    CONSTRAINT ck_budgets_period CHECK (
        year BETWEEN 1 AND 9999 AND month BETWEEN 1 AND 12 AND NOT (year = 9999 AND month = 12)),
    CONSTRAINT ck_budgets_limit CHECK (spending_limit > 0)
);

CREATE INDEX ix_budgets_user_period ON public.budgets (user_id, year, month);
CREATE INDEX ix_budgets_category_user ON public.budgets (category_id, user_id);

COMMIT;
