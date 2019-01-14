using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Snap.Logging;

namespace Snap.Extensions
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal static class LoggerExtensions
    {
        public static void LogIfThrows(this ILog This, LogLevel level, string message, Action block)
        {
            try
            {
                block();
            }
            catch (Exception ex)
            {
                switch (level)
                {
                    case LogLevel.Debug:
                        This.DebugException(message ?? "", ex);
                        break;
                    case LogLevel.Info:
                        This.InfoException(message ?? "", ex);
                        break;
                    case LogLevel.Warn:
                        This.WarnException(message ?? "", ex);
                        break;
                    case LogLevel.Error:
                        This.ErrorException(message ?? "", ex);
                        break;
                }

                throw;
            }
        }

        public static async Task LogIfThrows(this ILog This, LogLevel level, string message, Func<Task> block)
        {
            try
            {
                await block();
            }
            catch (Exception ex)
            {
                switch (level)
                {
                    case LogLevel.Debug:
                        This.DebugException(message ?? "", ex);
                        break;
                    case LogLevel.Info:
                        This.InfoException(message ?? "", ex);
                        break;
                    case LogLevel.Warn:
                        This.WarnException(message ?? "", ex);
                        break;
                    case LogLevel.Error:
                        This.ErrorException(message ?? "", ex);
                        break;
                }
                throw;
            }
        }

        public static async Task<T> LogIfThrows<T>(this ILog This, LogLevel level, string message, Func<Task<T>> block)
        {
            try
            {
                return await block();
            }
            catch (Exception ex)
            {
                switch (level)
                {
                    case LogLevel.Debug:
                        This.DebugException(message ?? "", ex);
                        break;
                    case LogLevel.Info:
                        This.InfoException(message ?? "", ex);
                        break;
                    case LogLevel.Warn:
                        This.WarnException(message ?? "", ex);
                        break;
                    case LogLevel.Error:
                        This.ErrorException(message ?? "", ex);
                        break;
                }
                throw;
            }
        }

        public static void WarnIfThrows(this ILog This, Action block, string message = null)
        {
            This.LogIfThrows(LogLevel.Warn, message, block);
        }

        public static Task WarnIfThrows(this ILog This, Func<Task> block, string message = null)
        {
            return This.LogIfThrows(LogLevel.Warn, message, block);
        }

        public static Task<T> WarnIfThrows<T>(this ILog This, Func<Task<T>> block, string message = null)
        {
            return This.LogIfThrows(LogLevel.Warn, message, block);
        }

        public static void ErrorIfThrows(this ILog This, Action block, string message = null)
        {
            This.LogIfThrows(LogLevel.Error, message, block);
        }

        public static Task ErrorIfThrows(this ILog This, Func<Task> block, string message = null)
        {
            return This.LogIfThrows(LogLevel.Error, message, block);
        }

        public static Task<T> ErrorIfThrows<T>(this ILog This, Func<Task<T>> block, string message = null)
        {
            return This.LogIfThrows(LogLevel.Error, message, block);
        }    
    }
}
