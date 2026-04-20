# PacketPanda — Design Document

## Overview

PacketPanda is a free, lightweight, cross-platform desktop API client for exploring, testing, and debugging HTTP APIs. It is built as a personal tool but will be made publicly available as an open source project.

The guiding philosophy is simplicity and transparency: requests are stored as plain TOML files on disk, making them version-controllable, shareable, and hand-editable without any import/export ceremony. This is inspired by Bruno's file-first approach.

## Goals

- Fast, lightweight desktop application for hitting HTTP endpoints
- Cross-platform (Windows, macOS, Linux)
- File-based persistence — no proprietary database or export formats
- Clean, focused UI without unnecessary complexity

## Tech Stack

| Concern | Choice |
|---|---|
| Language | C# (.NET 10) |
| UI Framework | Avalonia UI |
| MVVM Framework | ReactiveUI |
| HTTP | System.Net.Http.HttpClient |
| File Storage | TOML via Tomlyn |
| Syntax Highlighting | AvaloniaEdit |
| JS Scripting Engine | Jint |
| Testing | xUnit + NSubstitute |

## UI Layout

3-panel layout:
- **Left panel** — Collections sidebar (folders and saved requests)
- **Center panel** — Request editor (method, URL, headers, body, auth)
- **Right panel** — Response viewer (body, headers, status, timing, history)

## Features (v1 Scope)

### HTTP Methods
- GET, POST, PUT, DELETE

### Authentication
- None
- API Key (header or query param)
- Bearer Token
- Basic Auth
- OAuth 2.0

### Request Persistence
- Requests saved as TOML files on disk
- Organised into collections (folders)
- No export required — files are the source of truth

### Environments
- Named environments (e.g., dev, staging, prod) with key/value variables
- Stored as TOML files alongside collections
- Variables can be referenced in requests (URLs, headers, body)

### Response Viewer
- Syntax-highlighted JSON/XML body
- Response headers
- Status code and timing
- Per-request response history (timestamped log of previous hits)

### Pre/Post Request Scripts
- JavaScript scripts attached to individual requests, executed via Jint
- **Pre-request scripts** run before the HTTP request is sent — useful for generating tokens, setting dynamic headers, or mutating environment variables
- **Post-request scripts** run after a response is received — useful for extracting values (e.g., auth tokens) from the response and writing them back into the active environment
- Scripts have access to a sandboxed context object exposing the request, response, and environment variable read/write API

### In-Client Tests
- JavaScript assertions written per request, run against the response after it is received
- Tests have access to the same response context as post-request scripts (status code, body, headers, timing)
- Results displayed inline as a pass/fail list with error messages
- Sample snippets surfaced in the UI to help users get started (e.g., check status code, assert JSON field value, assert response time)
- Tests and scripts share the same Jint engine instance per request execution — scripts run first, then tests

## Architecture Approach

ReactiveUI + Avalonia with a clean MVVM structure. The reactive programming model is a natural fit for the core async loop: fire HTTP request → observe response → update UI. ViewModels expose reactive commands and observable properties; Views bind declaratively.

The file system is the data layer — no ORM, no database. Collections and environments map directly to directory and file structures on disk.

## Testing Strategy

- **Unit tests** (xUnit + NSubstitute) — ViewModels, file parsing/serialisation, HTTP request building, environment variable resolution
- **Integration tests** — file read/write round-trips, HTTP client behaviour against a local test server
- NSubstitute used to mock file I/O and HTTP dependencies in unit tests
