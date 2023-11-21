using AdGoBye.Plugins;

namespace AdGoBye.PluginInternal;

public class PluginEntry
{
    public string Name;
    public string Maintainer;
    public string Version;
    public IPlugin Instance;

    public PluginEntry(string name, string maintainer, string version, IPlugin instance)
    {
        Name = name;
        Maintainer = maintainer;
        Version = version;
        Instance = instance;
    }
}