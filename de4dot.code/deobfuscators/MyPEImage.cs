using System;
using System.Collections.Generic;
using dnlib.IO;
using dnlib.PE;
using dnlib.DotNet.MD;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	public sealed class MyPEImage : IDisposable {
		IPEImage peImage;
		byte[] peImageData;
		Metadata metadata;
		bool dnFileInitialized;
		ImageSectionHeader dotNetSection;
		bool ownPeImage;

		public Metadata Metadata {
			get {
				if (dnFileInitialized)
					return metadata;
				dnFileInitialized = true;

				var dotNetDir = peImage.ImageNTHeaders.OptionalHeader.DataDirectories[14];
				if (dotNetDir.VirtualAddress != 0 && dotNetDir.Size >= 0x48) {
					metadata = MetadataCreator.CreateMetadata(peImage, false);
					dotNetSection = FindSection(dotNetDir.VirtualAddress);
				}
				return metadata;
			}
		}

		public ImageCor20Header Cor20Header {
			get { return Metadata.ImageCor20Header; }
		}

		public DataReader Reader;

		public IPEImage PEImage {
			get { return peImage; }
		}

		public ImageFileHeader FileHeader {
			get { return peImage.ImageNTHeaders.FileHeader; }
		}

		public IImageOptionalHeader OptionalHeader {
			get { return peImage.ImageNTHeaders.OptionalHeader; }
		}

		public IList<ImageSectionHeader> Sections {
			get { return peImage.ImageSectionHeaders; }
		}

		public uint Length {
			get { return (uint)Reader.Length; }
		}

		public MyPEImage(IPEImage peImage) {
			Initialize(peImage);
		}

		public MyPEImage(byte[] peImageData) {
			this.ownPeImage = true;
			this.peImageData = peImageData;
			Initialize(new PEImage(peImageData));
		}

		void Initialize(IPEImage peImage) {
			this.peImage = peImage;
			this.Reader = peImage.CreateReader();
		}

		public ImageSectionHeader FindSection(RVA rva) {
			foreach (var section in peImage.ImageSectionHeaders) {
				if (section.VirtualAddress <= rva && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData))
					return section;
			}
			return null;
		}

		public ImageSectionHeader FindSection(string name) {
			foreach (var section in peImage.ImageSectionHeaders) {
				if (section.DisplayName == name)
					return section;
			}
			return null;
		}

		public void ReadMethodTableRowTo(DumpedMethod dm, uint rid) {
			dm.token = 0x06000000 + rid;
			RawMethodRow row;
			if (!Metadata.TablesStream.TryReadMethodRow(rid, out row))
				throw new ArgumentException("Invalid Method rid");
			dm.mdRVA = row.RVA;
			dm.mdImplFlags = row.ImplFlags;
			dm.mdFlags = row.Flags;
			dm.mdName = row.Name;
			dm.mdSignature = row.Signature;
			dm.mdParamList = row.ParamList;
		}

		public void UpdateMethodHeaderInfo(DumpedMethod dm, MethodBodyHeader mbHeader) {
			dm.mhFlags = mbHeader.flags;
			dm.mhMaxStack = mbHeader.maxStack;
			dm.mhCodeSize = dm.code == null ? 0 : (uint)dm.code.Length;
			dm.mhLocalVarSigTok = mbHeader.localVarSigTok;
		}

		public uint RvaToOffset(uint rva) {
			return (uint)peImage.ToFileOffset((RVA)rva);
		}

		static bool IsInside(ImageSectionHeader section, uint offset, uint length) {
			return offset >= section.PointerToRawData && offset + length <= section.PointerToRawData + section.SizeOfRawData;
		}

		public void WriteUInt32(uint rva, uint data) {
			OffsetWriteUInt32(RvaToOffset(rva), data);
		}

		public void WriteUInt16(uint rva, ushort data) {
			OffsetWriteUInt16(RvaToOffset(rva), data);
		}

		public byte ReadByte(uint rva) {
			return OffsetReadByte(RvaToOffset(rva));
		}

		public int ReadInt32(uint rva) {
			return (int)OffsetReadUInt32(RvaToOffset(rva));
		}

		public ushort ReadUInt16(uint rva) {
			return OffsetReadUInt16(RvaToOffset(rva));
		}

		public byte[] ReadBytes(uint rva, int size) {
			return OffsetReadBytes(RvaToOffset(rva), size);
		}

		public void OffsetWriteUInt32(uint offset, uint val) {
			peImageData[offset + 0] = (byte)val;
			peImageData[offset + 1] = (byte)(val >> 8);
			peImageData[offset + 2] = (byte)(val >> 16);
			peImageData[offset + 3] = (byte)(val >> 24);
		}

		public void OffsetWriteUInt16(uint offset, ushort val) {
			peImageData[offset + 0] = (byte)val;
			peImageData[offset + 1] = (byte)(val >> 8);
		}

		public uint OffsetReadUInt32(uint offset) {
			Reader.Position = offset;
			return Reader.ReadUInt32();
		}

		public ushort OffsetReadUInt16(uint offset) {
			Reader.Position = offset;
			return Reader.ReadUInt16();
		}

		public byte OffsetReadByte(uint offset) {
			Reader.Position = offset;
			return Reader.ReadByte();
		}

		public byte[] OffsetReadBytes(uint offset, int size) {
			Reader.Position = offset;
			return Reader.ReadBytes(size);
		}

		public void OffsetWrite(uint offset, byte[] data) {
			Array.Copy(data, 0, peImageData, offset, data.Length);
		}

		bool Intersect(uint offset1, uint length1, uint offset2, uint length2) {
			return !(offset1 + length1 <= offset2 || offset2 + length2 <= offset1);
		}

		bool Intersect(uint offset, uint length, IFileSection location) {
			return Intersect(offset, length, (uint)location.StartOffset, (uint)(location.EndOffset - location.StartOffset));
		}

		public bool DotNetSafeWriteOffset(uint offset, byte[] data) {
			if (Metadata != null) {
				uint length = (uint)data.Length;

				if (!IsInside(dotNetSection, offset, length))
					return false;
				if (Intersect(offset, length, Metadata.ImageCor20Header))
					return false;
				if (Intersect(offset, length, Metadata.MetadataHeader))
					return false;
			}

			OffsetWrite(offset, data);
			return true;
		}

		public bool DotNetSafeWrite(uint rva, byte[] data) {
			return DotNetSafeWriteOffset((uint)peImage.ToFileOffset((RVA)rva), data);
		}

		public void Dispose() {
			if (ownPeImage) {
				if (metadata != null)
					metadata.Dispose();
				if (peImage != null)
					peImage.Dispose();
			}

			metadata = null;
			peImage = null;
			Reader = default(DataReader);
		}
	}
}
