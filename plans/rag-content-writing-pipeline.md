# RAG generate (GeekBackend scope)

Status: **Phase C + F + D consumers shipped** (GraphRAG + ad-template index wired; defaults ON).

## Soft-disable

- Off when `GEEK_CRAWLER_RAG_URL` unset
- Or `GEEK_RAG_GENERATE_ENABLED=false`
- Graph: `GEEK_RAG_GRAPH_ENABLED=false` to soft-disable (default ON)
- Ad-template index: `GEEK_RAG_AD_TEMPLATES_ENABLED=false` to soft-disable (default ON)

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/rag/status` | Soft-detect + models + feature flags |
| POST | `/api/rag/generate` | Intent-routed draft |
| POST | `/api/rag/templates` | Upsert ad templates → Rag `/v1/templates/index` |

## Success criteria

- [x] Phase C generate + status + entities
- [x] Phase F: long-form o1/o3 routing
- [x] Phase D consumers: graph themes + ad-template index query/upsert
