// The local development stack. Never deployed: deployments use deploy/docker-compose.yml.
// The check lives here and not in LocalStackComposition, so that tests can inspect the model in a Release build.
#if !DEBUG
throw new InvalidOperationException("The local stack runs only in Debug builds.");
#else
using TechStrap.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));

builder.AddLocalStack(repositoryRoot);

builder.Build().Run();
#endif
