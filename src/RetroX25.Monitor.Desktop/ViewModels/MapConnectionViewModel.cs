using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// One trunk link between two discovered nodes, rendered as a NodifyM connection. It extends the base
    /// connection with the two node keys and a state-coloured stroke. Endpoints follow the two nodes'
    /// connector anchors automatically, so no manual endpoint maths are needed.
    /// </summary>
    public sealed partial class MapConnectionViewModel : ConnectionViewModelBase
    {
        /// <summary>
        /// The key of the node this edge starts at.
        /// </summary>
        public string FromKey { get; }

        /// <summary>
        /// The key of the node this edge ends at.
        /// </summary>
        public string ToKey { get; }

        /// <summary>
        /// True when the trunk is currently linked.
        /// </summary>
        public bool Up { get; set; }

        [ObservableProperty]
        private IBrush _stroke = Brushes.Gray;

        /// <summary>
        /// A friendly label for the trunk (e.g. "SW Oslo ↔ SW Bergen"), shown in the routing dialog title.
        /// </summary>
        [ObservableProperty]
        private string _trunkLabel = string.Empty;

        /// <summary>
        /// The routing each end of the trunk forwards across it, shown from the trunk's right-click menu.
        /// </summary>
        [ObservableProperty]
        private string _routingText = string.Empty;

        /// <summary>
        /// Creates an edge between two nodes' connectors.
        /// </summary>
        /// <param name="editor">
        /// The owning editor view-model.
        /// </param>
        /// <param name="from">
        /// The starting node.
        /// </param>
        /// <param name="to">
        /// The ending node.
        /// </param>
        public MapConnectionViewModel(NodifyEditorViewModelBase editor, MapNodeViewModel from, MapNodeViewModel to)
            : base(editor, from.Connector, to.Connector)
        {
            FromKey = from.Key;
            ToKey = to.Key;
        }
    }
}
