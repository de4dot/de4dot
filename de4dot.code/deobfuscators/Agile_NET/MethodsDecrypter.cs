/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
using dnlib.IO;
using dnlib.PE;
using dnlib.DotNet;
using dnlib.DotNet.MD;
using de4dot.blocks;
using de4dot.code.AssemblyClient;

namespace de4dot.code.deobfuscators.Agile_NET {
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
		static readonly byte[] oldSignature    = new byte[16] { 0x1F, 0x68, 0x9D, 0x2B, 0x07, 0x4A, 0xA6, 0x4A, 0x92, 0xBB, 0x31, 0x7E, 0x60, 0x7F, 0xD7, 0xCD };
		static readonly byte[] normalSignature = new byte[16] { 0x08, 0x44, 0x65, 0xE1, 0x8C, 0x82, 0x13, 0x4C, 0x9C, 0x85, 0xB4, 0x17, 0xDA, 0x51, 0xAD, 0x25 };
		static readonly byte[] proSignature    = new byte[16] { 0x68, 0xA0, 0xBB, 0x60, 0x13, 0x65, 0x5F, 0x41, 0xAE, 0x42, 0xAB, 0x42, 0x9B, 0x6B, 0x4E, 0xC1 };

		enum SigType {
			Unknown,
			Old,
			Normal,
			Pro,
		}

		MyPEImage peImage;
		ModuleDefMD module;
		CliSecureRtType csRtType;
		CodeHeader codeHeader = new CodeHeader();
		IDecrypter decrypter;
		SigType sigType;

