using AdGoBye;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Templates;
using Serilog.Templates.Themes;

var levelSwitch = new LoggingLevelSwitch
{
    MinimumLevel = (LogEventLevel)Settings.Options.LogLevel
};

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.ControlledBy(levelSwitch)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(new ExpressionTemplate(
        "[{@t:HH:mm:ss} {@l:u3} {Coalesce(SourceContext,'<none>')}] {@m}\n{@x}",
        theme: TemplateTheme.Literate))
    .CreateLogger();

var builder = Host.CreateDefaultBuilder(args)
    
    .ConfigureServices(collection =>
    {
        collection.AddDbContext<IndexContext>();
        collection.AddSerilog();
        collection.AddSingleton<Indexer>();
        collection.AddSingleton<SharedStateService>();
    });


using var host = builder.Build();
var awa = host.Services.GetRequiredService<Indexer>();
awa.ManageIndex();

host.Run();