# Scripting

PacketPanda runs JavaScript scripts at two points in the request lifecycle. Scripts are written per-request and executed in a sandboxed [Jint](https://github.com/sebastienros/jint) engine.

## Script sections

| Section | Runs | `pp.response` available |
|---|---|---|
| Pre-request | Before the HTTP request is sent | No |
| Post-response | After the HTTP response is received | Yes |
| Tests | After the post-response script | Yes |

The pre-request, post-response, and test scripts share the same engine instance and `pp` context object for the duration of a single request execution.

## The `pp` object

All scripting APIs are accessed through the global `pp` object.

### `pp.request`

Read-only snapshot of the outgoing request, available in all script sections.

| Property | Type | Description |
|---|---|---|
| `pp.request.url` | `string` | Resolved URL (environment variables already substituted) |
| `pp.request.method` | `string` | HTTP method in uppercase, e.g. `"GET"` |
| `pp.request.body` | `string` | Request body, or an empty string if none |

### `pp.response`

Available only in post-response scripts and tests. Throws if accessed in a pre-request script.

| Property / Method | Type | Description |
|---|---|---|
| `pp.response.status` | `number` | HTTP status code, e.g. `200` |
| `pp.response.body` | `string` | Raw response body as a string |
| `pp.response.json()` | `object` | Parses the response body as JSON and returns a JS object |

### `pp.environment`

Read and write the active environment's variables.

| Method | Description |
|---|---|
| `pp.environment.get(key)` | Returns the variable value, or `null` if not set |
| `pp.environment.set(key, value)` | Writes a value back to the active environment after the request completes |

`set` writes are applied to the active environment only when the HTTP request succeeds. If the request fails before a response is received, any pending writes are discarded.

### `pp.test(name, fn)`

Registers a named test. The function is called immediately; if it throws, the test is marked as failed. Results appear in the Tests tab of the response panel.

### `pp.expect(value)`

Returns an assertion object for use inside `pp.test` callbacks.

| Chain | Description |
|---|---|
| `.to.equal(expected)` | Strict equality (`===`) |
| `.to.be.above(n)` | Numeric greater-than |
| `.to.include(substring)` | String contains |

`.to.be` is a pass-through — `.to.equal(...)` and `.to.be.equal(...)` are equivalent.

## Examples

### Extract a token from a login response

```js
// Post-response script
var token = pp.response.json().access_token;
pp.environment.set("authToken", token);
```

### Assert the response shape

```js
// Tests
pp.test("status is 200", function () {
    pp.expect(pp.response.status).to.equal(200);
});

pp.test("body contains id", function () {
    pp.expect(pp.response.body).to.include("id");
});

pp.test("response time is reasonable", function () {
    // pp.response.status being available proves the request completed
    pp.expect(pp.response.status).to.be.above(0);
});
```

### Set a dynamic header before sending

```js
// Pre-request script
// pp.response is NOT available here
var ts = Date.now().toString();
pp.environment.set("requestTimestamp", ts);
```

Then reference `{{requestTimestamp}}` in a request header.