		interface IDecrypter {
			MethodBodyHeader Decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections);
		}

		abstract class DecrypterBase : IDecrypter {
			protected readonly MyPEImage peImage;
			protected readonly CodeHeader codeHeader;
			protected readonly uint endOfMetadata;

			public DecrypterBase(MyPEImage peImage, CodeHeader codeHeader) {
				this.peImage = peImage;
				this.codeHeader = codeHeader;
				var mdDir = peImage.Cor20Header.MetaData;
				endOfMetadata = peImage.RvaToOffset((uint)mdDir.VirtualAddress + mdDir.Size);
			}

			public abstract MethodBodyHeader Decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections);

			protected MethodBodyHeader GetCodeBytes(byte[] methodBody, out byte[] code, out byte[] extraSections) {
				return MethodBodyParser.ParseMethodBody(MemoryImageStream.Create(methodBody), out code, out extraSections);
			}
		}

		// CS 1.1 (could be other versions too)
		class Decrypter10 {
			MyPEImage peImage;
			CsBlowfish blowfish;

			public Decrypter10(MyPEImage peImage, byte[] key) {
				this.peImage = peImage;
				this.blowfish = new CsBlowfish(key);
			}

			public MethodBodyHeader Decrypt(uint bodyOffset, out byte[] code, out byte[] extraSections) {
				peImage.Reader.Position = bodyOffset;
				var mbHeader = MethodBodyParser.ParseMethodBody(peImage.Reader, out code, out extraSections);
				blowfish.Decrypt(code);
				return mbHeader;
			}
		}

		// CS 3.0 (could be other versions too)
		class Decrypter30 : DecrypterBase {
			public Decrypter30(MyPEImage peImage, CodeHeader codeHeader)
				: base(peImage, codeHeader) {
			}

			public override MethodBodyHeader Decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				peImage.Reader.Position = peImage.RvaToOffset(methodInfo.codeOffs);
				return MethodBodyParser.ParseMethodBody(peImage.Reader, out code, out extraSections);
			}
		}

		// CS 4.0 (could be other versions too)
		class Decrypter40 : DecrypterBase {
			public Decrypter40(MyPEImage peImage, CodeHeader codeHeader)
				: base(peImage, codeHeader) {
			}

			public override MethodBodyHeader Decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				peImage.Reader.Position = endOfMetadata + methodInfo.codeOffs;
				return MethodBodyParser.ParseMethodBody(peImage.Reader, out code, out extraSections);
			}
		}

		// CS 4.5 (could be other versions too)
		class Decrypter45 : DecrypterBase {
			public Decrypter45(MyPEImage peImage, CodeHeader codeHeader)
				: base(peImage, codeHeader) {
			}

			public override MethodBodyHeader Decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				var data = peImage.OffsetReadBytes(endOfMetadata + methodInfo.codeOffs, (int)methodInfo.codeSize);
				for (int i = 0; i < data.Length; i++) {
					byte b = data[i];
					b ^= codeHeader.decryptionKey[(methodInfo.codeOffs - 0x28 + i) % 16];
					data[i] = b;
				}
				return GetCodeBytes(data, out code, out extraSections);
			}
		}

		// CS 5.0+
		class Decrypter5 : DecrypterBase {
			readonly uint codeHeaderSize;

			public Decrypter5(MyPEImage peImage, CodeHeader codeHeader, uint codeHeaderSize)
				: base(peImage, codeHeader) {
				this.codeHeaderSize = codeHeaderSize;
			}

			public override MethodBodyHeader Decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				byte[] data = peImage.OffsetReadBytes(endOfMetadata + methodInfo.codeOffs, (int)methodInfo.codeSize);
				for (int i = 0; i < data.Length; i++) {
					byte b = data[i];
					b ^= codeHeader.decryptionKey[(methodInfo.codeOffs - codeHeaderSize + i) % 16];
					b ^= codeHeader.decryptionKey[(methodInfo.codeOffs - codeHeaderSize + i + 7) % 16];
					data[i] = b;
				}
				return GetCodeBytes(data, out code, out extraSections);
			}
		}

		// CS 5.4+. Used when the anti-debugger protection is enabled
		class ProDecrypter : DecrypterBase {
			readonly uint[] key = new uint[4];

			public ProDecrypter(MyPEImage peImage, CodeHeader codeHeader)
				: base(peImage, codeHeader) {
				for (int i = 0; i < 4; i++)
					key[i] = ReadUInt32_be(codeHeader.decryptionKey, i * 4);
			}

			public override MethodBodyHeader Decrypt(MethodInfo methodInfo, out byte[] code, out byte[] extraSections) {
				byte[] data = peImage.OffsetReadBytes(endOfMetadata + methodInfo.codeOffs, (int)methodInfo.codeSize);

				int numQwords = (int)(methodInfo.codeSize / 8);
				for (int i = 0; i < numQwords; i++) {
					int offset = i * 8;
					uint q0 = ReadUInt32_be(data, offset);
					uint q1 = ReadUInt32_be(data, offset + 4);

					const uint magic = 0x9E3779B8;
					uint val = 0xC6EF3700;	// magic * 0x20
					for (int j = 0; j < 32; j++) {
						q1 -= ((q0 << 4) + key[2]) ^ (val + q0) ^ ((q0 >> 5) + key[3]);
						q0 -= ((q1 << 4) + key[0]) ^ (val + q1) ^ ((q1 >> 5) + key[1]);
						val -= magic;
					}

					WriteUInt32_be(data, offset, q0);
					WriteUInt32_be(data, offset + 4, q1);
				}

				return GetCodeBytes(data, out code, out extraSections);
			}

			static uint ReadUInt32_be(byte[] data, int offset) {
				return (uint)((data[offset] << 24) +
						(data[offset + 1] << 16) +
						(data[offset + 2] << 8) +
						data[offset + 3]);
			}

			static void WriteUInt32_be(byte[] data, int offset, uint value) {
				data[offset] = (byte)(value >> 24);
				data[offset + 1] = (byte)(value >> 16);
				data[offset + 2] = (byte)(value >> 8);
				data[offset + 3] = (byte)value;
			}
		}

		interface ICsHeader {
			IDecrypter CreateDecrypter();
			List<MethodInfo> GetMethodInfos(uint codeHeaderOffset);
			void PatchMethodTable(MDTable methodDefTable, IList<MethodInfo> methodInfos);
		}

		abstract class CsHeaderBase : ICsHeader {
			protected readonly MethodsDecrypter methodsDecrypter;
			protected readonly uint codeHeaderSize;

			public CsHeaderBase(MethodsDecrypter methodsDecrypter, uint codeHeaderSize) {
				this.methodsDecrypter = methodsDecrypter;
				this.codeHeaderSize = codeHeaderSize;
			}

			public abstract IDecrypter CreateDecrypter();

			public virtual void PatchMethodTable(MDTable methodDefTable, IList<MethodInfo> methodInfos) {
			}

			public abstract List<MethodInfo> GetMethodInfos(uint codeHeaderOffset);

			protected List<MethodInfo> GetMethodInfos1(uint codeHeaderOffset) {
				uint offset = codeHeaderOffset + methodsDecrypter.codeHeader.totalCodeSize + codeHeaderSize;
				var methodInfos = new List<MethodInfo>((int)methodsDecrypter.codeHeader.numMethods);
				for (int i = 0; i < (int)methodsDecrypter.codeHeader.numMethods; i++, offset += 4) {
					uint codeOffs = methodsDecrypter.peImage.OffsetReadUInt32(offset);
					methodInfos.Add(new MethodInfo(codeOffs, 0, 0, 0));
				}
				return methodInfos;
			}

			protected List<MethodInfo> GetMethodInfos2(uint codeHeaderOffset) {
				uint offset = codeHeaderOffset + methodsDecrypter.codeHeader.totalCodeSize + codeHeaderSize;
				var methodInfos = new List<MethodInfo>((int)methodsDecrypter.codeHeader.numMethods);
				for (int i = 0; i < (int)methodsDecrypter.codeHeader.numMethods; i++, offset += 8) {
					uint codeOffs = methodsDecrypter.peImage.OffsetReadUInt32(offset);
					uint codeSize = methodsDecrypter.peImage.OffsetReadUInt32(offset + 4);
					methodInfos.Add(new MethodInfo(codeOffs, codeSize, 0, 0));
				}
				return methodInfos;
			}

			protected List<MethodInfo> GetMethodInfos4(uint codeHeaderOffset) {
				uint offset = codeHeaderOffset + methodsDecrypter.codeHeader.totalCodeSize + codeHeaderSize;
				var methodInfos = new List<MethodInfo>((int)methodsDecrypter.codeHeader.numMethods);
				for (int i = 0; i < (int)methodsDecrypter.codeHeader.numMethods; i++, offset += 16) {
					uint codeOffs = methodsDecrypter.peImage.OffsetReadUInt32(offset);
					uint codeSize = methodsDecrypter.peImage.OffsetReadUInt32(offset + 4);
					uint flags = methodsDecrypter.peImage.OffsetReadUInt32(offset + 8);
					uint localVarSigTok = methodsDecrypter.peImage.OffsetReadUInt32(offset + 12);
					methodInfos.Add(new MethodInfo(codeOffs, codeSize, flags, localVarSigTok));
				}
				return methodInfos;
			}
		}

		// CS 3.0 (could be other versions too)
		class CsHeader30 : CsHeaderBase {
			public CsHeader30(MethodsDecrypter methodsDecrypter)
				: base(methodsDecrypter, 0x28) {
			}

			public override IDecrypter CreateDecrypter() {
				return new Decrypter30(methodsDecrypter.peImage, methodsDecrypter.codeHeader);
			}

			public override List<MethodInfo> GetMethodInfos(uint codeHeaderOffset) {
				return GetMethodInfos1(codeHeaderOffset);
			}
		}

		// CS 4.0 (could be other versions too)
		class CsHeader40 : CsHeaderBase {
			public CsHeader40(MethodsDecrypter methodsDecrypter)
				: base(methodsDecrypter, 0x28) {
			}

			public override IDecrypter CreateDecrypter() {
				return new Decrypter40(methodsDecrypter.peImage, methodsDecrypter.codeHeader);
			}

			public override List<MethodInfo> GetMethodInfos(uint codeHeaderOffset) {
				return GetMethodInfos1(codeHeaderOffset);
			}
		}

		// CS 4.5 (could be other versions too)
		class CsHeader45 : CsHeaderBase {
			public CsHeader45(MethodsDecrypter methodsDecrypter)
				: base(methodsDecrypter, 0x28) {
			}

			public override IDecrypter CreateDecrypter() {
				return new Decrypter45(methodsDecrypter.peImage, methodsDecrypter.codeHeader);
			}

			public override List<MethodInfo> GetMethodInfos(uint codeHeaderOffset) {
				return GetMethodInfos2(codeHeaderOffset);
			}
		}

		// CS 5.0+
		class CsHeader5 : CsHeaderBase {
			public CsHeader5(MethodsDecrypter methodsDecrypter, uint codeHeaderSize)
				: base(methodsDecrypter, codeHeaderSize) {
			}

			public override IDecrypter CreateDecrypter() {
				switch (GetSigType(methodsDecrypter.codeHeader.signature)) {
				case SigType.Normal:
					return new Decrypter5(methodsDecrypter.peImage, methodsDecrypter.codeHeader, codeHeaderSize);

				case SigType.Pro:
					return new ProDecrypter(methodsDecrypter.peImage, methodsDecrypter.codeHeader);

				case SigType.Unknown:
				default:
					throw new ArgumentException("sig");
				}
			}

			public override List<MethodInfo> GetMethodInfos(uint codeHeaderOffset) {
				if (codeHeaderSize == 0x28)
					return GetMethodInfos2(codeHeaderOffset);
				return GetMethodInfos4(codeHeaderOffset);
			}

			public override void PatchMethodTable(MDTable methodDefTable, IList<MethodInfo> methodInfos) {
				uint offset = (uint)methodDefTable.StartOffset - methodDefTable.RowSize;
				foreach (var methodInfo in methodInfos) {
					offset += methodDefTable.RowSize;
					if (methodInfo.flags == 0 || methodInfo.codeOffs == 0)
						continue;
					uint rva = methodsDecrypter.peImage.OffsetReadUInt32(offset);
					methodsDecrypter.peImage.WriteUInt16(rva, (ushort)methodInfo.flags);
					methodsDecrypter.peImage.WriteUInt32(rva + 8, methodInfo.localVarSigTok);
				}
			}
		}

		enum CsHeaderVersion {
			Unknown,
			V10,
			V30,
			V40,
			V45,
			V50,	// 5.0, possibly also 5.1
			V52,	// 5.2+ (or maybe 5.1+)
		}

		List<CsHeaderVersion> GetCsHeaderVersions(uint codeHeaderOffset, MDTable methodDefTable) {
			if (sigType == SigType.Old)
				return new List<CsHeaderVersion> { CsHeaderVersion.V10 };
			if (!IsOldHeader(methodDefTable))
				return new List<CsHeaderVersion> { CsHeaderVersion.V52 };
			if (csRtType.IsAtLeastVersion50())
				return new List<CsHeaderVersion> { CsHeaderVersion.V50 };
			if (IsCsHeader40(codeHeaderOffset)) {
				return new List<CsHeaderVersion> {
					CsHeaderVersion.V40,
					CsHeaderVersion.V30,
				};
			}
			return new List<CsHeaderVersion> {
				CsHeaderVersion.V45,
				CsHeaderVersion.V50,
			};
		}

		bool IsCsHeader40(uint codeHeaderOffset) {
			try {
				uint offset = codeHeaderOffset + codeHeader.totalCodeSize + 0x28;
				uint prevCodeOffs = 0;
				for (int i = 0; i < (int)codeHeader.numMethods; i++, offset += 4) {
					uint codeOffs = peImage.OffsetReadUInt32(offset);
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

		bool IsOldHeader(MDTable methodDefTable) {
			if (methodDefTable.RowSize != codeHeader.methodDefElemSize)
				return true;
			if ((uint)methodDefTable.StartOffset - peImage.RvaToOffset((uint)peImage.Cor20Header.MetaData.VirtualAddress) != codeHeader.methodDefTableOffset)
				return true;

			return false;
		}

		ICsHeader CreateCsHeader(CsHeaderVersion version) {
			switch (version) {
			case CsHeaderVersion.V30: return new CsHeader30(this);
			case CsHeaderVersion.V40: return new CsHeader40(this);
			case CsHeaderVersion.V45: return new CsHeader45(this);
			case CsHeaderVersion.V50: return new CsHeader5(this, 0x28);
			case CsHeaderVersion.V52: return new CsHeader5(this, 0x30);
			default: throw new ApplicationException("Unknown CS header");
			}
		}

		enum DecryptResult {
			NotEncrypted,
			Decrypted,
			Error,
		}

		public bool Decrypt(MyPEImage peImage, ModuleDefMD module, CliSecureRtType csRtType, ref DumpedMethods dumpedMethods) {
			this.peImage = peImage;
			this.csRtType = csRtType;
			this.module = module;

			switch (Decrypt2(ref dumpedMethods)) {
			case DecryptResult.Decrypted: return true;
			case DecryptResult.NotEncrypted: return false;

			case DecryptResult.Error:
				Logger.n("Using dynamic method decryption");
				byte[] moduleCctorBytes = GetModuleCctorBytes(csRtType);
				dumpedMethods = de4dot.code.deobfuscators.MethodsDecrypter.Decrypt(module, moduleCctorBytes);
				return true;

			default:
				throw new ApplicationException("Invalid DecryptResult");
			}
		}

		static byte[] GetModuleCctorBytes(CliSecureRtType csRtType) {
			var initMethod = csRtType.InitializeMethod;
			if (initMethod == null)
				return null;
			uint initToken = initMethod.MDToken.ToUInt32();
			var moduleCctorBytes = new byte[6];
			moduleCctorBytes[0] = 0x28;	// call
			moduleCctorBytes[1] = (byte)initToken;
			moduleCctorBytes[2] = (byte)(initToken >> 8);
			moduleCctorBytes[3] = (byte)(initToken >> 16);
			moduleCctorBytes[4] = (byte)(initToken >> 24);
			moduleCctorBytes[5] = 0x2A;	// ret
			return moduleCctorBytes;
		}

		static uint GetCodeHeaderOffset(MyPEImage peImage) {
			return peImage.RvaToOffset((uint)peImage.Cor20Header.MetaData.VirtualAddress + peImage.Cor20Header.MetaData.Size);
		}

		static string[] sections = new string[] {
			".text", ".rsrc", ".data", ".rdata",
		};
		static uint GetOldCodeHeaderOffset(MyPEImage peImage) {
			var sect = GetLastOf(peImage, sections);
			if (sect == null || sect.VirtualSize < 0x100)
				return 0;
			return peImage.RvaToOffset((uint)sect.VirtualAddress + sect.VirtualSize - 0x100);
		}

		static ImageSectionHeader GetLastOf(MyPEImage peImage, string[] sections) {
			ImageSectionHeader sect = null;
			foreach (var name in sections) {
				var sect2 = peImage.FindSection(name);
				if (sect2 == null)
					continue;
				if (sect == null || sect2.VirtualAddress > sect.VirtualAddress)
					sect = sect2;
			}
			return sect;
		}

		DecryptResult Decrypt2(ref DumpedMethods dumpedMethods) {
			uint codeHeaderOffset = InitializeCodeHeader();
			if (sigType == SigType.Unknown)
				return DecryptResult.NotEncrypted;

			var methodDefTable = peImage.MetaData.TablesStream.MethodTable;

			foreach (var version in GetCsHeaderVersions(codeHeaderOffset, methodDefTable)) {
				try {
					if (version == CsHeaderVersion.V10)
						DecryptMethodsOld(methodDefTable, ref dumpedMethods);
					else
						DecryptMethods(codeHeaderOffset, methodDefTable, CreateCsHeader(version), ref dumpedMethods);
					return DecryptResult.Decrypted;
				}
				catch {
				}
			}

			return DecryptResult.Error;
		}

		uint InitializeCodeHeader() {
			uint codeHeaderOffset = GetCodeHeaderOffset(peImage);
			ReadCodeHeader(codeHeaderOffset);
			sigType = GetSigType(codeHeader.signature);

			if (sigType == SigType.Unknown) {
				codeHeaderOffset = GetOldCodeHeaderOffset(peImage);
				if (codeHeaderOffset != 0) {
					ReadCodeHeader(codeHeaderOffset);
					sigType = GetSigType(codeHeader.signature);
				}
			}

			return codeHeaderOffset;
		}

		void DecryptMethodsOld(MDTable methodDefTable, ref DumpedMethods dumpedMethods) {
			dumpedMethods = new DumpedMethods();
			var decrypter = new Decrypter10(peImage, codeHeader.decryptionKey);
			for (uint rid = 1; rid <= methodDefTable.Rows; rid++) {
				var dm = new DumpedMethod();

				var method = (MethodDef)module.ResolveMethod(rid);
				if (method == null || method.DeclaringType == module.GlobalType)
					continue;

				peImage.ReadMethodTableRowTo(dm, rid);
				if (dm.mdRVA == 0)
					continue;
				uint bodyOffset = peImage.RvaToOffset(dm.mdRVA);

				var mbHeader = decrypter.Decrypt(bodyOffset, out dm.code, out dm.extraSections);
				peImage.UpdateMethodHeaderInfo(dm, mbHeader);

				dumpedMethods.Add(dm);
			}
		}

		void DecryptMethods(uint codeHeaderOffset, MDTable methodDefTable, ICsHeader csHeader, ref DumpedMethods dumpedMethods) {
			var methodInfos = csHeader.GetMethodInfos(codeHeaderOffset);
			csHeader.PatchMethodTable(methodDefTable, methodInfos);

			dumpedMethods = new DumpedMethods();
			decrypter = csHeader.CreateDecrypter();
			for (uint rid = 1; rid <= (uint)methodInfos.Count; rid++) {
				var methodInfo = methodInfos[(int)rid - 1];
				if (methodInfo.codeOffs == 0)
					continue;

				var dm = new DumpedMethod();
				peImage.ReadMethodTableRowTo(dm, rid);

				var mbHeader = decrypter.Decrypt(methodInfo, out dm.code, out dm.extraSections);
				peImage.UpdateMethodHeaderInfo(dm, mbHeader);

				dumpedMethods.Add(dm);
			}
		}

		void ReadCodeHeader(uint offset) {
			codeHeader.signature = peImage.OffsetReadBytes(offset, 16);
			codeHeader.decryptionKey = peImage.OffsetReadBytes(offset + 0x10, 16);
			codeHeader.totalCodeSize = peImage.OffsetReadUInt32(offset + 0x20);
			codeHeader.numMethods = peImage.OffsetReadUInt32(offset + 0x24);
			codeHeader.methodDefTableOffset = peImage.OffsetReadUInt32(offset + 0x28);
			codeHeader.methodDefElemSize = peImage.OffsetReadUInt32(offset + 0x2C);
		}

		static SigType GetSigType(byte[] sig) {
			if (Utils.Compare(sig, normalSignature))
				return SigType.Normal;
			else if (Utils.Compare(sig, proSignature))
				return SigType.Pro;
			else if (Utils.Compare(sig, oldSignature))
				return SigType.Old;
			return SigType.Unknown;
		}

		static bool IsValidSignature(byte[] signature) {
			return GetSigType(signature) != SigType.Unknown;
		}

		public static bool Detect(MyPEImage peImage) {
			try {
				uint codeHeaderOffset = GetCodeHeaderOffset(peImage);
				if (IsValidSignature(peImage.OffsetReadBytes(codeHeaderOffset, 16)))
					return true;
			}
			catch {
			}

			try {
				uint codeHeaderOffset = GetOldCodeHeaderOffset(peImage);
				if (codeHeaderOffset != 0 && IsValidSignature(peImage.OffsetReadBytes(codeHeaderOffset, 16)))
					return true;
			}
			catch {
			}

			return false;
		}
	}
}
