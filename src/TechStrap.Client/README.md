# TechStrap.Client

A small .NET client for the TechStrap support desk: submit a support ticket from your own app over the intake API, with an API key, retries and typed errors.

## Install

    dotnet add package TechStrap.Client

## Using it

Call `AddTechStrapClient` on your service collection with the API address and your key, either through a delegate or from a configuration section named `TechStrap`. The options are validated the first time they are read, so a bad address or key fails with a message that names the setting.

## Keys

A Public key is meant for apps you ship to customers. Never embed a Trusted key in an app: anyone can read it out of the package. Keep Trusted keys on servers you control.

Source and issues: https://github.com/Syntax-Circus/techstrap
