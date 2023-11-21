namespace AdGoBye.Plugins
{
    public interface IPlugin
    {
        /// <returns>The <see cref="EPluginType">EPluginType</see> of this plugin.</returns>
        EPluginType PluginType();

        /// <returns>
        /// An array of Content IDs that a given plugin is responsible for.
        ///
        /// This is ignored if <see cref="PluginType"/> is <see cref="EPluginType.Global"/>.
        /// </returns>
        string[]? ResponsibleForContentIds();

        /// <summary>
        /// A plugin can choose to override the default and user-specific blocklists
        /// (in case the logic should be directly handled by the plugin).
        ///
        /// A user can override this on a per-plugin-per-world level.
        /// </summary>
        /// <param name="contentId">The id of the content that is to be evaluated</param>
        /// <returns>Whether the plugin will override the blocklist.</returns>
        bool OverrideBlocklist(string contentId);

        /// <summary>
        /// Patch is the main entrypoint to a plugin's operations. Plugins are expected to carry out their respective
        /// behaviours in this method.
        /// </summary>
        /// <param name="contentId">A string representing the content's blueprint identifier</param>
        /// <param name="dataDirectoryPath">The path of the cache directory that contains the __data file</param>
        /// <returns>A <see cref="EPatchResult"/> that signifies the result of the plugin's patch operation</returns>
        EPatchResult Patch(string contentId, string dataDirectoryPath);
        
        /// <param name="contentId">A string representing the content's blueprint identifier</param>
        /// <param name="dataDirectoryPath">The path of the cache directory that contains the __data file</param>
        /// <returns>A <see cref="EVerifyResult"/> that signifies the result of the plugin's patch operation</returns>
        EVerifyResult Verify(string contentId, string dataDirectoryPath);
    }
}