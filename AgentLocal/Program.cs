using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AgentLocal.SMTP;
using AgentLocal.OPENAI;
using AgentLocal.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentLocal;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var services = new ServiceCollection()
            // Configuration existante
            .Configure<CamundaConfig>(configuration.GetSection("CamundaConfig"))
            .Configure<MailConfig>(configuration.GetSection("MailConfig"))
            .Configure<OpenAIConfig>(configuration.GetSection("OpenAIConfig"))

            // Ajout du DbContext
            .AddDbContext<PrototypeDbContext>(options =>
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection")))

            // Services existants
            .AddTransient<MailService>()
            .AddTransient<Camunda>()
            .BuildServiceProvider();

        // Création automatique de la base de données au démarrage
        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PrototypeDbContext>();
            dbContext.Database.EnsureCreated(); // Crée la base de données et les tables si elles n'existent pas
        }

        var camunda = services.GetRequiredService<Camunda>();
        await camunda.Start();
    }
}