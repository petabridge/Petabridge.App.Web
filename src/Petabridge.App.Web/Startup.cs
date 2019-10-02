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
            services.AddHostedService<AkkaService>();
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
                var actorRef = AkkaService.Sys.ActorOf(Props.Create(() => new ConsoleActor()), "console");

                endpoints.MapGet("/", async context =>
                {
                    actorRef.Tell($"hit from {context.TraceIdentifier}");
                    await context.Response.WriteAsync("Hello World!");
                });
            });
        }
    }
}
