using System;
using System.Threading.Tasks;
using Splat;

namespace Snap.Extensions
{
    internal static class LoggerExtensions
    {
        public static void LogIfThrows(this IFullLogger This, LogLevel level, string message, Action block)
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

        public static async Task LogIfThrows(this IFullLogger This, LogLevel level, string message, Func<Task> block)
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

        public static async Task<T> LogIfThrows<T>(this IFullLogger This, LogLevel level, string message, Func<Task<T>> block)
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

        public static void WarnIfThrows(this IEnableLogger This, Action block, string message = null)
        {
            This.Log().LogIfThrows(LogLevel.Warn, message, block);
        }

        public static Task WarnIfThrows(this IEnableLogger This, Func<Task> block, string message = null)
        {
            return This.Log().LogIfThrows(LogLevel.Warn, message, block);
        }

        public static Task<T> WarnIfThrows<T>(this IEnableLogger This, Func<Task<T>> block, string message = null)
        {
            return This.Log().LogIfThrows(LogLevel.Warn, message, block);
        }

        public static void ErrorIfThrows(this IEnableLogger This, Action block, string message = null)
        {
            This.Log().LogIfThrows(LogLevel.Error, message, block);
        }

        public static Task ErrorIfThrows(this IEnableLogger This, Func<Task> block, string message = null)
        {
            return This.Log().LogIfThrows(LogLevel.Error, message, block);
        }

        public static Task<T> ErrorIfThrows<T>(this IEnableLogger This, Func<Task<T>> block, string message = null)
        {
            return This.Log().LogIfThrows(LogLevel.Error, message, block);
        }    
    }
}
