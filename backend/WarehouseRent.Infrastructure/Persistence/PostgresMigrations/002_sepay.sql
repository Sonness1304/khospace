-- Additive upgrade. Never alter the frozen 001 baseline or copy owner credentials.
ALTER TABLE khospace."PaymentIntents"
    ADD COLUMN "BankTransferReference" character varying(15) COLLATE "C",
    ADD COLUMN "ExpiresAt" timestamp with time zone;

ALTER TABLE khospace."PaymentIntents" DROP CONSTRAINT "CK_PaymentIntent_Mode";
ALTER TABLE khospace."PaymentIntents" ADD CONSTRAINT "CK_PaymentIntent_Mode"
    CHECK ("Mode" IN ('Sandbox','Live') AND "Provider" IN ('Vnpay','Momo','SePay')
        AND ("Mode"='Live' OR "PaymentReceiptId" IS NULL));

ALTER TABLE khospace."PaymentIntents" ADD CONSTRAINT "CK_PaymentIntent_BankTransfer"
    CHECK (("Provider"='SePay' AND "Method"='SePayQr'
        AND "BankTransferReference" IS NOT NULL
        AND "BankTransferReference" ~ '^SEVQR[A-Z0-9]{10}$'
        AND "ExpiresAt" IS NOT NULL AND "ExpiresAt">"CreatedAt"
        AND "MerchantId" ~ '^[0-9A-F]{64}$' AND "CheckoutUrl" IS NULL)
        OR ("Provider"<>'SePay' AND "BankTransferReference" IS NULL AND "ExpiresAt" IS NULL));

CREATE UNIQUE INDEX "IX_PaymentIntents_BankTransferReference"
    ON khospace."PaymentIntents" ("BankTransferReference")
    WHERE "BankTransferReference" IS NOT NULL;
