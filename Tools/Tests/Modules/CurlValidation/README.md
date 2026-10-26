# libcurl HTTP handler validation

Runs `CurlHttpMessageHandler` against loopback HTTP servers with the bundled
Windows x64 `libcurl.dll`. The harness links the production sources under
`Assets/Plugins/MajdataPlay/Net/Curl`, the handler, `PooledArray` and
`UnsafeHelper`. Unity, the logger, and `Pool<T>` are test doubles. The test
`Pool<T>` reuses returned arrays, poisons them, and counts double returns.

Run from the repository root with .NET SDK 9 on Windows x64:

```powershell
dotnet run -c Release --project Tools/Tests/Modules/CurlValidation/CurlValidation.csproj
```

If the default SDK is newer and restore fails with `NU1101`, run from a directory
containing `global.json` that selects an installed 9.0 SDK. Pass the project by
absolute path. Build output goes to `Temp/CurlValidation`. Pass check names as
arguments to run only those checks.

Every check also fails if the worker logged an error or a pooled buffer was
returned twice. The checks cover:

- Shutdown: the six `Tools/Tests/Modules/Editor/CurlShutdownTests` scenarios. These
  cover idle and concurrent sync/async disposal, requests waiting for headers,
  responses that outlive the client, and requests or cancellations racing with
  disposal.
- Body delivery: an intact 8 MiB body, and a body whose libcurl writes exceed
  the backpressure high-water mark while the buffer is empty.
- Dispose races: disposing the response or the client while another thread is
  blocked reading the body.
- Native lifetime: these checks use `LibCurlLifecycle`'s reference count.
  Disposing a completed response releases its easy handle synchronously, and a
  request cancelled before its headers arrive releases it without a finalizer.
  A completed request does not stay registered on a long-lived cancellation
  token.
- Upload failures: an upload stream that throws fails the request instead of
  unwinding through libcurl.
- Body buffer: the backpressure buffer follows `ResponseBodyBufferSize`, not
  `MaxResponseHeadersLength`.
- Header limit: chunked trailers that arrive after the response has been
  published do not abort the transfer. Each header line counts once. Redirect
  and 1xx responses get their own budget, and headers over the limit still fail
  the request.
- SSL options: the native CA bit and the revocation bit are combined into one
  `CURLOPT_SSL_OPTIONS` value. libcurl cannot report the option back, and an
  HTTPS check would need a root in the Windows certificate store. This check
  therefore inspects the computed bitmask instead of a transfer.

`curl_multi_init` returning `NULL` is not covered. It only happens when
allocation fails, and loopback HTTP cannot cause that.

This is a CoreCLR test. It does not cover Mono/IL2CPP callback marshalling,
other platforms' libcurl builds, HTTPS, proxies, or Unity scene lifecycles.
