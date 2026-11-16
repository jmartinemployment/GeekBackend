-- Geek-Receptionist billing standing (docs/Geek-Receptionist.md section 5, item 2), decided 2026-11-16: a failed overage charge or a
-- PayPal subscription PayPal suspends suspends the tenant; its number keeps answering for seven days and then stops until the overage
-- is paid and PayPal holds the subscription ACTIVE again. A subscription PayPal cancels or expires ends the tenant at the end of its
-- paid period, and its Twilio number and Retell agent are released. The checks are NOT VALID: they hold every row written from now on
-- without scanning the rows already there, none of which can break them, so they cannot stop GeekRepository from starting.

ALTER TABLE geek_voice.tenants ADD COLUMN IF NOT EXISTS suspended_at TIMESTAMPTZ NULL;
ALTER TABLE geek_voice.tenants ADD COLUMN IF NOT EXISTS answering_stopped_at TIMESTAMPTZ NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_tenants_suspended_at') THEN
        ALTER TABLE geek_voice.tenants ADD CONSTRAINT ck_voice_tenants_suspended_at
            CHECK ((status = 'suspended') = (suspended_at IS NOT NULL)) NOT VALID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_tenants_answering_stopped') THEN
        ALTER TABLE geek_voice.tenants ADD CONSTRAINT ck_voice_tenants_answering_stopped
            CHECK (answering_stopped_at IS NULL OR status IN ('suspended', 'cancelled')) NOT VALID;
    END IF;
END
$$;

-- When a cancelled tenant's Twilio subaccount was closed and its Retell number, agent and model deleted. Null while any of them may
-- still exist; the ids of anything that could not be removed are in GeekAPI's Error log.
ALTER TABLE geek_voice.tenant_telephony ADD COLUMN IF NOT EXISTS released_at TIMESTAMPTZ NULL;

-- An overage the automatic charge could not take, paid later by the owner through PayPal checkout. The charge stays 'failed' (what
-- the automatic charge did); these say it has since been paid, and with which PayPal order and capture.
ALTER TABLE geek_voice.overage_charges ADD COLUMN IF NOT EXISTS collected_order_id TEXT NULL;
ALTER TABLE geek_voice.overage_charges ADD COLUMN IF NOT EXISTS collected_capture_id TEXT NULL;
ALTER TABLE geek_voice.overage_charges ADD COLUMN IF NOT EXISTS collected_at TIMESTAMPTZ NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_voice_overage_collected') THEN
        ALTER TABLE geek_voice.overage_charges ADD CONSTRAINT ck_voice_overage_collected CHECK (
            (collected_at IS NULL) = (collected_order_id IS NULL)
            AND (collected_at IS NULL) = (collected_capture_id IS NULL)
            AND (collected_at IS NULL OR status = 'failed'));
    END IF;
END
$$;

-- The hourly sweep finds the suspended tenants before it knows any tenant, as it finds the periods that ended (0062). FOR SELECT on
-- this table only; it reads the tenant id, and everything it then does runs on a connection that names the tenant.
DROP POLICY IF EXISTS billing_sweep ON geek_voice.tenants;
CREATE POLICY billing_sweep ON geek_voice.tenants FOR SELECT
    USING (current_setting('app.billing_sweep', true) = 'on');
