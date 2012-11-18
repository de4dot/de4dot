using System;
using dot10.IO;
using dot10.PE;
using dot10.DotNet.MD;

namespace de4dot.code.deobfuscators.dotNET_Reactor {
	sealed class MyPEImage : IDisposable {
		IPEImage peImage;
		byte[] peImageData;
		IImageStream peStream;
		DotNetFile dnFile;
		ImageSectionHeader dotNetSection;
		bool ownPeImage;

		public IPEImage PEImage {
			get { return peImage; }
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

			//TODO: Only init this if they use the .NET MD
			var dotNetDir = peImage.ImageNTHeaders.OptionalHeader.DataDirectories[14];
			if (dotNetDir.VirtualAddress != 0 && dotNetDir.Size >= 0x48) {
				dnFile = DotNetFile.Load(peImage, false);
				dotNetSection = findSection(dotNetDir.VirtualAddress);
			}
		}

		ImageSectionHeader findSection(RVA rva) {
			foreach (var section in peImage.ImageSectionHeaders) {
				if (section.VirtualAddress <= rva && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData))
					return section;
			}
			return null;
		}

		static bool isInside(ImageSectionHeader section, uint offset, uint length) {
			return offset >= section.PointerToRawData && offset + length <= section.PointerToRawData + section.SizeOfRawData;
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
			if (dnFile != null) {
				uint length = (uint)data.Length;

				if (!isInside(dotNetSection, offset, length))
					return false;
				if (intersect(offset, length, dnFile.MetaData.ImageCor20Header))
					return false;
				if (intersect(offset, length, (uint)dnFile.MetaData.TablesStream.FileOffset, dnFile.MetaData.TablesStream.HeaderLength))
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
