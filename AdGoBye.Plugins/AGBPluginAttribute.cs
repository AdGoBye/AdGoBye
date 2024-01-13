namespace AdGoBye.Plugins;

public class AgbPluginAttribute(string name, string maintainer, string version, Type instance)
    : Attribute
{
    public Type Instance = instance;
    public string Maintainer = maintainer;
    public string Name = name;
    public string Version = version;
}