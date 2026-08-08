using Telumera.ServiceDefaults;
using WorkerServiceTemplate;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
builder.Services.AddHostedService<Worker>();

// Graceful shutdown: give in-flight work a chance to finish before the host tears the process down.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

var host = builder.Build();
host.Run();
