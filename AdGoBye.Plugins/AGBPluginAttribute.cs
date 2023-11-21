namespace AdGoBye.Plugins;

public class AgbPluginAttribute : Attribute
{
    public string Name;
    public string Maintainer;
    public string Version;
    public Type Instance;

    public AgbPluginAttribute(string name, string maintainer, string version, Type instance)
    {
        Name = name;
        Maintainer = maintainer;
        Version = version;
        Instance = instance;
    }
}