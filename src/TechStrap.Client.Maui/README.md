# TechStrap.Client.Maui

Device and app metadata capture and a submit-ticket helper for .NET MAUI apps, on top of TechStrap.Client. It reads the app version, platform, connectivity and (optionally) display and battery state through MAUI Essentials and attaches them to the ticket as metadata.

## Install

    dotnet add package TechStrap.Client.Maui

## Using it

Register it in `MauiProgram.cs` with the API address and your key:

    services.AddTechStrapMaui(client =>
    {
        client.BaseAddress = new Uri("https://support.example.com");
        client.ApiKey = "<your Public key>";
    });

## Keys

A key inside an app can be extracted by anyone who has the app. Use a Public key only, never a Trusted key. The metadata an app sends is flagged as untrusted on the server.

More: docs/development/CLIENT-SDK.md in the repository.

Source and issues: https://github.com/Syntax-Circus/techstrap