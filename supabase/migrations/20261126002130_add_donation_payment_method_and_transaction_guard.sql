-- Guarda a forma de contribuição e impede duplicidade de entrada financeira
-- para a mesma contribuição confirmada.
ALTER TABLE public."Donations"
  ADD COLUMN IF NOT EXISTS "PaymentMethod" character varying(30) NOT NULL DEFAULT 'PIX';

CREATE UNIQUE INDEX IF NOT EXISTS "UX_Transactions_DonationId"
  ON public."Transactions" ("DonationId")
  WHERE "DonationId" IS NOT NULL;
