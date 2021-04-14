using Akka.Actor;
using Akka.Event;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Petabridge.App.Web
{
    public sealed class ConsoleActor : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        public ConsoleActor()
        {
            ReceiveAny(_ => _log.Info("Received: {0}", _));
        }
    }

    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
              // creates an instance of the ISignalRProcessor that can be handled by SignalR
            services.AddSingleton<IConsoleReporter, AkkaService>();

            // starts the IHostedService, which creates the ActorSystem and actors
            services.AddHostedService<AkkaService>(sp => (AkkaService)sp.GetRequiredService<IConsoleReporter>());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                var reporter = endpoints.ServiceProvider.GetRequiredService<IConsoleReporter>();

                endpoints.MapGet("/", async context =>
                {
                    reporter.Report($"hit from {context.TraceIdentifier}"); // calls Akka.NET under the covers
                    await context.Response.WriteAsync("Hello World!");
                });
            });
        }
    }
}
