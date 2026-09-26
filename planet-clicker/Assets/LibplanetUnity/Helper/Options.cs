using System;
using System.Collections.Generic;
using UnityEngine;

namespace LibplanetUnity.Helper
{
    /// <summary>
    /// Runtime options for the Agent.  Populated either from
    /// StreamingAssets/command_line_options.json (parsed with JsonUtility against the
    /// public fields below) or from the process command line (see
    /// <see cref="CommnadLineParser.GetCommandLineOptions"/>).
    /// </summary>
    [Serializable]
    public class Options
    {
        public bool logging;

        public bool noMiner;

        public string privateKey;

        public string host;

        public int port;

        public string[] peers = new string[] { };

        public string storagePath;

        public string appProtocolVersion;

        public string[] trustedAppProtocolVersionSigners = new string[] { };

        public bool Logging { get => logging; set => logging = value; }

        public bool NoMiner { get => noMiner; set => noMiner = value; }

        public string PrivateKey { get => privateKey; set => privateKey = value; }

        public string Host { get => host; set => host = value; }

        public int Port { get => port; set => port = value; }

        public string[] Peers { get => peers; set => peers = value ?? new string[] { }; }

        public string StoragePath { get => storagePath; set => storagePath = value; }

        public string AppProtocolVersion { get => appProtocolVersion; set => appProtocolVersion = value; }

        public string[] TrustedAppProtocolVersionSigners
        {
            get => trustedAppProtocolVersionSigners;
            set => trustedAppProtocolVersionSigners = value ?? new string[] { };
        }
    }

    /// <summary>
    /// Parses the command line into <see cref="Options"/>.  Previously this delegated to
    /// the CommandLineParser library, which throws while instantiating the options type
    /// in some environments; a small hand-rolled parser is used instead.  Unknown
    /// arguments (e.g. Unity's own -logFile / -batchmode) are ignored.
    /// </summary>
    public static class CommnadLineParser
    {
        public static Options GetCommandLineOptions()
        {
            string[] args = Environment.GetCommandLineArgs();

            var options = new Options();
            var peers = new List<string>();
            var signers = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string NextValue()
                {
                    return i + 1 < args.Length ? args[++i] : null;
                }

                try
                {
                    switch (arg)
                    {
                        case "--logging":
                            options.Logging = true;
                            break;
                        case "--no-miner":
                            options.NoMiner = true;
                            break;
                        case "--private-key":
                            options.PrivateKey = NextValue();
                            break;
                        case "--host":
                            options.Host = NextValue();
                            break;
                        case "--port":
                            if (int.TryParse(NextValue(), out int port))
                            {
                                options.Port = port;
                            }
                            break;
                        case "--peer":
                            string peer = NextValue();
                            if (!string.IsNullOrEmpty(peer))
                            {
                                peers.Add(peer);
                            }
                            break;
                        case "--storage-path":
                            options.StoragePath = NextValue();
                            break;
                        case "--app-protocol-version":
                            options.AppProtocolVersion = NextValue();
                            break;
                        case "--trusted-app-protocol-version-signer":
                            string signer = NextValue();
                            if (!string.IsNullOrEmpty(signer))
                            {
                                signers.Add(signer);
                            }
                            break;
                        default:
                            // Unknown/Unity-owned argument; skip it.  Values of unknown
                            // "--foo=bar"-style flags are ignored too.
                            break;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to parse command line argument \"{arg}\": {e.Message}");
                }
            }

            options.Peers = peers.ToArray();
            options.TrustedAppProtocolVersionSigners = signers.ToArray();
            return options;
        }
    }
}
