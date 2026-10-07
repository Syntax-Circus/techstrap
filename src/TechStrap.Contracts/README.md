# TechStrap.Contracts

The wire contracts of TechStrap, the self-hosted support desk: request and response types, header names, route constants and size limits. It has no dependencies, so any .NET client can reference it.

## Stability

The types and constants here are the public API surface. A breaking change ships only in a new major version, and package validation checks every release against the previous one.

## Using it

Most applications want the client SDK, which wraps these contracts in a typed HTTP client. See TechStrap.Client.

Source and issues: https://github.com/Syntax-Circus/techstrap
