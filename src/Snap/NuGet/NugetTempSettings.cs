using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using NuGet.Configuration;

namespace Snap.NuGet
{
    internal sealed class NugetTempSettings : ISettings
    {
        readonly Settings _settings;

        public NugetTempSettings([NotNull] string tempDirectory) 
        {
            if (tempDirectory == null) throw new ArgumentNullException(nameof(tempDirectory));
            _settings = new Settings(tempDirectory);
        }

        public SettingSection GetSection(string sectionName)
        {
            return _settings.GetSection(sectionName);
        }

        public void AddOrUpdate(string sectionName, SettingItem item)
        {
            _settings.AddOrUpdate(sectionName, item);
        }

        public void Remove(string sectionName, SettingItem item)
        {
            _settings.Remove(sectionName, item);
        }

        public void SaveToDisk()
        {
            
        }

        public IList<string> GetConfigFilePaths()
        {
            return _settings.GetConfigFilePaths();
        }

        public IList<string> GetConfigRoots()
        {
            return _settings.GetConfigRoots();
        }

        public event EventHandler SettingsChanged;
    }
}
