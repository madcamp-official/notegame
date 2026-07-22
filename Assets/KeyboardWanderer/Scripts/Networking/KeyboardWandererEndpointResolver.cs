using System;

namespace KeyboardWanderer.Networking
{
    /// <summary>Single precedence contract for every Unity-to-server transport.</summary>
    internal static class KeyboardWandererEndpointResolver
    {
        internal const string GameServerEnvironmentVariable = "KW_GAME_SERVER_URL";
        internal const string GmEndpointEnvironmentVariable = "KW_GM_ENDPOINT";

        internal static string ResolveBaseUrl(string explicitBaseUrl, string fallback)
        {
            string value = FirstNonBlank(explicitBaseUrl,
                Environment.GetEnvironmentVariable(GameServerEnvironmentVariable), fallback);
            return value.Trim().TrimEnd('/');
        }

        internal static string ResolveGmEndpoint(string explicitEndpoint, string fallback)
        {
            string value = FirstNonBlank(explicitEndpoint,
                Environment.GetEnvironmentVariable(GmEndpointEnvironmentVariable));
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim().TrimEnd('/');
            string serverBase = Environment.GetEnvironmentVariable(GameServerEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(serverBase))
                return serverBase.Trim().TrimEnd('/') + "/v1/gm/narrate";
            return fallback.Trim();
        }

        private static string FirstNonBlank(params string[] values)
        {
            if (values != null)
                for (int i = 0; i < values.Length; i++)
                    if (!string.IsNullOrWhiteSpace(values[i]))
                        return values[i];
            return string.Empty;
        }
    }
}
