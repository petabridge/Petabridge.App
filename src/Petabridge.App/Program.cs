using System.Diagnostics;
using Akka.Actor;
using Akka.Cluster.Hosting;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Cluster.Sharding;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;

var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

var host = new HostBuilder()
    .ConfigureHostConfiguration(builder =>
        builder.AddEnvironmentVariables()
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environment}.json"))
    .ConfigureServices((hostContext, services) =>
    {
        var akkaConfig = hostContext.Configuration.GetRequiredSection(nameof(AkkaClusterConfig))
            .Get<AkkaClusterConfig>();
        services.AddLogging();
        services.AddAkka(akkaConfig.ActorSystemName, (builder, provider) =>
        {
            Debug.Assert(akkaConfig.Port != null, "akkaConfig.Port != null");
            builder.AddHoconFile("app.conf")
                .WithRemoting(akkaConfig.Hostname, akkaConfig.Port.Value)
                .WithClustering(new ClusterOptions()
                {
                    Roles = akkaConfig.Roles?.ToArray() ?? Array.Empty<string>(),
                    SeedNodes = akkaConfig.SeedNodes?.Select(Address.Parse).ToArray() ?? Array.Empty<Address>()
                })
                .AddPetabridgeCmd(cmd =>
                {
                    cmd.RegisterCommandPalette(new RemoteCommands());
                    cmd.RegisterCommandPalette(ClusterCommands.Instance);

                    // sharding commands, although the app isn't configured to host any by default
                    cmd.RegisterCommandPalette(ClusterShardingCommands.Instance);
                });
        });
    })
    .ConfigureLogging((hostContext, configLogging) => { configLogging.AddConsole(); })
    .UseConsoleLifetime()
    .Build();

await host.RunAsync();