using AdGoBye.Plugins;

namespace AdGoBye.PluginInternal;

public class PluginEntry(string name, string maintainer, string version, IPlugin instance)
{
    public IPlugin Instance = instance;
    public string Maintainer = maintainer;
    public string Name = name;
    public string Version = version;
}