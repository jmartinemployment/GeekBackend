-- Geek-Receptionist call content policy (docs/Geek-Receptionist.md section 5, item 6).
--
-- A call's row no longer takes the caller's number, the transcript, the summary or the recording link: GeekRepository stops writing
-- all four with this script. The caller's number was required, so it becomes optional. What earlier calls stored in those columns
-- is left as it is here; deleting it is a separate, deliberate step.
ALTER TABLE geek_voice.call_logs ALTER COLUMN caller_number DROP NOT NULL;
