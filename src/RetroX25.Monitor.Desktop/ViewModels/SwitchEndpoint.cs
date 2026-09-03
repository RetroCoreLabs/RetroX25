namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// A named switch/tap endpoint a tool window can connect to. Shared across the network map and every
    /// tool window so they all connect to the same set of endpoints.
    /// </summary>
    public sealed class SwitchEndpoint
    {
        /// <summary>
        /// A friendly name for the endpoint.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The host or IP address of the monitoring server.
        /// </summary>
        public string Host { get; }

        /// <summary>
        /// The monitoring stream TCP port.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// The label shown in a selector.
        /// </summary>
        public string Display => $"{Name} ({Host}:{Port})";

        /// <summary>
        /// Creates a switch endpoint.
        /// </summary>
        /// <param name="name">
        /// Friendly name.
        /// </param>
        /// <param name="host">
        /// Host or IP.
        /// </param>
        /// <param name="port">
        /// TCP port.
        /// </param>
        public SwitchEndpoint(string name, string host, int port)
        {
            Name = name;
            Host = host;
            Port = port;
        }
    }
}
