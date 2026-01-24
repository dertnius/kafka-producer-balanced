var builder = DistributedApplication.CreateBuilder(args);

// Get the solution directory (parent of AppHost directory)
var solutionDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, ".."));

// Add the main app
var app = builder.AddProject<Projects.MyDotNetApp>("mydotnetapp")
	.WithExternalHttpEndpoints();

// Add test executables - run tests with visible output
builder.AddExecutable(
	"tests",
	"pwsh",
	solutionDir,
	new[] { "-NoLogo", "-NoProfile", "-Command", "cd Tests\\MyDotNetApp.Tests; dotnet test --logger \"console;verbosity=detailed\"" }
);

builder.Build().Run();
