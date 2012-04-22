/*
    Copyright (C) 2011-2012 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using Mono.MyStuff;
using de4dot.PE;

namespace de4dot.code.deobfuscators.CliSecure {
	class CodeHeader {
		public byte[] signature;
		public byte[] decryptionKey;
		public uint totalCodeSize;
		public uint numMethods;
		public uint methodDefTableOffset;	// Relative to start of metadata
		public uint methodDefElemSize;
	}

	struct MethodInfo {
		public uint codeOffs, codeSize, flags, localVarSigTok;

		public MethodInfo(uint codeOffs, uint codeSize, uint flags, uint localVarSigTok) {
			this.codeOffs = codeOffs;
			this.codeSize = codeSize;
			this.flags = flags;
			this.localVarSigTok = localVarSigTok;
		}

		public override string ToString() {
			return string.Format("{0:X8} {1:X8} {2:X8} {3:X8}", codeOffs, codeSize, flags, localVarSigTok);
		}
	}

	class MethodsDecrypter {
		static byte[] normalSignature = new byte[16] { 0x08, 0x44, 0x65, 0xE1, 0x8C, 0x82, 0x13, 0x4C, 0x9C, 0x85, 0xB4, 0x17, 0xDA, 0x51, 0xAD, 0x25 };
		static byte[] proSignature    = new byte[16] { 0x68, 0xA0, 0xBB, 0x60, 0x13, 0x65, 0x5F, 0x41, 0xAE, 0x42, 0xAB, 0x42, 0x9B, 0x6B, 0x4E, 0xC1 };

		enum SigType {
			Unknown,
			Normal,
			Pro,
		}

		PeImage peImage;
		CliSecureRtType csRtType;
		CodeHeader codeHeader = new CodeHeader();
		IDecrypter decrypter;

		interface IDecrypter {
			MethodBodyHeader decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections);
		}

		class Decrypter40 : IDecrypter {
			PeImage peImage;

			public Decrypter40(PeImage peImage) {
				this.peImage = peImage;
			}

			public MethodBodyHeader decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				peImage.Reader.BaseStream.Position = methodInfo.codeOffs;
				return MethodBodyParser.parseMethodBody(peImage.Reader, out code, out extraSections);
			}
		}

		abstract class DecrypterBase : IDecrypter {
			protected PeImage peImage;
			protected CodeHeader codeHeader;
			protected uint endOfMetadata;

			public DecrypterBase(PeImage peImage, CodeHeader codeHeader) {
				this.peImage = peImage;
				this.codeHeader = codeHeader;
				endOfMetadata = peImage.rvaToOffset(peImage.Cor20Header.metadataDirectory.virtualAddress + peImage.Cor20Header.metadataDirectory.size);
			}

			public abstract MethodBodyHeader decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections);

			protected MethodBodyHeader getCodeBytes(byte[] methodBody, out byte[] code, out byte[] extraSections) {
				return MethodBodyParser.parseMethodBody(new BinaryReader(new MemoryStream(methodBody)), out code, out extraSections);
			}
		}

		// CS 4.5
		class Decrypter45 : DecrypterBase {
			public Decrypter45(PeImage peImage, CodeHeader codeHeader)
				: base(peImage, codeHeader) {
			}

			public override MethodBodyHeader decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				var data = peImage.offsetReadBytes(endOfMetadata + methodInfo.codeOffs, (int)methodInfo.codeSize);
				for (int i = 0; i < data.Length; i++) {
					byte b = data[i];
					b ^= codeHeader.decryptionKey[(methodInfo.codeOffs - 0x28 + i) % 16];
					data[i] = b;
				}
				return getCodeBytes(data, out code, out extraSections);
			}
		}

		class NormalDecrypter : DecrypterBase {
			uint codeHeaderSize;

			public NormalDecrypter(PeImage peImage, CodeHeader codeHeader, uint codeHeaderSize)
				: base(peImage, codeHeader) {
					this.codeHeaderSize = codeHeaderSize;
			}

			public override MethodBodyHeader decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				byte[] data = peImage.offsetReadBytes(endOfMetadata + methodInfo.codeOffs, (int)methodInfo.codeSize);
				for (int i = 0; i < data.Length; i++) {
					byte b = data[i];
					b ^= codeHeader.decryptionKey[(methodInfo.codeOffs - codeHeaderSize + i) % 16];
					b ^= codeHeader.decryptionKey[(methodInfo.codeOffs - codeHeaderSize + i + 7) % 16];
					data[i] = b;
				}
				return getCodeBytes(data, out code, out extraSections);
			}
		}

		// Used when the anti-debugger protection is enabled
		class ProDecrypter : DecrypterBase {
			uint[] key = new uint[4];

			public ProDecrypter(PeImage peImage, CodeHeader codeHeader)
				: base(peImage, codeHeader) {
				for (int i = 0; i < 4; i++)
					key[i] = be_readUint32(codeHeader.decryptionKey, i * 4);
			}

			public override MethodBodyHeader decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				byte[] data = peImage.offsetReadBytes(endOfMetadata + methodInfo.codeOffs, (int)methodInfo.codeSize);

				int numQwords = (int)(methodInfo.codeSize / 8);
				for (int i = 0; i < numQwords; i++) {
					int offset = i * 8;
					uint q0 = be_readUint32(data, offset);
					uint q1 = be_readUint32(data, offset + 4);

					const uint magic = 0x9E3779B8;
					uint val = 0xC6EF3700;	// magic * 0x20
					for (int j = 0; j < 32; j++) {
						q1 -= ((q0 << 4) + key[2]) ^ (val + q0) ^ ((q0 >> 5) + key[3]);
						q0 -= ((q1 << 4) + key[0]) ^ (val + q1) ^ ((q1 >> 5) + key[1]);
						val -= magic;
					}

					be_writeUint32(data, offset, q0);
					be_writeUint32(data, offset + 4, q1);
				}

				return getCodeBytes(data, out code, out extraSections);
			}

			static uint be_readUint32(byte[] data, int offset) {
				return (uint)((data[offset] << 24) +
						(data[offset + 1] << 16) +
						(data[offset + 2] << 8) +
						data[offset + 3]);
			}

			static void be_writeUint32(byte[] data, int offset, uint value) {
				data[offset] = (byte)(value >> 24);
				data[offset + 1] = (byte)(value >> 16);
				data[offset + 2] = (byte)(value >> 8);
				data[offset + 3] = (byte)value;
			}
		}

		interface ICsHeader {
			IDecrypter createDecrypter();
			List<MethodInfo> getMethodInfos(uint codeHeaderOffset);
			void patchMethodDefTable(MetadataType methodDefTable, IList<MethodInfo> methodInfos);
		}

		abstract class CsHeaderBase : ICsHeader {
			protected readonly MethodsDecrypter methodsDecrypter;
			protected readonly uint codeHeaderSize;

			public CsHeaderBase(MethodsDecrypter methodsDecrypter, uint codeHeaderSize) {
				this.methodsDecrypter = methodsDecrypter;
				this.codeHeaderSize = codeHeaderSize;
			}

			public abstract IDecrypter createDecrypter();

			public virtual void patchMethodDefTable(MetadataType methodDefTable, IList<MethodInfo> methodInfos) {
			}

			public abstract List<MethodInfo> getMethodInfos(uint codeHeaderOffset);

			protected List<MethodInfo> getMethodInfos1(uint codeHeaderOffset) {
				uint offset = codeHeaderOffset + methodsDecrypter.codeHeader.totalCodeSize + codeHeaderSize;
				var methodInfos = new List<MethodInfo>((int)methodsDecrypter.codeHeader.numMethods);
				for (int i = 0; i < (int)methodsDecrypter.codeHeader.numMethods; i++, offset += 4) {
					uint codeOffs = methodsDecrypter.peImage.offsetReadUInt32(offset);
					if (codeOffs != 0)
						codeOffs += codeHeaderOffset;
					methodInfos.Add(new MethodInfo(codeOffs, 0, 0, 0));
				}
				return methodInfos;
			}

			protected List<MethodInfo> getMethodInfos2(uint codeHeaderOffset) {
				uint offset = codeHeaderOffset + methodsDecrypter.codeHeader.totalCodeSize + codeHeaderSize;
				var methodInfos = new List<MethodInfo>((int)methodsDecrypter.codeHeader.numMethods);
				for (int i = 0; i < (int)methodsDecrypter.codeHeader.numMethods; i++, offset += 8) {
					uint codeOffs = methodsDecrypter.peImage.offsetReadUInt32(offset);
					uint codeSize = methodsDecrypter.peImage.offsetReadUInt32(offset + 4);
					methodInfos.Add(new MethodInfo(codeOffs, codeSize, 0, 0));
				}
				return methodInfos;
			}

			protected List<MethodInfo> getMethodInfos4(uint codeHeaderOffset) {
				uint offset = codeHeaderOffset + methodsDecrypter.codeHeader.totalCodeSize + codeHeaderSize;
				var methodInfos = new List<MethodInfo>((int)methodsDecrypter.codeHeader.numMethods);
				for (int i = 0; i < (int)methodsDecrypter.codeHeader.numMethods; i++, offset += 16) {
					uint codeOffs = methodsDecrypter.peImage.offsetReadUInt32(offset);
					uint codeSize = methodsDecrypter.peImage.offsetReadUInt32(offset + 4);
					uint flags = methodsDecrypter.peImage.offsetReadUInt32(offset + 8);
					uint localVarSigTok = methodsDecrypter.peImage.offsetReadUInt32(offset + 12);
					methodInfos.Add(new MethodInfo(codeOffs, codeSize, flags, localVarSigTok));
				}
				return methodInfos;
			}
		}

		// CS 4.0
		class CsHeader40 : CsHeaderBase {
			public CsHeader40(MethodsDecrypter methodsDecrypter)
				: base(methodsDecrypter, 0x28) {
			}

			public override IDecrypter createDecrypter() {
				return new Decrypter40(methodsDecrypter.peImage);
			}

			public override List<MethodInfo> getMethodInfos(uint codeHeaderOffset) {
				return getMethodInfos1(codeHeaderOffset);
			}
		}

		// CS 4.5
		class CsHeader45 : CsHeaderBase {
			public CsHeader45(MethodsDecrypter methodsDecrypter)
				: base(methodsDecrypter, 0x28) {
			}

			public override IDecrypter createDecrypter() {
				return new Decrypter45(methodsDecrypter.peImage, methodsDecrypter.codeHeader);
			}

			public override List<MethodInfo> getMethodInfos(uint codeHeaderOffset) {
				return getMethodInfos2(codeHeaderOffset);
			}
		}

		// CS 5.0+
		class CsHeader5 : CsHeaderBase {
			public CsHeader5(MethodsDecrypter methodsDecrypter, uint codeHeaderSize)
				: base(methodsDecrypter, codeHeaderSize) {
			}

			public override IDecrypter createDecrypter() {
				switch (getSigType(methodsDecrypter.codeHeader.signature)) {
				case SigType.Normal:
					return new NormalDecrypter(methodsDecrypter.peImage, methodsDecrypter.codeHeader, codeHeaderSize);

				case SigType.Pro:
					return new ProDecrypter(methodsDecrypter.peImage, methodsDecrypter.codeHeader);

				case SigType.Unknown:
				default:
					throw new ArgumentException("sig");
				}
			}

			public override List<MethodInfo> getMethodInfos(uint codeHeaderOffset) {
				if (codeHeaderSize == 0x28)
					return getMethodInfos2(codeHeaderOffset);
				return getMethodInfos4(codeHeaderOffset);
			}

			public override void patchMethodDefTable(MetadataType methodDefTable, IList<MethodInfo> methodInfos) {
				uint offset = methodDefTable.fileOffset - methodDefTable.totalSize;
				foreach (var methodInfo in methodInfos) {
					offset += methodDefTable.totalSize;
					if (methodInfo.flags == 0 || methodInfo.codeOffs == 0)
						continue;
					uint rva = methodsDecrypter.peImage.offsetReadUInt32(offset);
					methodsDecrypter.peImage.writeUint16(rva, (ushort)methodInfo.flags);
					methodsDecrypter.peImage.writeUint32(rva + 8, methodInfo.localVarSigTok);
				}
			}
		}

		public bool decrypt(PeImage peImage, string filename, CliSecureRtType csRtType, ref DumpedMethods dumpedMethods) {
			this.peImage = peImage;
			this.csRtType = csRtType;
			try {
				return decrypt2(ref dumpedMethods);
			}
			catch (InvalidMethodBody) {
				Log.w("Using dynamic method decryption");
				byte[] moduleCctorBytes = getModuleCctorBytes(csRtType);
				dumpedMethods = de4dot.code.deobfuscators.MethodsDecrypter.decrypt(filename, moduleCctorBytes);
				return true;
			}
		}

		static byte[] getModuleCctorBytes(CliSecureRtType csRtType) {
			var initMethod = csRtType.InitializeMethod;
			if (initMethod == null)
				return null;
			uint initToken = initMethod.MetadataToken.ToUInt32();
			var moduleCctorBytes = new byte[6];
			moduleCctorBytes[0] = 0x28;	// call
			moduleCctorBytes[1] = (byte)initToken;
			moduleCctorBytes[2] = (byte)(initToken >> 8);
			moduleCctorBytes[3] = (byte)(initToken >> 16);
			moduleCctorBytes[4] = (byte)(initToken >> 24);
			moduleCctorBytes[5] = 0x2A;	// ret
			return moduleCctorBytes;
		}

		enum CsHeaderVersion {
			Unknown,
			V40,
			V45,
			V50,	// 5.0, possibly also 5.1
			V52,	// 5.2+ (or maybe 5.1+)
		}

		CsHeaderVersion getCsHeaderVersion(uint codeHeaderOffset, MetadataType methodDefTable) {
			if (!isOldHeader(methodDefTable))
				return CsHeaderVersion.V52;
			if (csRtType.isAtLeastVersion50())
				return CsHeaderVersion.V50;
			if (isCsHeader40(codeHeaderOffset))
				return CsHeaderVersion.V40;
			return CsHeaderVersion.V45;
		}

		bool isCsHeader40(uint codeHeaderOffset) {
			try {
				uint offset = codeHeaderOffset + codeHeader.totalCodeSize + 0x28;
				uint prevCodeOffs = 0;
				for (int i = 0; i < (int)codeHeader.numMethods; i++, offset += 4) {
					uint codeOffs = peImage.offsetReadUInt32(offset);
					if (prevCodeOffs != 0 && codeOffs != 0 && codeOffs < prevCodeOffs)
						return false;
					if (codeOffs != 0)
						prevCodeOffs = codeOffs;
				}

				return true;
			}
			catch (IOException) {
				return false;
			}
		}

		bool isOldHeader(MetadataType methodDefTable) {
			if (methodDefTable.totalSize != codeHeader.methodDefElemSize)
				return true;
			if (methodDefTable.fileOffset - peImage.rvaToOffset(peImage.Cor20Header.metadataDirectory.virtualAddress) != codeHeader.methodDefTableOffset)
				return true;

			return false;
		}

		ICsHeader createCsHeader(uint codeHeaderOffset, MetadataType methodDefTable) {
			switch (getCsHeaderVersion(codeHeaderOffset, methodDefTable)) {
			case CsHeaderVersion.V40: return new CsHeader40(this);
			case CsHeaderVersion.V45: return new CsHeader45(this);
			case CsHeaderVersion.V50: return new CsHeader5(this, 0x28);
			case CsHeaderVersion.V52: return new CsHeader5(this, 0x30);
			default: throw new ApplicationException("Unknown CS header");
			}
		}

		static uint getCodeHeaderOffset(PeImage peImage) {
			return peImage.rvaToOffset(peImage.Cor20Header.metadataDirectory.virtualAddress + peImage.Cor20Header.metadataDirectory.size);
		}

		public bool decrypt2(ref DumpedMethods dumpedMethods) {
			uint codeHeaderOffset = getCodeHeaderOffset(peImage);
			if (!readCodeHeader(codeHeaderOffset))
				return false;

			var metadataTables = peImage.Cor20Header.createMetadataTables();
			var methodDefTable = metadataTables.getMetadataType(MetadataIndex.iMethodDef);

			var csHeader = createCsHeader(codeHeaderOffset, methodDefTable);
			var methodInfos = csHeader.getMethodInfos(codeHeaderOffset);
			csHeader.patchMethodDefTable(methodDefTable, methodInfos);

			dumpedMethods = new DumpedMethods();
			uint offset = methodDefTable.fileOffset;
			decrypter = csHeader.createDecrypter();
			for (int i = 0; i < methodInfos.Count; i++, offset += methodDefTable.totalSize) {
				var methodInfo = methodInfos[i];
				if (methodInfo.codeOffs == 0)
					continue;

				var dm = new DumpedMethod();
				dm.token = 0x06000001 + (uint)i;

				dm.mdImplFlags = peImage.offsetReadUInt16(offset + (uint)methodDefTable.fields[1].offset);
				dm.mdFlags = peImage.offsetReadUInt16(offset + (uint)methodDefTable.fields[2].offset);
				dm.mdName = peImage.offsetRead(offset + (uint)methodDefTable.fields[3].offset, methodDefTable.fields[3].size);
				dm.mdSignature = peImage.offsetRead(offset + (uint)methodDefTable.fields[4].offset, methodDefTable.fields[4].size);
				dm.mdParamList = peImage.offsetRead(offset + (uint)methodDefTable.fields[5].offset, methodDefTable.fields[5].size);

				var mbHeader = decrypter.decrypt(methodInfo, out dm.code, out dm.extraSections);
				dm.mhFlags = mbHeader.flags;
				dm.mhMaxStack = mbHeader.maxStack;
				dm.mhCodeSize = (uint)dm.code.Length;
				dm.mhLocalVarSigTok = mbHeader.localVarSigTok;

				dumpedMethods.add(dm);
			}

			return true;
		}

		bool readCodeHeader(uint offset) {
			codeHeader.signature = peImage.offsetReadBytes(offset, 16);
			codeHeader.decryptionKey = peImage.offsetReadBytes(offset + 0x10, 16);
			codeHeader.totalCodeSize = peImage.offsetReadUInt32(offset + 0x20);
			codeHeader.numMethods = peImage.offsetReadUInt32(offset + 0x24);
			codeHeader.methodDefTableOffset = peImage.offsetReadUInt32(offset + 0x28);
			codeHeader.methodDefElemSize = peImage.offsetReadUInt32(offset + 0x2C);

			if (!isValidSignature(codeHeader.signature))
				return false;
			if (codeHeader.totalCodeSize > 0x10000000)
				return false;
			if (codeHeader.numMethods > 512*1024)
				return false;

			return true;
		}

		static SigType getSigType(byte[] sig) {
			if (Utils.compare(sig, normalSignature))
				return SigType.Normal;
			else if (Utils.compare(sig, proSignature))
				return SigType.Pro;
			return SigType.Unknown;
		}

		static bool isValidSignature(byte[] signature) {
			return getSigType(signature) != SigType.Unknown;
		}

		public static bool detect(PeImage peImage) {
			try {
				uint codeHeaderOffset = getCodeHeaderOffset(peImage);
				var sig = peImage.offsetReadBytes(codeHeaderOffset, 16);
				return isValidSignature(sig);
			}
			catch {
				return false;
			}
		}
	}
}
