using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Configuration;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System;
using Akka.DependencyInjection;

namespace Petabridge.App
{
    /// <summary>
    /// <see cref="IHostedService"/> that runs and manages <see cref="ActorSystem"/> in background of application.
    /// </summary>
    public class AkkaService : IHostedService
    {
        private ActorSystem ClusterSystem;
        private readonly IServiceProvider _serviceProvider;

        public AkkaService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
             var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf")).BootstrapFromDocker();
             var bootstrap = BootstrapSetup.Create()
                .WithConfig(config) // load HOCON
                .WithActorRefProvider(ProviderSelection.Cluster.Instance); // launch Akka.Cluster

            // N.B. `WithActorRefProvider` isn't actually needed here - the HOCON file already specifies Akka.Cluster

            // enable DI support inside this ActorSystem, if needed
            var diSetup = DependencyResolverSetup.Create(_serviceProvider);

            // merge this setup (and any others) together into ActorSystemSetup
            var actorSystemSetup = bootstrap.And(diSetup);

            // start ActorSystem
            ClusterSystem = ActorSystem.Create("ClusterSys", actorSystemSetup);

            // start Petabridge.Cmd (https://cmd.petabridge.com/)
            var pbm = PetabridgeCmd.Get(ClusterSystem);
            pbm.RegisterCommandPalette(ClusterCommands.Instance);
            pbm.RegisterCommandPalette(new RemoteCommands());
            pbm.Start(); // begin listening for PBM management commands

            // instantiate actors

            // use the ServiceProvider ActorSystem Extension to start DI'd actors
            var sp = DependencyResolver.For(ClusterSystem);
            
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // strictly speaking this may not be necessary - terminating the ActorSystem would also work
            // but this call guarantees that the shutdown of the cluster is graceful regardless
             await CoordinatedShutdown.Get(ClusterSystem).Run(CoordinatedShutdown.ClrExitReason.Instance);
        }
    }
   
}
