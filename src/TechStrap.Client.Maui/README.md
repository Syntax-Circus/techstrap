# TechStrap.Client.Maui

Device and app metadata capture and a submit-ticket helper for .NET MAUI apps, on top of TechStrap.Client. It reads the app version, platform, connectivity and (optionally) display and battery state through MAUI Essentials and attaches them to the ticket as metadata.

## Install

    dotnet add package TechStrap.Client.Maui

## Register

Register the client and the helper in one call, usually in `MauiProgram.cs`. The first delegate configures the client (use a Public key; see Privacy), the second one switches on opt-in device context.

```csharp
services.AddTechStrapMaui(
    client =>
    {
        client.BaseAddress = new Uri("https://support.example.com/");
        client.ApiKey = "<public key from configuration>";
    },
    context =>
    {
        context.IncludeDisplay = true;
    });
```

## Submit

Resolve `IMauiTicketSubmitter` in a page or view model and send a `MauiTicketDraft`. The helper adds the device context to the ticket's metadata. Supply a stable idempotency key if you may retry the same ticket.

```csharp
var submitter = services.GetRequiredService<IMauiTicketSubmitter>();
Result<SubmitTicketResponse> result = await submitter.SubmitAsync(
    new MauiTicketDraft("Crash on startup", "Steps: open the app, tap Sync.", "user@example.com")
    {
        IdempotencyKey = Guid.NewGuid().ToString("N"),
    },
    ct);

if (!result.IsSuccess)
{
    // result.Errors holds the code and message of each failure (see the error codes in TechStrap.Client).
}
```

A retry of the same ticket must reuse the same `IdempotencyKey`, or it can create a second ticket. The `Result` is the one `TechStrap.Client` returns, with the same error codes.

## What is collected

Defaults are collected while `IncludeDeviceContext` is true (the default). The extras are collected only when the matching option is switched on. Each field is read on its own: a field that cannot be read is skipped silently.

| Key | Source | Default or extra |
| --- | --- | --- |
| `app.name` | `IAppInfo.Name` | Default |
| `app.version` | `IAppInfo.VersionString` | Default |
| `app.build` | `IAppInfo.BuildString` | Default |
| `app.package` | `IAppInfo.PackageName` | Default |
| `os.platform` | `IDeviceInfo.Platform` | Default |
| `os.version` | `IDeviceInfo.VersionString` | Default |
| `device.manufacturer` | `IDeviceInfo.Manufacturer` | Default |
| `device.model` | `IDeviceInfo.Model` | Default |
| `device.idiom` | `IDeviceInfo.Idiom` | Default |
| `device.type` | `IDeviceInfo.DeviceType` | Default |
| `locale` | Current culture name | Default |
| `timezone` | Local time zone id | Default |
| `network.access` | `IConnectivity.NetworkAccess` (Android needs the `ACCESS_NETWORK_STATE` permission) | Default |
| `display.width` | `IDeviceDisplay.MainDisplayInfo` | Extra (`IncludeDisplay`) |
| `display.height` | `IDeviceDisplay.MainDisplayInfo` | Extra (`IncludeDisplay`) |
| `display.density` | `IDeviceDisplay.MainDisplayInfo`, invariant culture, for example "2.625" | Extra (`IncludeDisplay`) |
| `display.orientation` | `IDeviceDisplay.MainDisplayInfo` | Extra (`IncludeDisplay`) |
| `battery.state` | `IBattery.State` (Android needs `BATTERY_STATS`) | Extra (`IncludeBattery`) |
| `battery.level` | `IBattery.ChargeLevel` as a rounded whole percent; left out when the level is negative (Android needs `BATTERY_STATS`) | Extra (`IncludeBattery`) |

Android permissions: `ACCESS_NETWORK_STATE` for `network.access`, `BATTERY_STATS` for the battery fields. Without the permission the field is skipped, not an error.

iOS: display values may be missing when `SubmitAsync` runs off the UI thread (UIKit's thread check). `IncludeDisplay` is opt-in and a failure never crashes the app, so submit from the UI thread when you need them.

## Privacy

The helper never collects advertising or device ids, location, contacts, an IP address or user names. The table above is the whole set, and `IncludeDeviceContext = false` turns it all off.

A key inside an app can be extracted by anyone who has the app, so use a Public key only and never a Trusted key. The metadata an app sends under a Public key is stored but flagged as untrusted on the server: treat it as a hint, not as proof.

To redact a value, set `DeviceContextOptions.Redact`, a `Func<string, string, string?>` called with the key and the value of each collected field. Return a changed value to replace it, or `null` to drop the field. The draft's own `Metadata` does not pass through it.

## Limits

- The collected keys (up to 19) count toward the 50 metadata keys a ticket may carry, so an app can rely on 31 of its own.
- A key of the table above (or any key in `TicketMetadataKeys`) in the draft's `Metadata` is ignored: the collected value, or none, is sent.
- A blank value is skipped, a value is cut to 1000 characters, a key is at most 64 characters.
- A blank key or a key longer than 64 characters in the draft's `Metadata` also fails locally with `metadata-invalid` and nothing is sent.
- More than 50 keys, or more than 16,000 characters of serialized metadata, fails locally with the code `metadata-invalid` and nothing is sent.

## Compatibility

Targets plain `net10.0` and depends on `Microsoft.Maui.Essentials`, not on the MAUI workload. Consumers need MAUI >= 10.0.0. The package is AOT-compatible. There is no screenshot and no attachment until a later release.

More: https://github.com/Syntax-Circus/techstrap/blob/main/docs/development/CLIENT-SDK.md

Source and issues: https://github.com/Syntax-Circus/techstrap
