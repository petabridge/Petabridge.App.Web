using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Bootstrap.Docker;
using Akka.Configuration;
using Microsoft.Extensions.Hosting;
using Petabridge.Cmd.Cluster;
using Petabridge.Cmd.Host;
using Petabridge.Cmd.Remote;

namespace Petabridge.App.Web
{
    public class AkkaService : IHostedService
    {
        public static ActorSystem Sys { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var config = ConfigurationFactory.ParseString(File.ReadAllText("app.conf")).BootstrapFromDocker();
            Sys = ActorSystem.Create("ClusterSys", config);

            var pbm = PetabridgeCmd.Get(Sys);
            pbm.RegisterCommandPalette(ClusterCommands.Instance);
            pbm.RegisterCommandPalette(RemoteCommands.Instance);
            pbm.Start(); // begin listening for PBM management commands

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return CoordinatedShutdown.Get(Sys).Run(CoordinatedShutdown.ClrExitReason.Instance);
        }
    }
}
