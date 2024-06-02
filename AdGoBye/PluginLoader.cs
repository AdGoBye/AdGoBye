using System.Reflection;
using AdGoBye.PluginInternal;
using AdGoBye.Plugins;
using Serilog;

namespace AdGoBye;

public static class PluginLoader
{
    private static readonly ILogger Logger = Log.ForContext(typeof(PluginLoader));
    public static readonly List<PluginEntry> LoadedPlugins = [];

    public static void LoadPlugins()
    {
        var pluginLoadPath = GetRelativeDirectoryPath("Plugins");
        Directory.CreateDirectory(pluginLoadPath);
        var pluginsToLoad = Directory.GetFiles(pluginLoadPath, "*.dll");

        foreach (var pluginPath in pluginsToLoad)
        {
            LoadPlugin(Path.GetFullPath(pluginPath));
        }

        return;

        static string GetRelativeDirectoryPath(string endingDirectory)
        {
            var relPath = $".{Path.DirectorySeparatorChar}{endingDirectory}";
            if (string.IsNullOrEmpty(Environment.ProcessPath)) return relPath;

            var processPath = Environment.ProcessPath;
            var lastDirIndex = processPath.LastIndexOf(Path.DirectorySeparatorChar);
            var lastDir = processPath[..lastDirIndex];
            relPath = Path.Join(lastDir, endingDirectory);

            return relPath;
        }
    }

    private static void LoadPlugin(string pluginPath)
    {
        Assembly? plugin;

        if (string.IsNullOrEmpty(pluginPath)) return;
        try
        {
            plugin = Assembly.LoadFile(pluginPath);
            if (plugin is null) throw new Exception("plugin was null");
        }
        catch (Exception e)
        {
            Logger.Error("Plugin {path} failed to load with error: {error}", pluginPath, e);
            return;
        }

        var pluginName = "";
        var pluginMaintainer = "";
        var pluginVersion = "";
        Type pluginClass = null!;

        using var pluginAttrs = plugin.CustomAttributes.GetEnumerator();
        while (pluginAttrs.MoveNext())
        {
            var attr = pluginAttrs.Current;
            switch (attr.AttributeType.Name)
            {
                case nameof(AgbPluginAttribute):
                    pluginName = (string)attr.ConstructorArguments[0].Value!;
                    pluginMaintainer = (string)attr.ConstructorArguments[1].Value!;
                    pluginVersion = (string)attr.ConstructorArguments[2].Value!;
                    pluginClass = (Type)attr.ConstructorArguments[3].Value!;
                    break;
            }

            if (!string.IsNullOrEmpty(pluginName) && !string.IsNullOrEmpty(pluginVersion) &&
                pluginClass is not null) break;
        }

        if (string.IsNullOrEmpty(pluginName) || string.IsNullOrEmpty(pluginVersion) ||
            pluginClass is null)
        {
            Logger.Error("Plugin {path} failed to load as it was missing a name, version or class.", pluginPath);
            return;
        }

#if !DEBUG
        if (!Settings.Options.DisablePluginInstallWarning)
        {
            var allowlist = LoadPluginAllowlist();
            if (!allowlist.Contains($"{pluginName} ({pluginMaintainer}, {pluginVersion})"))
            {
                Logger.Information("""
                                   You are trying to run {Name} ({maintainer}, {version})
                                   Plugins can run arbitrary code therefore can do everything on your system that you can, this includes installing malware or stealing your accounts.

                                   Be very suspicious when someone doesn't let you view the source code of a plugin, be watchful about where the file you're installing comes from.
                                   The AdGoBye Team is not responsible for what Plugins do.

                                   Input 'y' to allow this plugin or input anything else to skip this plugin.
                                   """, pluginName, pluginMaintainer, pluginVersion);
                var input = Console.ReadLine();

                if (input is null || !input.Equals("y", StringComparison.OrdinalIgnoreCase)) return;

                allowlist.Add($"{pluginName} ({pluginMaintainer}, {pluginVersion})");
                SavePluginAllowList(allowlist);
            }
        }

        static List<string> LoadPluginAllowlist()
        {
            return !File.Exists("pluginallowlist") ? [] : File.ReadAllLines("pluginallowlist").ToList();
        }

        static void SavePluginAllowList(IEnumerable<string> allowlist)
        {
            File.WriteAllLines("pluginallowlist", allowlist);
        }
#endif

        var pluginInstance = (IPlugin)Activator.CreateInstance(pluginClass)!;
        LoadedPlugins.Add(new PluginEntry(pluginName, pluginMaintainer, pluginVersion, pluginInstance));
    }
}