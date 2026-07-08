# IOPP Document Classifier — Presentation Brief

*Use this as source material for an AI presentation generator (e.g. "create a 10-slide deck from this brief").*

## 1. What it is (one-liner)

A web application that automatically classifies uploaded maritime PDF documents (IOPP Certificate Supplements vs. Tank Diagrams) and extracts structured data (sludge tanks, bilge water holding tanks, disposal means) from them — removing the need for manual document review.

## 2. The problem it solves

- Ships carry many PDF certificates and diagrams (IOPP Certificate Supplements, Tank Capacity Diagrams, etc.).
- Sorting and reading these by hand to find specific data (e.g. sludge tank capacities, disposal methods) is slow and error-prone.
- This tool automates: **upload → classify document type → extract key structured fields → store as searchable records**.

## 3. Who uses it / how

1. User uploads a PDF via a web frontend (two options exist — see below).
2. System classifies the document as:
   - **IOPP Certificate Supplement**
   - **Tank Diagram / Capacity Plan**
   - **Unknown Document** (flagged for manual review)
3. User can then trigger extraction to pull structured data out of an IOPP document:
   - Section 3.1 — oil residue (sludge) tanks (tank ID, frame range, port/center/starboard position, volume in m³, total volume)
   - Section 3.2 — disposal means (incinerator, auxiliary boiler, other means + description)
   - Section 3.3 — bilge water holding tanks (same structure as 3.1)
4. All uploads and results are persisted as records that can be listed, viewed, or deleted later.

## 4. Architecture at a glance

```
Frontend (Angular app OR static HTML page)
        │  POST /classify (PDF upload)
        ▼
C# ASP.NET Core API  (IoppClassifier/)
   - saves PDF to disk (uploads/)
   - forwards file to Python service
   - persists record metadata (records.json)
        │
        ▼
Python FastAPI service  (app.py)
   - classify()   → heuristic document-type detection
   - extract_sections() → structured data extraction
```

**Two frontends currently exist, both talking to the same C# API:**
| Frontend | Tech | Notes |
|---|---|---|
| `index.html` | Static JS | Root-level page, upload + record browsing |
| `iopp-classifier/` | **Angular 9** | Standalone Angular app, `IoppService` calls the same endpoints |

**Backend layers:**
| Layer | Tech | Role |
|---|---|---|
| `IoppClassifier/` | ASP.NET Core (.NET 8) | Orchestration API — receives uploads, saves files, calls Python, stores metadata, serves results |
| `app.py` + friends | Python / FastAPI | Does the actual PDF parsing (via `pdfplumber`), classification, and extraction |

**Storage:** simple and local — PDFs on disk (`uploads/`), metadata in a flat `records.json` file. No database yet.

## 5. How classification actually works (important nuance for Q&A)

- **This is currently rule-based / heuristic, not AI/LLM-based.**
- Classification runs in tiers, most confident first:
  1. **Tier 1 — content scan**: looks for the exact "Regulation 12" declaration sentence in the PDF text.
  2. **Tier 2 — filename heuristic**: filename contains "IOPP" or ≥3 IOPP-related keywords.
  3. **Tier 3 — content keywords**: PDF text contains ≥3 tank-diagram-related keywords (capacity, tank, LCG, VCG, frame, etc.), or filename hints at "capacity/tank/diagram".
  4. **Default**: unclassified → flagged for manual review.
- Extraction uses regex/text-anchor parsing to pull specific certificate sections and tank tables out of the PDF text.

## 6. Key API endpoints

| Endpoint | Purpose |
|---|---|
| `POST /classify` | Upload a PDF, get classification result, record is saved |
| `POST /classify/extract` | Extract structured sections from a PDF |
| `GET /classify/extract-saved/{id}` | Extract sections from an already-saved record |
| `GET /classify/records` | List all saved records |
| `DELETE /classify/records/{id}` | Delete a record |

## 7. Current status

- Working end-to-end prototype: upload → classify → extract → view records (see `Prototype.png` for a UI screenshot).
- Two parallel frontends exist (static HTML + Angular); not yet consolidated into one.
- Angular app is on an older version (Angular 9) — would benefit from an upgrade.
- No database — storage is local files (`uploads/`, `records.json`); fine for a prototype, not production-scale.
- No authentication/access control yet.
- Classification/extraction is heuristic (regex/keyword based) — works well for the specific IOPP certificate template it was built against, but is not resilient to different document layouts/formats.

## 8. Natural next steps (good talking points for "roadmap" slide)

- Consolidate to a single frontend (retire the static HTML page or the Angular app).
- Upgrade Angular to a current LTS version.
- Move from `records.json` to a real database for multi-user/production use.
- Add authentication and audit logging (who uploaded/deleted what).
- Consider an LLM-assisted extraction fallback for documents that don't match the current template exactly, to improve robustness beyond fixed regex patterns.
- Add automated tests around the classification tiers and extraction parsing.

## 9. Suggested slide breakdown

1. Title / problem statement
2. Who it's for & the manual pain today
3. Solution overview (one diagram)
4. How it works — upload → classify → extract (with screenshot)
5. Architecture (frontend/backend/Python split)
6. Tech stack summary
7. Current status & limitations (be upfront: heuristic-based, prototype-stage)
8. Roadmap / next steps
9. Demo / screenshot
10. Questions
