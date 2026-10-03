namespace TechStrap.Domain;

/// <summary>Primary keys are version 7 GUIDs (time-ordered), generated from the injected clock, never from the system clock.</summary>
public static class EntityId
{
    public static Guid New(TimeProvider clock) => Guid.CreateVersion7(clock.GetUtcNow());
}
