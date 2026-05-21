# Examples

Sample collections and environments for trying out PacketPanda. They target
public echo servers, so requests can be sent without any setup or credentials.

## Files

| File | What it is |
| --- | --- |
| `Echo API Samples.collection.json` | A collection demonstrating query params, JSON bodies, all three auth types, pre-request scripts, post-response scripts, and tests. |
| `Beeceptor Echo.environment.json` | Alternative environment pointing at `https://echo.free.beeceptor.com`. |

## How to use

1. Open the collection: **Collections sidebar -> Open... -> Open file(s)...** and pick
   `Echo API Samples.collection.json`.
2. Open an environment: **Environments sidebar -> Open environment file...** and pick
   `Beeceptor Echo.environment.json`.
   (Environment files must be opened from the Environments sidebar, not the
   Collections "Open file(s)" action.)
3. Set it as the active environment, then open any request and press **Send**.

The collection is built against [Beeceptor's echo server](https://echo.free.beeceptor.com),
which reflects your request back as JSON — query params under `parsedQueryParams`,
the request body under `parsedBody`, headers under `headers` — so the tests can make
real assertions.

## What each folder shows

- **Basics** — a GET with query params and a POST with a JSON body. `POST JSON body`
  also has a pre-request script that writes a variable the test later reads.
- **Auth** — the echo endpoint reached with Bearer, Basic, and API-key auth; the tests
  confirm the auth was applied to the outgoing request.
- **Scripting & Tests** — `Login - capture token` reads a token out of the response
  and stores it into the environment as `{{authToken}}`; `Use captured token` then
  sends that token on the next request. `Method & status checks` asserts on the
  status code and the echoed request method.

## Scripting API quick reference

Scripts run in a sandboxed JS engine. The `pp` object exposes:

- `pp.environment.get(key)` / `pp.environment.set(key, value)`
- `pp.response.status` / `pp.response.body` / `pp.response.json()`
- `pp.test(name, fn)` — register a test
- `pp.expect(value).to.equal(x)` / `.to.be.above(n)` / `.to.include(substr)`
