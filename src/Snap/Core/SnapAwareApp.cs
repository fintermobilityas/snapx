using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core.Models;
using Snap.Core.UnitTest;
using Snap.Extensions;
using Snap.Logging;

namespace Snap.Core
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public static class SnapAwareApp
    {
        static readonly ILog Logger = LogProvider.GetLogger(nameof(SnapAwareApp));
        static readonly object SyncRoot = new object();
        
        static ISnapOs SnapOs { get; }

        static SnapAwareApp()
        {
            lock (SyncRoot)
            {
                if (SnapOs != null)
                {
                    return;
                }
            }

            if (Current != null)
            {
                if (ModeDetector.InUnitTestRunner())
                {
                    return;
                }
                
                throw new Exception($"Expected {nameof(Current)} to equal null.");
            }
                        
            SnapOs = AnyOS.SnapOs.AnyOs;

            try
            {
                WorkingDirectory = SnapOs.Filesystem.PathGetDirectoryName(typeof(SnapAwareApp).Assembly.Location);
                Current = WorkingDirectory.GetSnapAppFromDirectory(SnapOs.Filesystem, new SnapAppReader());
            }
            catch (Exception e)
            {
                Logger.ErrorException("Failed to load snap manifest.", e);
            }
        }
        
        /// <summary>
        /// Current application release information.
        /// </summary>
        public static SnapApp Current { get; internal set; }
        
        /// <summary>
        /// Current application working directory.
        /// </summary>
        public static string WorkingDirectory { get; }

        /// <summary>
        /// Call this method as early as possible in app startup. This method
        /// will dispatch to your methods to set up your app. Depending on the
        /// parameter, your app will exit after this method is called, which 
        /// is required by Snap. SnapUpdateManager has methods to help you to
        /// do this, such as CreateShortcutForThisExe.
        /// </summary>
        /// <param name="onInitialInstall">Called when your app is initially
        /// installed. Set up app shortcuts here as well as file associations.
        /// </param>
        /// <param name="onAppUpdate">Called when your app is updated to a new
        /// version.</param>
        /// <param name="onAppObsoleted">Called when your app is no longer the
        /// latest version (i.e. they have installed a new version and your app
        /// is now the old version)</param>
        /// <param name="onAppUninstall">Called when your app is uninstalled 
        /// via Programs and Features. Remove all of the things that you created
        /// in onInitialInstall.</param>
        /// <param name="onFirstRun">Called the first time an app is run after
        /// being installed. Your application will **not** exit after this is
        /// dispatched, you should use this as a hint (i.e. show a 'Welcome' 
        /// screen, etc etc.</param>
        /// <param name="arguments">Use in a unit-test runner to mock the 
        /// arguments. In your app, leave this as null.</param>
        public static void HandleEvents(
            Action<SemanticVersion> onInitialInstall = null,
            Action<SemanticVersion> onAppUpdate = null,
            Action<SemanticVersion> onAppObsoleted = null,
            Action<SemanticVersion> onAppUninstall = null,
            Action onFirstRun = null,
            string[] arguments = null)
        {
            void DefaultBlock(SemanticVersion v)
            {
            }

            var args = arguments ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Length == 0) return;

            var lookup = new[] {
                new { Key = "--snap-install", Value = onInitialInstall ??  DefaultBlock },
                new { Key = "--snap-updated", Value = onAppUpdate ??  DefaultBlock },
                new { Key = "--snap-obsolete", Value = onAppObsoleted ??  DefaultBlock },
                new { Key = "--snap-uninstall", Value = onAppUninstall ??  DefaultBlock }
            }.ToDictionary(k => k.Key, v => v.Value);

            if (args[0] == "--snap-firstrun")
            {
                (onFirstRun ?? (() => { }))();
                return;
            }

            if (args.Length != 2)
            {
                return;
            }

            if (!lookup.ContainsKey(args[0]))
            {
                return;
            }

            try
            {
                lookup[args[0]](Current.Version);
                #if !SNAP_NUPKG
                if (ModeDetector.InUnitTestRunner())
                {
                    return;
                }
                #else
                Environment.Exit(0);
                #endif
            }
            catch (Exception ex)
            {
                Logger.ErrorException($"Exception thrown while handling snap arguments. Arguments: {args}", ex);
                #if !SNAP_NUPKG
                if (ModeDetector.InUnitTestRunner())
                {
                    return;
                }
                #else
                Environment.Exit(-1);
                #endif
            }
        }
    }
}
