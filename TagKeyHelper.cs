using System;
using System.IO;

namespace HyIO
{
    internal static class TagKeyHelper
    {
        public static string GetImageKey(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            try
            {
                return Path.GetFullPath(filePath);
            }
            catch
            {
                return filePath;
            }
        }

        public static bool IsPathBasedKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            try
            {
                return Path.IsPathRooted(key);
            }
            catch
            {
                return false;
            }
        }
    }
}
