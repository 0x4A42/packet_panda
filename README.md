# PacketPanda

A free, lightweight, cross-platform desktop API client for exploring, testing, and debugging HTTP APIs.

## What is PacketPanda?

PacketPanda is a developer tool for hitting HTTP endpoints — think Postman or Bruno, but built to be fast, minimal, and file-first. Requests are stored as plain TOML files on disk, making them version-controllable and shareable without any import/export ceremony.

## Features

- **Cross-platform** — Windows, macOS, and Linux via Avalonia UI
- **File-based storage** — requests and environments live as TOML files; your filesystem is the source of truth
- **Environment support** — switch between dev, staging, and prod with named environment files
- **Authentication** — API Key, Bearer Token, Basic Auth, and OAuth 2.0
- **Response viewer** — syntax-highlighted body, headers, status code, timing, and per-request history
- **3-panel layout** — collections sidebar, request editor, response viewer

## Tech Stack

- C# / .NET 10
- Avalonia UI
- ReactiveUI (MVVM)
- Tomlyn (TOML parsing)
- AvaloniaEdit (syntax highlighting)

## Status

Early development. Design phase in progress.
