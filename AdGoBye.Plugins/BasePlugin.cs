namespace AdGoBye.Plugins
{
    public class BasePlugin : IPlugin
    {
        public virtual EPluginType PluginType()
        {
            return EPluginType.Global;
        }

        public virtual string[]? ResponsibleForContentIds()
        {
            if (PluginType() == EPluginType.Global)
            {
                return null;
            }

            throw new NotImplementedException();
        }

        public virtual bool OverrideBlocklist(string contentId)
        {
            return false;
        }

        public virtual EPatchResult Patch(string contentId, string dataDirectoryPath)
        {
            throw new NotImplementedException();
        }

        public virtual EVerifyResult Verify(string contentId, string dataDirectoryPath)
        {
            return EVerifyResult.Success;
        }
    }
}