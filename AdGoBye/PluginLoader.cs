using AdGoBye.PluginInternal;
using AdGoBye.Plugins;
using Serilog;
using System.Reflection;

namespace AdGoBye;

public static class PluginLoader
{
    private static readonly ILogger Logger = Log.ForContext(typeof(PluginLoader));
    public static List<PluginEntry> LoadedPlugins = new();

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

    internal static void LoadPlugin(string pluginPath)
    {
        Assembly? plugin;

        if (string.IsNullOrEmpty(pluginPath)) return;
        try
        {
            plugin = Assembly.LoadFile(pluginPath);
            if (plugin == null)
            {
                throw new Exception("plugin was null");
            }
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
                pluginClass != null) break;
        }

        if (string.IsNullOrEmpty(pluginName) || string.IsNullOrEmpty(pluginVersion) ||
            pluginClass == null)
        {
            Logger.Error("Plugin {path} failed to load as it was missing a name, version or class.", pluginPath);
            return;
        }

        var pluginInstance = (IPlugin)Activator.CreateInstance(pluginClass)!;
        LoadedPlugins.Add(new PluginEntry(pluginName, pluginMaintainer, pluginVersion, pluginInstance));
    }
}