using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;
using NuGet.Versioning;
using Snap.AnyOS;
using Snap.Core.Models;
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
        
        internal static ISnapOs SnapOs { get; set; }

        static SnapAwareApp()
        {
            lock (SyncRoot)
            {
                if (SnapOs != null)
                {
                    return;
                }                               
            }

            try
            {     
                SnapOs = AnyOS.SnapOs.AnyOs;
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
        /// is required by Snap. 
        /// </summary>
        /// <param name="onFirstRun">Called the first time an app is run after
        /// being installed. Your application will **not** exit after this is
        /// dispatched, you should use this as a hint (i.e. show a 'Welcome'
        /// </param>
        /// <param name="onInstalled">Called when your app is initially
        /// installed. Your application will exit afterwards.
        /// </param>
        /// <param name="onUpdated">Called when your app is updated to a new
        /// version. Your application will exit afterwards.</param>
        /// <param name="arguments">Use in a unit-test runner to mock the 
        /// arguments. In your app, leave this as null.</param>
        /// <returns>If this methods returns TRUE then you should exit your program immediately.</returns>
        public static bool ProcessEvents([NotNull] string[] arguments,
            Action<SemanticVersion> onFirstRun = null,
            Action<SemanticVersion> onInstalled = null,
            Action<SemanticVersion> onUpdated = null)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            var args = arguments.Skip(1).ToArray();
            if (args.Length != 2)
            {
                return false;
            }

            var invoke = new[] {
                new { Key = "--snap-first-run", Value = onFirstRun ??  DefaultAction },
                new { Key = "--snap-installed", Value = onInstalled ??  DefaultAction },
                new { Key = "--snap-updated", Value = onUpdated ??  DefaultAction }
            }.ToDictionary(k => k.Key, v => v.Value);

            var actionName = args[0];
            if (!invoke.ContainsKey(actionName))
            {
                return false;
            }

            var doNotExitActions = new[]
            {
                "--snap-first-run"
            };
            
            try
            {
                Logger.Trace($"Handling event: {actionName}.");

                var currentVersion = SemanticVersion.Parse(args[1]);

                invoke[actionName](currentVersion);

                Logger.Trace($"Handled event: {actionName}.");

                if (doNotExitActions.Any(x => string.Equals(x, actionName)))
                {
                    return false;
                }

                SnapOs.Exit();

                return true;
            }
            catch (Exception ex)
            {
                Logger.ErrorException($"Exception thrown while handling snap event. Action: {actionName}", ex);
                
                SnapOs.Exit(-1);

                return true;
            }
        }

        static void DefaultAction(SemanticVersion version)
        {
        }
    }
}
