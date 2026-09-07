# RAG generate (GeekBackend scope)

Status: **Phase C + F shipped**; Phase D consumers wired with soft-disable until Rag GraphRAG / ad-template index exist.

## Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| GET | `/api/rag/health` | optional | Liveness + available flag |
| GET | `/api/rag/status` | required | Soft-detect + intent/entity/model catalogs |
| GET | `/api/rag/entities` | required | Phase 0 seed entity names |
| POST | `/api/rag/generate` | required | Intent-routed draft + sources |

## Soft-disable

- Off when `GEEK_CRAWLER_RAG_URL` unset (`IGeekCrawlerRagClient.IsEnabled == false`)
- Or `GEEK_RAG_GENERATE_ENABLED=false` / `0` / `off`
- Graph retrieval: **off** unless `GEEK_RAG_GRAPH_ENABLED=true`
- Ad-template index: **off** unless `GEEK_RAG_AD_TEMPLATES_ENABLED=true` (client-supplied templates still work)

## Model routing (Phase F)

| Family | Env | Default |
|--------|-----|---------|
| Long-form | `GEEK_RAG_LONGFORM_MODEL` | `o3` |
| Short-form | `GEEK_RAG_SHORTFORM_MODEL` | `gpt-4o` |
| Battlecard | `GEEK_RAG_BATTLECARD_MODEL` | `gpt-4o` |
| Slides | `GEEK_RAG_SLIDES_MODEL` | `gpt-4o` |

`OpenAiProvider` omits temperature / uses `max_completion_tokens` for o1/o3/o4 models.

## Retrieval

1. Latest `partner` + `competitors` runs for the caller
2. `QueryAsync` with preferParent/Child + optional `retrievalMode: graph` (forward-compatible)
3. Short-form injects client `adTemplates` as few-shot exemplars
4. Slides/strategy return `themeSources` + slide outline markdown

## Success criteria

- [x] Phase C generate + status + entities
- [x] Phase F: long-form intents use o1/o3 (or configured reasoning model); short-form stays on lighter model
- [x] Phase D consumers: graph + ad-template hooks with soft-disable; slides intents + theme sources; template injection
