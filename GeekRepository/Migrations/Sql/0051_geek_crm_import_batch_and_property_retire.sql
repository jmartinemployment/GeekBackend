-- 0051: two additive columns Phase 1 needs, both nullable so no existing row or writer changes.
--
--   participants.import_batch_id and contact_points.import_batch_id: the id of the import that
--   created the row, so a batch can be reviewed and archived as a unit. Null for rows made by the
--   receptionist or by hand.
--
--   property_definitions.retired_at: set when a property is retired. A retired property is hidden
--   from reads and refuses new values, but its stored values are kept for history.
--
-- Declared in GeekApplication/Schemas/GeekCrm.xsd (ImportBatchId, RetiredAt). The tenant keys and
-- row-level security that the plan also places in Phase 1 are a separate script, because they change
-- every table and need a database run before they can be pushed.

ALTER TABLE geek_crm.participants ADD COLUMN IF NOT EXISTS import_batch_id uuid NULL;
ALTER TABLE geek_crm.contact_points ADD COLUMN IF NOT EXISTS import_batch_id uuid NULL;
ALTER TABLE geek_crm.property_definitions ADD COLUMN IF NOT EXISTS retired_at timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_participants_import_batch
    ON geek_crm.participants (tenant_id, import_batch_id) WHERE import_batch_id IS NOT NULL;
