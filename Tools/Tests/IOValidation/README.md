# I/O device protocol validation

Run from the repository root with .NET SDK 9 or newer:

```powershell
dotnet run --project Tools/Tests/IOValidation/IOValidation.csproj
```

The harness links the production device lifecycle, standalone keyboard device,
standalone `KeyboardHelper` mappings and sampling, Rosen/Yuan/Dao button parsers, serial and pipe parsers,
input frame state buffer, device factory, and serial/Dao LED encoders. No NuGet
packages are required. Transport, Unity input controls, and game services are
small test doubles.

Checks cover direct inheritance from transport bases, capability interfaces,
all twelve logical/keyboard/gamepad bindings, keyboard sampling and function-key
overlays, keyboard removal, key pulses between snapshots,
idempotent worker start/disposal, daemon configuration, cancellation
of blocked I/O, reconnection, player selection, malformed and fragmented reports,
press/release pulses between frames, repeated reads of shared device
capabilities, one snapshot per shared device, capability polling intervals,
disconnect release, all eight LED colors, brightness and checksum encoding,
change throttling, and output replay after reconnect.

Dao factory checks cover all capabilities enabled with different input/output
connection options, LED-only mode with no input reads, input-only mode with no LED
writes, and all capabilities disabled. Enabled capabilities share one device and
one HID connection.

Expected result: `IO_VALIDATION_PASSED` with an assertion count. Physical device
discovery, native USB/HID/serial driver cancellation, and Unity lifecycle
still require integration checks with the corresponding hardware.
