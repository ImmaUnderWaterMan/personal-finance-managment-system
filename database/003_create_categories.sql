BEGIN;
 
CREATE TABLE public.categories (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    name varchar(100) NOT NULL,
    type integer NOT NULL,
    CONSTRAINT pk_categories PRIMARY KEY (id),
    CONSTRAINT fk_categories_users FOREIGN KEY (user_id) REFERENCES public.users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_categories_name CHECK (char_length(btrim(name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_categories_type CHECK (type IN (0, 1)),
    CONSTRAINT uq_categories_id_user_type UNIQUE (id, user_id, type)
);

CREATE INDEX ix_categories_user_id ON public.categories (user_id);

COMMIT;
