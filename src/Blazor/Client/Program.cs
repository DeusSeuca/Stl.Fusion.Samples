using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stl.Fusion;
using Stl.Fusion.Client;
using Stl.Fusion.UI;
using Stl.OS;
using Samples.Blazor.Client.Services;
using Samples.Blazor.Client.Models;
using Samples.Blazor.Common.Services;

namespace Samples.Blazor.Client
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            if (OSInfo.Kind != OSKind.WebAssembly)
                throw new ApplicationException("This app runs only in browser.");

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            ConfigureServices(builder.Services, builder);
            builder.RootComponents.Add<App>("app");
            var host = builder.Build();

            var runTask = host.RunAsync();
            Task.Run(async () => {
                var hostedServices = host.Services.GetService<IEnumerable<IHostedService>>();
                foreach (var hostedService in hostedServices)
                    await hostedService.StartAsync(default);
            });
            return runTask;
        }

        public static void ConfigureServices(IServiceCollection services, WebAssemblyHostBuilder builder)
        {
            ConfigureSharedServices(services);

            var baseUri = new Uri(builder.HostEnvironment.BaseAddress);
            services.AddFusionWebSocketClient((c, o) => {
                o.BaseUri = baseUri;
                // o.MessageLogLevel = LogLevel.Information;
            });

            // Replica services
            var apiBaseUri = new Uri($"{baseUri}api/");
            services.AddTransient(c => new HttpClient() { BaseAddress = apiBaseUri });
            services.AddReplicaService<ITimeClient>("time");
            services.AddReplicaService<IScreenshotClient>("screenshot");
            services.AddReplicaService<IChatClient>("chat");
            services.AddReplicaService<IComposerClient>("composer");
            // Client-side versions of server-side services
            services.AddSingleton<ITimeService, ClientTimeService>();
            services.AddSingleton<IScreenshotService, ClientScreenshotService>();
            services.AddSingleton<IChatService, ClientChatService>();
            services.AddSingleton<IComposerService, ClientComposerService>();
        }

        public static void ConfigureSharedServices(IServiceCollection services)
        {
            // Computed services
            services.AddComputedService<ILocalComposerService, LocalComposerService>();

            // Configuring live updaters
            services.AddSingleton(c => new UpdateDelayer.Options() {
                Delay = TimeSpan.FromSeconds(0.1),
            });
            services.AutoAddLiveState(typeof(Program).Assembly, (c, options) => {
                if (options is LiveState<ServerScreenState.Local, ServerScreenState>.Options) {
                    // Server Screen part always updates as quickly as it can
                    options.UpdateDelayer = new UpdateDelayer(new UpdateDelayer.Options() {
                        Delay = TimeSpan.FromSeconds(0),
                    });
                }
                if (options is LiveState<ServerTimeState>.Options) {
                    // Slower auto-updates for Server Time part
                    options.UpdateDelayer = new UpdateDelayer(new UpdateDelayer.Options() {
                        Delay = TimeSpan.FromSeconds(0.5),
                    });
                }
                if (options is LiveState<CompositionState.Local, CompositionState>.Options) {
                    // Slower auto-updates for Composition part
                    options.UpdateDelayer = new UpdateDelayer(new UpdateDelayer.Options() {
                        Delay = TimeSpan.FromSeconds(0.5),
                    });
                }
            });

        }
    }
}
