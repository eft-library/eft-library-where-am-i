using System.Reflection;

namespace where_am_i.Services
{
    public static class AppVersionInfo
    {
        public static string GetCurrentVersion(Assembly assembly) =>
            (assembly.GetName().Version ?? new Version(0, 0, 0, 0)).ToString(4);
    }
}
