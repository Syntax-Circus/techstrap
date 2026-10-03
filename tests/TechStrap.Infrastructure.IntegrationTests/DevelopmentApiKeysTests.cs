using TechStrap.Infrastructure.Seeding;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class DevelopmentApiKeysTests
{
    [Fact]
    public void The_smoke_script_default_key_is_the_seeded_orbitly_trusted_key()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull();
        var script = File.ReadAllText(Path.Combine(directory.FullName, "scripts", "Send-TestTicket.ps1"));

        script.ShouldContain(DevelopmentApiKeys.OrbitlyTrusted);
    }
}
