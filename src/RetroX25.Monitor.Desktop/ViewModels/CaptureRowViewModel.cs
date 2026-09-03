using System.Collections.ObjectModel;
using RetroIO.Protocols.X25.Monitor;

namespace RetroX25.Monitor.Desktop.ViewModels
{
    /// <summary>
    /// A single decoded field row shown under a layer in the detail tree.
    /// </summary>
    public sealed class FieldRowViewModel
    {
        /// <summary>
        /// The field name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The field value.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Byte offset of this field within the captured frame, or -1 when it has no byte range.
        /// </summary>
        public int Offset { get; }

        /// <summary>
        /// Byte length of this field within the captured frame.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// The combined text shown in the detail tree: "name = value" plus the byte range when known.
        /// </summary>
        public string Display => Offset >= 0
            ? $"{Name} = {Value}   [@{Offset} +{Length}]"
            : $"{Name} = {Value}";

        /// <summary>
        /// Creates a field row.
        /// </summary>
        /// <param name="name">
        /// Field name.
        /// </param>
        /// <param name="value">
        /// Field value.
        /// </param>
        /// <param name="offset">
        /// Byte offset into the frame, or -1 when not applicable.
        /// </param>
        /// <param name="length">
        /// Byte length within the frame.
        /// </param>
        public FieldRowViewModel(string name, string value, int offset, int length)
        {
            Name = name;
            Value = value;
            Offset = offset;
            Length = length;
        }
    }

    /// <summary>
    /// A protocol layer node in the detail tree, with its decoded fields as children.
    /// </summary>
    public sealed class LayerNodeViewModel
    {
        /// <summary>
        /// The layer display label (name and summary).
        /// </summary>
        public string Header { get; }

        /// <summary>
        /// The decoded fields of the layer.
        /// </summary>
        public ObservableCollection<FieldRowViewModel> Fields { get; } = new ObservableCollection<FieldRowViewModel>();

        /// <summary>
        /// Creates a layer node from a decoded layer.
        /// </summary>
        /// <param name="layer">
        /// The decoded layer.
        /// </param>
        public LayerNodeViewModel(MonitorLayer layer)
        {
            Header = string.IsNullOrEmpty(layer.Summary) ? layer.Name : $"{layer.Name}  —  {layer.Summary}";
            for (int i = 0; i < layer.Fields.Count; i++)
            {
                MonitorField field = layer.Fields[i];
                Fields.Add(new FieldRowViewModel(field.Name, field.Value, field.Offset, field.Length));
            }
        }
    }

    /// <summary>
    /// One row in the packet-list pane, wrapping a decoded <see cref="CaptureRecord"/>.
    /// </summary>
    public sealed class CaptureRowViewModel
    {
        /// <summary>
        /// The underlying decoded record, used to build the detail panes on selection.
        /// </summary>
        public CaptureRecord Record { get; }

        /// <summary>
        /// The server sequence number.
        /// </summary>
        public long Seq => Record.Seq;

        /// <summary>
        /// The observing port.
        /// </summary>
        public int Port => Record.Port;

        /// <summary>
        /// The travel direction as text.
        /// </summary>
        public string Direction => Record.Direction.ToString();

        /// <summary>
        /// The X.25 packet type.
        /// </summary>
        public string PacketType => Record.PacketType;

        /// <summary>
        /// The called address, if any.
        /// </summary>
        public string Called => Record.Called;

        /// <summary>
        /// The calling address, if any.
        /// </summary>
        public string Calling => Record.Calling;

        /// <summary>
        /// The captured length in bytes.
        /// </summary>
        public int Length => Record.TotalLength;

        /// <summary>
        /// The summary of the outermost layer, for an at-a-glance description.
        /// </summary>
        public string Summary => Record.Layers.Count > 0 ? Record.Layers[0].Summary : Record.PacketType;

        /// <summary>
        /// Creates a packet-list row.
        /// </summary>
        /// <param name="record">
        /// The decoded record.
        /// </param>
        public CaptureRowViewModel(CaptureRecord record)
        {
            Record = record;
        }
    }
}
