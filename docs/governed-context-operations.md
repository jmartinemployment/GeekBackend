# Governed context operations

## Frozen production contract

- Authority: GeekRepository `content_creator_v2`; Qdrant and object storage are rebuildable derivatives.
- Authorization: owner-only UUID principal matching on every aggregate and nested resource.
- Manifest: `run-context-manifest.v1` (`schemaVersion: 1`), canonical property ordering, SHA-256 digest, HMAC-SHA-256 over the lowercase digest, base64-encoded keys, and a required key ID.
- Lifecycle: `draft -> in_review -> approved -> deprecated|revoked`. Deprecated pinned versions remain reproducible with warnings. Revoked versions are never executable.
- Freshness: 180 days by default. Set `ContentCreatorV2__ContextPolicy__RequireFreshKnowledge=true` to make stale Knowledge blocking.
- Retention: run attachments expire after 30 days and remain pinned until their manifest retention window ends.
- Quotas: 10 MiB/file, 100 MiB/run attachments, and 1 GiB/owner by default.
- Approved parsers: UTF-8 text, Markdown, HTML, PDF (PdfPig), DOCX, PPTX, and XLSX (bounded local Open XML extraction).
- Image, audio, and video remain fail-closed until local OCR/transcription/keyframe models, golden fixtures, resource budgets, and data-processing approval are configured. They must not be routed to a hosted parser.
- Connector SPI is enabled but ships with no connector. Google Search Console is specifically excluded until an owner-authenticated Geek-SEO service boundary is approved.

## Threat model and controls

| Threat | Control |
|---|---|
| Cross-owner enumeration or retrieval | Owner is checked in GeekAPI and repository lookups; RAG filters owner plus manifest allowlist. |
| Object substitution | Presigned PUT binds content length/type; finalization recomputes size and SHA-256. |
| Malware or executable upload | Private quarantine plus ClamAV INSTREAM scan; missing/unhealthy scanner fails closed. |
| Parser exploit or resource exhaustion | Local parsers only; byte, character, page, part, expansion-ratio, slide, and sheet limits; macros, traversal, symlinks, malformed data, and signature spoofing rejected. |
| Prompt/source authority injection | Source content cannot change manifest, tools, models, skills, lifecycle, or owner policy. |
| Manifest tampering/replay | Canonical digest, fixed-time signature verification, exact job binding, and durable specialist execution claims. |
| Stale Qdrant points after revocation | Revoked manifests fail verification; exact owner/version/resource tombstone is also sent to RAG. |
| Product claim drift | Product Schema is pinned automatically; selected field IDs are schema checked; prohibited claims and mandatory disclaimers are deterministically validated. |
| Credential exposure | Object credentials and signatures are never returned; browser receives only short-lived upload grants. |

## Deployment order

1. Apply the additive EF migration to a disposable database, run migration rollback/upgrade tests, then apply to staging.
2. Provision private object storage and ClamAV. Confirm private DNS and persistent ClamAV definitions.
3. Configure GeekAPI object-store, quota, scanner, and manifest-signing variables.
4. Configure Geek-Crawler-Rag `API_KEY` and `CONTEXT_MANIFEST_SIGNING_KEYS` with the same active/retired manifest key map.
5. Deploy RAG first. Smoke authenticated index, query, and tombstone endpoints.
6. Deploy GeekRepository, then GeekAPI readers, then writers.
7. Enable manifest creation only after an upload-to-index-to-query staging exercise succeeds.
8. Build a new Qdrant collection, compare owner/manifest-scoped retrieval, then move the alias atomically.

Never deploy GeekAPI manifest transmission before RAG has the matching signing key. Keep retired verification keys in both services until every retained manifest using them has expired.

Server-side rollout gates default to enabled for compatibility. Disable independently with
`ContentCreatorV2__ContextFeatures__Catalogs`,
`ContentCreatorV2__ContextFeatures__Ingestion`,
`ContentCreatorV2__ContextFeatures__Resolution`, and
`ContentCreatorV2__ContextFeatures__ManifestRetrieval`. RAG indexing/tombstones and retrieval are independently gated by
`CONTEXT_ASSET_INDEXING_ENABLED` and `CONTEXT_MANIFEST_QUERY_ENABLED`. Enable readers before
writers and enable RAG verification before context resolution.

## Recovery runbooks

### Failed or stuck ingestion

1. Locate the ingestion job by ID and owner; inspect its durable events and terminal error.
2. For an expired lease, restart GeekAPI. Startup recovery requeues queued and expired-running jobs.
3. Verify ClamAV, object access, and authenticated RAG health.
4. Requeue only after correcting the dependency. Never mark a job ready manually.

### Object exists but finalization failed

1. Compare registered object key, authorized byte size, and SHA-256 with the private object.
2. If all match and the upload grant belongs to the owner, repeat the JSON completion call.
3. If any value differs, delete the object and abandoned draft revision; issue a new grant.

### Version ready but index missing

1. Confirm the exact normalized resource digest and parser metadata.
2. Call the owner-authorized version reindex endpoint.
3. Query using a signed manifest containing only that owner/version and verify exact evidence coordinates.
4. Do not edit the ready version or point payload in place.

### Qdrant rebuild and rollback

1. Create a new collection with the current embedding dimensions and payload indexes.
2. Replay all non-revoked immutable resources through authenticated indexing.
3. Compare zero-hit rate, evidence IDs, owner isolation, and manifest isolation.
4. Move the alias to the new collection. Roll back by moving the alias back; do not delete either collection during observation.

### Signing-key rotation

1. Generate at least 32 random bytes and assign a new dated key ID.
2. Add the key to GeekAPI and RAG verification maps.
3. Change only GeekAPI `ActiveKeyId`; deploy RAG before GeekAPI.
4. Verify a shared golden fixture and one staging job.
5. Retain old keys for historical verification; remove only after manifest retention expires.

### Revocation after manifests exist

1. Revoke the exact version with actor and reason.
2. Confirm RAG tombstone success. If it fails, retrieval still fails closed because the persisted manifest is revalidated.
3. Existing jobs become terminal on their next execution boundary; do not silently substitute a newer version.
4. Use explicit refresh-and-rerun to create a new job and manifest.

### Owner export/deletion

1. Export catalog metadata, immutable payloads, resource digests, manifests, findings, and audit records by owner.
2. Delete Qdrant points and private objects by exact owner/resource IDs.
3. Soft-delete temporary attachments and retire catalogs.
4. Preserve immutable audit records according to legal retention; never transfer them to another owner.

## Observability and alerts

Correlate structured logs/metrics by request ID, owner, create, job, attempt, manifest, asset version, and ingestion job. Never emit raw source content, object credentials, signatures, or private object keys.

GeekAPI emits `gcc_v2_context_upload_bytes`, `gcc_v2_context_upload_quota_rejections`,
`gcc_v2_context_ingestion_outcomes`, `gcc_v2_context_ingestion_latency_seconds`,
`gcc_v2_context_retention_outcomes`, `gcc_v2_context_resolution_outcomes`, and
`gcc_v2_context_manifest_entries`.

Alert on scanner unavailability, scan/parser failure rate, ingestion lease expiry, indexing lag, context blocks, signature failures, zero-hit manifest queries, citation drops, stale-source use, quota rejection, retention backlog, and unexpected modality cost.

## Rollback

- Disable new manifest-backed job creation, but preserve reads and execution for already-created manifests.
- Disable one parser/media type independently; never fall back to a hosted parser.
- Move the Qdrant alias back to the prior collection.
- Roll back application code without dropping additive tables or columns.
- Preserve object and manifest data until recovery or retention cleanup is complete.
