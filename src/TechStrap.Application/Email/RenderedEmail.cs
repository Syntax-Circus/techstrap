namespace TechStrap.Application.Email;

public sealed record RenderedEmail(string Subject, string Text, string Html, string? From, string? ReplyTo);
