using System;

namespace Game.Client.Compatibility
{
    public static class ServerContractCompatibility
    {
        public static int ResolveProgressLevel(int progressLevel, int adminLevel)
        {
            return progressLevel != 0 || adminLevel == 0 ? progressLevel : adminLevel;
        }

        public static string[] ResolveProgressTokens(string[] progressTokens, string[] accessTokens)
        {
            return progressTokens != null ? progressTokens : accessTokens ?? Array.Empty<string>();
        }

        public static T ResolveReference<T>(T canonical, T legacy) where T : class
        {
            return canonical ?? legacy;
        }

        public static bool ResolveFlag(bool canonical, bool legacy)
        {
            return canonical || legacy;
        }
    }
}
