namespace TechStrap.Architecture.Tests.RecordDeclarationFixtures.Records
{
    /// <summary>The one good record: internal sealed, named *Record, in the records namespace.</summary>
    internal sealed class FooRecord
    {
        public int Id { get; set; }
    }

    /// <summary>(a) In the records namespace but not named *Record.</summary>
    internal sealed class Widget
    {
        public int Id { get; set; }
    }

    /// <summary>(b) A public *Record.</summary>
    public sealed class PublicRecord
    {
        public int Id { get; set; }
    }

    /// <summary>(c) A *Record that is not sealed.</summary>
    internal class UnsealedRecord
    {
        public int Id { get; set; }
    }
}

namespace TechStrap.Architecture.Tests.RecordDeclarationFixtures.Elsewhere
{
    /// <summary>(d) Named *Record but outside the records namespace.</summary>
    internal sealed class StrayRecord
    {
        public int Id { get; set; }
    }
}
