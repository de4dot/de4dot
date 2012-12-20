using System;
using System.Collections.Generic;
using dnlib.IO;
using dnlib.PE;
using dnlib.DotNet.MD;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	sealed class MyPEImage : IDisposable {
		IPEImage peImage;
		byte[] peImageData;
		IImageStream peStream;
		DotNetFile dnFile;
		bool dnFileInitialized;
		ImageSectionHeader dotNetSection;
		bool ownPeImage;

		public DotNetFile DotNetFile {
			get {
				if (dnFileInitialized)
					return dnFile;
				dnFileInitialized = true;

				var dotNetDir = peImage.ImageNTHeaders.OptionalHeader.DataDirectories[14];
				if (dotNetDir.VirtualAddress != 0 && dotNetDir.Size >= 0x48) {
					dnFile = DotNetFile.Load(peImage, false);
					dotNetSection = findSection(dotNetDir.VirtualAddress);
				}
				return dnFile;
			}
		}

		public ImageCor20Header Cor20Header {
			get { return DotNetFile.MetaData.ImageCor20Header; }
		}

		public IBinaryReader Reader {
			get { return peStream; }
		}

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
			get { return (uint)peStream.Length; }
		}

		public MyPEImage(IPEImage peImage) {
			initialize(peImage);
		}

		public MyPEImage(byte[] peImageData) {
			this.ownPeImage = true;
			this.peImageData = peImageData;
			initialize(new PEImage(peImageData));
		}

		void initialize(IPEImage peImage) {
			this.peImage = peImage;
			this.peStream = peImage.CreateFullStream();
		}

		ImageSectionHeader findSection(RVA rva) {
			foreach (var section in peImage.ImageSectionHeaders) {
				if (section.VirtualAddress <= rva && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData))
					return section;
			}
			return null;
		}

		public ImageSectionHeader findSection(string name) {
			foreach (var section in peImage.ImageSectionHeaders) {
				if (section.DisplayName == name)
					return section;
			}
			return null;
		}

		public void readMethodTableRowTo(DumpedMethod dm, uint rid) {
			dm.token = 0x06000000 + rid;
			var row = DotNetFile.MetaData.TablesStream.ReadMethodRow(rid);
			if (row == null)
				throw new ArgumentException("Invalid Method rid");
			dm.mdRVA = row.RVA;
			dm.mdImplFlags = row.ImplFlags;
			dm.mdFlags = row.Flags;
			dm.mdName = row.Name;
			dm.mdSignature = row.Signature;
			dm.mdParamList = row.ParamList;
		}

		public void updateMethodHeaderInfo(DumpedMethod dm, MethodBodyHeader mbHeader) {
			dm.mhFlags = mbHeader.flags;
			dm.mhMaxStack = mbHeader.maxStack;
			dm.mhCodeSize = dm.code == null ? 0 : (uint)dm.code.Length;
			dm.mhLocalVarSigTok = mbHeader.localVarSigTok;
		}

		public uint rvaToOffset(uint rva) {
			return (uint)peImage.ToFileOffset((RVA)rva);
		}

		static bool isInside(ImageSectionHeader section, uint offset, uint length) {
			return offset >= section.PointerToRawData && offset + length <= section.PointerToRawData + section.SizeOfRawData;
		}

		public void writeUInt32(uint rva, uint data) {
			offsetWriteUInt32(rvaToOffset(rva), data);
		}

		public void writeUInt16(uint rva, ushort data) {
			offsetWriteUInt16(rvaToOffset(rva), data);
		}

		public byte readByte(uint rva) {
			return offsetReadByte(rvaToOffset(rva));
		}

		public int readInt32(uint rva) {
			return (int)offsetReadUInt32(rvaToOffset(rva));
		}

		public ushort readUInt16(uint rva) {
			return offsetReadUInt16(rvaToOffset(rva));
		}

		public byte[] readBytes(uint rva, int size) {
			return offsetReadBytes(rvaToOffset(rva), size);
		}

		public void offsetWriteUInt32(uint offset, uint val) {
			peImageData[offset + 0] = (byte)val;
			peImageData[offset + 1] = (byte)(val >> 8);
			peImageData[offset + 2] = (byte)(val >> 16);
			peImageData[offset + 3] = (byte)(val >> 24);
		}

		public void offsetWriteUInt16(uint offset, ushort val) {
			peImageData[offset + 0] = (byte)val;
			peImageData[offset + 1] = (byte)(val >> 8);
		}

		public uint offsetReadUInt32(uint offset) {
			peStream.Position = offset;
			return peStream.ReadUInt32();
		}

		public ushort offsetReadUInt16(uint offset) {
			peStream.Position = offset;
			return peStream.ReadUInt16();
		}

		public byte offsetReadByte(uint offset) {
			peStream.Position = offset;
			return peStream.ReadByte();
		}

		public byte[] offsetReadBytes(uint offset, int size) {
			peStream.Position = offset;
			return peStream.ReadBytes(size);
		}

		public void offsetWrite(uint offset, byte[] data) {
			Array.Copy(data, 0, peImageData, offset, data.Length);
		}

		bool intersect(uint offset1, uint length1, uint offset2, uint length2) {
			return !(offset1 + length1 <= offset2 || offset2 + length2 <= offset1);
		}

		bool intersect(uint offset, uint length, IFileSection location) {
			return intersect(offset, length, (uint)location.StartOffset, (uint)(location.EndOffset - location.StartOffset));
		}

		public bool dotNetSafeWriteOffset(uint offset, byte[] data) {
			if (DotNetFile != null) {
				uint length = (uint)data.Length;

				if (!isInside(dotNetSection, offset, length))
					return false;
				if (intersect(offset, length, DotNetFile.MetaData.ImageCor20Header))
					return false;
				if (intersect(offset, length, DotNetFile.MetaData.MetaDataHeader))
					return false;
			}

			offsetWrite(offset, data);
			return true;
		}

		public bool dotNetSafeWrite(uint rva, byte[] data) {
			return dotNetSafeWriteOffset((uint)peImage.ToFileOffset((RVA)rva), data);
		}

		public void Dispose() {
			if (ownPeImage) {
				if (dnFile != null)
					dnFile.Dispose();
				if (peImage != null)
					peImage.Dispose();
			}
			if (peStream != null)
				peStream.Dispose();

			dnFile = null;
			peImage = null;
			peStream = null;
		}
	}
}
