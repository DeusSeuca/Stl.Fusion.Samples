﻿using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Stl;
using Stl.Fusion;

namespace Samples.HelloWorld
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection()
                .AddFusionCore()
                .AddComputedService<UserService>()
                .AddComputedService<GreetingService>()
                .BuildServiceProvider();

            var users = services.GetRequiredService<UserService>();
            var greetings = services.GetRequiredService<GreetingService>();
            var userId = 1;
            var user = new User(userId, "(name isn't set yet)");
            await users.AddOrUpdateUserAsync(user);

            Task.Run(async () => {
                var computed = await Computed.CaptureAsync(_ => greetings.GreetUserAsync(userId));
                while (true) {
                    Console.WriteLine($"Background task: {computed.Value}");
                    await computed.InvalidatedAsync();
                    // Computed instances are immutable, so we need
                    // to get a new one to observe the updated value.
                    computed = await computed.UpdateAsync(false);
                }
            }).Ignore();

            // Notice the code below doesn't even know there are some IComputed, etc.
            while (true) {
                Console.WriteLine("What's your name?");
                var name = Console.ReadLine();
                await users.AddOrUpdateUserAsync(new User(userId, name));
                var greeting = await greetings.GreetUserAsync(userId);
                Console.WriteLine(greeting);
            }
        }
    }
}
