namespace AdGoBye.Plugins;

public class AgbPluginAttribute : Attribute
{
    public Type Instance;
    public string Maintainer;
    public string Name;
    public string Version;

    public AgbPluginAttribute(string name, string maintainer, string version, Type instance)
    {
        Name = name;
        Maintainer = maintainer;
        Version = version;
        Instance = instance;
    }
}