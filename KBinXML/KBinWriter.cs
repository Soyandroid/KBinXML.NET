using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace KBinXML {

	public class KBinWriter {
		private readonly bool _compressed;
		private readonly Encoding _encoding;
		private readonly ByteBuffer _nodeBuffer;
		private readonly ByteBuffer _dataBuffer;
		private readonly ByteBuffer _dataWordBuffer;
		private readonly ByteBuffer _dataByteBuffer;
		internal static Encoding[] Encodings => KBinReader.Encodings;
		internal static Dictionary<int, Format> Formats = KBinReader.Formats;
		public byte[] Document;
		
		public KBinWriter(XDocument document, Encoding encoding = default, bool compressed = true) {
			_compressed = compressed;
			_encoding = encoding ?? Encodings[0];
			
			var header = new ByteBuffer();
			header.AppendU8(0xA0);
			header.AppendU8((byte) (_compressed ? 0x42 : 0x45));

			var encodingIndex = Array.IndexOf(Encodings, encoding) << 5;
			header.AppendU8(encodingIndex);
			header.AppendU8(encodingIndex ^ 0xFF);
			
			_nodeBuffer = new ByteBuffer();
			_dataBuffer = new ByteBuffer();
			_dataByteBuffer = new ByteBuffer();
			_dataWordBuffer = new ByteBuffer();
			
			WriteNode(document.Root);
			
			_nodeBuffer.AppendU8((byte)KBinReader.Control.SectionEnd | 64);
			_nodeBuffer.RealignWrite();
			header.AppendU32((uint) _nodeBuffer.Length);
			_nodeBuffer.AppendU32((uint) _dataBuffer.Length);
			Document = header.Data.Concat(_nodeBuffer.Data.Concat(_dataBuffer.Data)).ToArray();
		}

		private void WriteDataAligned(byte[] data, int size, int count) {
			if (_dataByteBuffer.Offset % 4 == 0)
				_dataByteBuffer.Offset = _dataBuffer.Offset;
			if (_dataWordBuffer.Offset % 4 == 0)
				_dataByteBuffer.Offset = _dataBuffer.Offset;

			var totalSize = size * count;
			if (totalSize == 1) {
				if (_dataByteBuffer.Offset % 4 == 0) {
					_dataByteBuffer.Offset = _dataBuffer.Length;
					_dataByteBuffer.AppendU32(0);
				}
				_dataByteBuffer.AppendU8(data[0]);
			} else if(totalSize == 2) {
				if (_dataWordBuffer.Offset % 4 == 0) {
					_dataWordBuffer.Offset = _dataBuffer.Length;
					_dataWordBuffer.AppendU32(0);
				}
				_dataByteBuffer.AppendBytes(data[..2]);
			} else {
				_dataBuffer.AppendBytes(data);
				_dataBuffer.RealignWrite();
			}
		}

		private void WriteDataAuto(byte[] data) {
			_dataBuffer.AppendS32(data.Length);
			_dataBuffer.AppendBytes(data);
			_dataBuffer.RealignWrite();
		}
		
		private void WriteString(string text) {
			WriteDataAuto(_encoding.GetBytes(text));
		}

		private void WriteNode(XElement element) {
			var nodeType = element.Attribute("__type")?.Value;
			if (nodeType == null) {
				if (element.GetValue().Length > 0) {
					nodeType = "str";
				} else {
					nodeType = "void";
				}
			}

			var (nodeId, format) = Formats.First(x => x.Value.HasName(nodeType));
			var isArray = 0;
			var countValue = element.Attribute("__count")?.Value;
			if (countValue != null) {
				var count = int.Parse(countValue);
				isArray = 0b01000000;
			}

			_nodeBuffer.AppendU8(nodeId | isArray);

			var name = element.Name.LocalName;
			WriteNodeName(name);

			if (nodeType != "void") {
				var value = element.GetValue();
				byte[] data;

				if (format.Name == "bin") {
					data = new byte[value.Length / 2];
					for (var i = 0; i < data.Length; i++) {
						data[i] = Convert.ToByte(value[i..(i + 2)], 16);
					}
				} else if (format.Name == "str") {
					data = _encoding.GetBytes(value); //Bug potential: kbinxml appends a null byte here.
				} else {
					data = value.Split(" ").Aggregate(new byte[0], (b, s) => b.Concat(format.FormatFromString(s)).ToArray());
				}

				if (isArray > 0 || format.Count == -1) {
					_dataBuffer.AppendU32((uint) (data.Length * format.Size));
					_dataBuffer.AppendBytes(data);
					_dataBuffer.RealignWrite();
				} else {
					WriteDataAligned(data, format.Size, format.Count);
				}
			}

			foreach (var a in element.Attributes().OrderBy(x => x.Name.LocalName)) {
				if (!a.Name.LocalName.StartsWith("__")) {
					WriteString(a.Value);
					_nodeBuffer.AppendU8((byte) KBinReader.Control.Attribute);
					WriteNodeName(a.Name.LocalName);
				}
			}

			foreach (var c in element.Descendants()) {
				WriteNode(c);
			}

			_nodeBuffer.AppendU8((byte) KBinReader.Control.NodeEnd | 64);
		}

		private void WriteNodeName(string name) {
			if (_compressed) {
				//TODO: Sixbit encoding. Fuck.
			} else {
				var encoded = _encoding.GetBytes(name);
				_nodeBuffer.AppendU8((encoded.Length - 1) | 64);
				_nodeBuffer.AppendBytes(encoded);
			}
		}
	}

}