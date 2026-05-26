<p align="center" width="100%">
    <img width="33%" src="src/PacketPanda.Desktop/app.ico"> 
</p>

# PacketPanda

A free, lightweight, cross-platform desktop API client for exploring, testing, and debugging HTTP APIs.

## What is PacketPanda?

PacketPanda is a developer tool for hitting HTTP endpoints — built to be fast, minimal, and file-first. Collections and environments are stored as plain JSON files on disk, so they are version-controllable and shareable with no import/export step.

## Features

- **Cross-platform** — Windows, macOS, and Linux via Avalonia UI
- **File-based storage** — collections and environments live as JSON files; your filesystem is the source of truth
- **Environment variables** — switch between dev, staging, and prod with named environment files; `{{variable}}` references are resolved at send time and highlighted in the editor
- **Authentication** — None, API Key, Bearer Token, and Basic Auth, injected into the request at send time
- **Body types** — JSON, plain text, XML, form-urlencoded, and multipart/form-data (with file upload)
- **Scripting & tests** — pre-request and post-response scripts run in a sandboxed JavaScript engine; write in-client tests that produce a pass/fail list
- **Response viewer** — syntax-highlighted body, headers, cookies, status code, timing, and size
- **cURL** — copy any request as a cURL command; import from cURL to open a new tab instantly
- **Request history** — every send is recorded automatically; click any entry to re-open it as a full editable tab; history persists across sessions
- **Settings** — configure SSL verification, redirect following, request timeout, and history limit
- **Light & dark themes** — switchable at runtime and remembered between sessions
- **3-panel layout** — collections sidebar, request editor, response viewer

## Getting started

```bash
dotnet run --project src/PacketPanda.Desktop
```

Sample collections and environments live in the [`examples/`](examples/) directory — open them from the sidebar to try the app against a public echo server.

## Tech Stack

- C# / .NET 10
- Avalonia UI
- ReactiveUI (MVVM)
- System.Text.Json (JSON persistence)
- Jint (sandboxed JavaScript for scripts and tests)
- AvaloniaEdit (script editor and syntax highlighting)

## Building and testing

```bash
dotnet build          # build the solution
dotnet test           # run all tests
```

## Future improvements

Currently in active development.
