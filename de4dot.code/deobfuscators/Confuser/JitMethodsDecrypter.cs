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
using System.Text;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class JitMethodsDecrypter : MethodsDecrypterBase, IStringDecrypter {
		MethodDef compileMethod;
		MethodDef hookConstructStr;
		MethodDataIndexes methodDataIndexes;
		ConfuserVersion version = ConfuserVersion.Unknown;

		enum ConfuserVersion {
			Unknown,
			v17_r73404,
			v17_r73430,
			v17_r73477,
			v17_r73479,
			v17_r74021,
			v18_r75257,
			v18_r75288,
			v18_r75291,
			v18_r75402,
			v19_r75725,
		}

		struct MethodDataIndexes {
			public int codeSize;
			public int maxStack;
			public int ehs;
			public int localVarSigTok;
			public int options;
		}

		public JitMethodsDecrypter(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator)
			: base(module, simpleDeobfuscator) {
		}

		public JitMethodsDecrypter(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, JitMethodsDecrypter other)
			: base(module, simpleDeobfuscator, other) {
			if (other != null)
				version = other.version;
		}

		protected override bool CheckType(TypeDef type, MethodDef initMethod) {
			if (type == null)
				return false;

			compileMethod = FindCompileMethod(type);
			if (compileMethod == null)
				return false;

			decryptMethod = FindDecryptMethod(type);
			if (decryptMethod == null)
				return false;

			var theVersion = ConfuserVersion.Unknown;
			switch (type.NestedTypes.Count) {
			case 35:
				if (type.Fields.Count == 9)
					theVersion = ConfuserVersion.v17_r73404;
				else if (type.Fields.Count == 10)
					theVersion = ConfuserVersion.v17_r73430;
				else
					return false;
				break;

			case 38:
				switch (CountInt32s(compileMethod, 0xFF)) {
				case 2: theVersion = ConfuserVersion.v17_r73477; break;
				case 4: theVersion = ConfuserVersion.v17_r73479; break;
				default: return false;
				}
				break;

			case 39:
				if (!DotNetUtils.CallsMethod(initMethod, "System.Void System.Console::WriteLine(System.Char)")) {
					if (DotNetUtils.CallsMethod(decryptMethod, "System.Security.Cryptography.Rijndael System.Security.Cryptography.Rijndael::Create()"))
						theVersion = ConfuserVersion.v17_r74021;
					else
						theVersion = ConfuserVersion.v18_r75291;
				}
				else if (DotNetUtils.CallsMethod(decryptMethod, "System.Security.Cryptography.Rijndael System.Security.Cryptography.Rijndael::Create()"))
					theVersion = ConfuserVersion.v18_r75257;
				else
					theVersion = ConfuserVersion.v18_r75288;
				break;

			case 27:
				if (DotNetUtils.CallsMethod(initMethod, "System.Int32 System.String::get_Length()"))
					theVersion = ConfuserVersion.v18_r75402;
				else
					theVersion = ConfuserVersion.v19_r75725;
				break;

			default:
				return false;
			}

			if (theVersion >= ConfuserVersion.v17_r73477) {
				hookConstructStr = FindHookConstructStr(type);
				if (hookConstructStr == null)
					return false;
			}

			version = theVersion;
			return true;
		}

		static int CountInt32s(MethodDef method, int val) {
			int count = 0;
			foreach (var instr in method.Body.Instructions) {
				if (!instr.IsLdcI4())
					continue;
				if (instr.GetLdcI4Value() == val)
					count++;
			}
			return count;
		}

		static MethodDef FindCompileMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				var sig = method.MethodSig;
				if (sig == null || sig.Params.Count != 6)
					continue;
				if (sig.RetType.GetElementType() != ElementType.U4)
					continue;
				if (sig.Params[0].GetElementType() != ElementType.I)
					continue;
				if (sig.Params[3].GetElementType() != ElementType.U4)
					continue;
				if (sig.Params[4].GetFullName() != "System.Byte**")
					continue;
				if (sig.Params[5].GetFullName() != "System.UInt32*")
					continue;

				return method;
			}
			return null;
		}

		static MethodDef FindHookConstructStr(TypeDef type) {
			foreach (var nested in type.NestedTypes) {
				if (nested.Fields.Count != 8 && nested.Fields.Count != 10)
					continue;
				foreach (var method in nested.Methods) {
					if (method.IsStatic || method.Body == null)
						continue;
					var sig = method.MethodSig;
					if (sig == null || sig.Params.Count != 4)
						continue;
					if (sig.Params[0].GetElementType() != ElementType.I)
						continue;
					if (sig.Params[1].GetElementType() != ElementType.I)
						continue;
					if (sig.Params[2].GetElementType() != ElementType.U4)
						continue;
					if (sig.Params[3].GetElementType() != ElementType.I && sig.Params[3].GetFullName() != "System.IntPtr&")
						continue;

					return method;
				}
			}
			return null;
		}

		public void Initialize() {
			if (initMethod == null)
				return;
			if (!InitializeKeys())
				throw new ApplicationException("Could not find all decryption keys");
			if (!InitializeMethodDataIndexes(compileMethod))
				throw new ApplicationException("Could not find MethodData indexes");
		}

		bool InitializeKeys() {
			switch (version) {
			case ConfuserVersion.v17_r73404: return InitializeKeys_v17_r73404();
			case ConfuserVersion.v17_r73430: return InitializeKeys_v17_r73404();
			case ConfuserVersion.v17_r73477: return InitializeKeys_v17_r73404();
			case ConfuserVersion.v17_r73479: return InitializeKeys_v17_r73404();
			case ConfuserVersion.v17_r74021: return InitializeKeys_v17_r73404();
			case ConfuserVersion.v18_r75257: return InitializeKeys_v17_r73404();
			case ConfuserVersion.v18_r75288: return InitializeKeys_v17_r73404();
			case ConfuserVersion.v18_r75291: return InitializeKeys_v17_r73404();
			case ConfuserVersion.v18_r75402: return InitializeKeys_v18_r75402();
			case ConfuserVersion.v19_r75725: return InitializeKeys_v18_r75402();
			default: throw new ApplicationException("Invalid version");
			}
		}

		bool InitializeKeys_v17_r73404() {
			simpleDeobfuscator.Deobfuscate(initMethod);
			if (!FindLKey0(initMethod, out lkey0))
				return false;
			if (!FindKey0_v16_r71742(initMethod, out key0))
				return false;
			if (!FindKey1(initMethod, out key1))
				return false;
			if (!FindKey2Key3(initMethod, out key2, out key3))
				return false;

			simpleDeobfuscator.Deobfuscate(decryptMethod);
			if (!FindKey6(decryptMethod, out key6))
				return false;

			return true;
		}

		bool InitializeKeys_v18_r75402() {
			simpleDeobfuscator.Deobfuscate(initMethod);
			if (!FindLKey0(initMethod, out lkey0))
				return false;
			if (!FindKey0_v16_r71742(initMethod, out key0))
				return false;
			if (!FindKey1(initMethod, out key1))
				return false;
			if (!FindKey2Key3(initMethod, out key2, out key3))
				return false;

			simpleDeobfuscator.Deobfuscate(compileMethod);
			if (!FindKey4(compileMethod, out key4))
				return false;

			simpleDeobfuscator.Deobfuscate(hookConstructStr);
			if (!FindKey5(hookConstructStr, out key5))
				return false;

			simpleDeobfuscator.Deobfuscate(decryptMethod);
			if (!FindKey6(decryptMethod, out key6))
				return false;

			return true;
		}

		static bool FindKey4(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = ConfuserUtils.FindCallMethod(instrs, index, Code.Call, "System.Void System.Runtime.InteropServices.Marshal::Copy(System.Byte[],System.Int32,System.IntPtr,System.Int32)");
				if (index < 0)
					break;
				if (index + 2 >= instrs.Count)
					continue;
				if (!instrs[index + 1].IsLdloc())
					continue;
				var ldci4 = instrs[index + 2];
				if (!ldci4.IsLdcI4())
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		static bool FindKey5(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i + 4 < instrs.Count; i++) {
				int index = i;
				var ldci4_8 = instrs[index++];
				if (!ldci4_8.IsLdcI4() || ldci4_8.GetLdcI4Value() != 8)
					continue;
				if (instrs[index++].OpCode.Code != Code.Shl)
					continue;
				if (instrs[index++].OpCode.Code != Code.Or)
					continue;
				var ldci4 = instrs[index++];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[index++].OpCode.Code != Code.Xor)
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}

			key = 0;
			return false;
		}

		bool InitializeMethodDataIndexes(MethodDef compileMethod) {
			switch (version) {
			case ConfuserVersion.v17_r73404: return true;
			case ConfuserVersion.v17_r73430: return true;
			case ConfuserVersion.v17_r73477: return InitializeMethodDataIndexes_v17_r73477(compileMethod);
			case ConfuserVersion.v17_r73479: return InitializeMethodDataIndexes_v17_r73477(compileMethod);
			case ConfuserVersion.v17_r74021: return InitializeMethodDataIndexes_v17_r73477(compileMethod);
			case ConfuserVersion.v18_r75257: return InitializeMethodDataIndexes_v17_r73477(compileMethod);
			case ConfuserVersion.v18_r75288: return InitializeMethodDataIndexes_v17_r73477(compileMethod);
			case ConfuserVersion.v18_r75291: return InitializeMethodDataIndexes_v17_r73477(compileMethod);
			case ConfuserVersion.v18_r75402: return InitializeMethodDataIndexes_v17_r73477(compileMethod);
			case ConfuserVersion.v19_r75725: return InitializeMethodDataIndexes_v17_r73477(compileMethod);
			default: throw new ApplicationException("Invalid version");
			}
		}

		bool InitializeMethodDataIndexes_v17_r73477(MethodDef method) {
			simpleDeobfuscator.Deobfuscate(method);
			var methodDataType = FindFirstThreeIndexes(method, out methodDataIndexes.maxStack, out methodDataIndexes.ehs, out methodDataIndexes.options);
			if (methodDataType == null)
				return false;

			if (!FindLocalVarSigTokIndex(method, methodDataType, out methodDataIndexes.localVarSigTok))
				return false;

			if (!FindCodeSizeIndex(method, methodDataType, out methodDataIndexes.codeSize))
				return false;

			return true;
		}

		static TypeDef FindFirstThreeIndexes(MethodDef method, out int maxStackIndex, out int ehsIndex, out int optionsIndex) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index1 = FindLdfldStind(instrs, i, false, true);
				if (index1 < 0)
					break;
				i = index1;

				int index2 = FindLdfldStind(instrs, index1 + 1, true, true);
				if (index2 < 0)
					continue;

				int index3 = FindLdfldStind(instrs, index2 + 1, true, false);
				if (index3 < 0)
					continue;

				var field1 = instrs[index1].Operand as FieldDef;
				var field2 = instrs[index2].Operand as FieldDef;
				var field3 = instrs[index3].Operand as FieldDef;
				if (field1 == null || field2 == null || field3 == null)
					continue;
				if (field1.DeclaringType != field2.DeclaringType || field1.DeclaringType != field3.DeclaringType)
					continue;

				maxStackIndex = GetInstanceFieldIndex(field1);
				ehsIndex = GetInstanceFieldIndex(field2);
				optionsIndex = GetInstanceFieldIndex(field3);
				return field1.DeclaringType;
			}

			maxStackIndex = -1;
			ehsIndex = -1;
			optionsIndex = -1;
			return null;
		}

		static bool FindLocalVarSigTokIndex(MethodDef method, TypeDef methodDataType, out int localVarSigTokIndex) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldfld = instrs[i];
				if (ldfld.OpCode.Code != Code.Ldfld)
					continue;
				var field = ldfld.Operand as FieldDef;
				if (field == null || field.DeclaringType != methodDataType)
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodDef;
				if (calledMethod == null || !calledMethod.IsStatic || calledMethod.DeclaringType != method.DeclaringType)
					continue;

				localVarSigTokIndex = GetInstanceFieldIndex(field);
				return true;
			}

			localVarSigTokIndex = -1;
			return false;
		}

		static bool FindCodeSizeIndex(MethodDef method, TypeDef methodDataType, out int codeSizeIndex) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldfld = instrs[i];
				if (ldfld.OpCode.Code != Code.Ldfld)
					continue;
				var field = ldfld.Operand as FieldDef;
				if (field == null || field.DeclaringType != methodDataType)
					continue;

				if (instrs[i+1].OpCode.Code != Code.Stfld)
					continue;

				codeSizeIndex = GetInstanceFieldIndex(field);
				return true;
			}

			codeSizeIndex = -1;
			return false;
		}

		static int GetInstanceFieldIndex(FieldDef field) {
			int i = 0;
			foreach (var f in field.DeclaringType.Fields) {
				if (f.IsStatic)
					continue;
				if (f == field)
					return i;
				i++;
			}
			throw new ApplicationException("Could not find field");
		}

		static int FindLdfldStind(IList<Instruction> instrs, int index, bool onlyInBlock, bool checkStindi4) {
			for (int i = index; i < instrs.Count - 1; i++) {
				var ldfld = instrs[i];
				if (onlyInBlock && ldfld.OpCode.FlowControl != FlowControl.Next)
					break;

				if (ldfld.OpCode.Code != Code.Ldfld)
					continue;

				var stindi4 = instrs[i + 1];
				if (checkStindi4 && stindi4.OpCode.Code != Code.Stind_I4)
					continue;

				return i;
			}
			return -1;
		}

		public bool Decrypt(MyPEImage peImage, byte[] fileData, ref DumpedMethods dumpedMethods) {
			if (initMethod == null)
				return false;

			switch (version) {
			case ConfuserVersion.v17_r73404: return Decrypt_v17_r73404(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v17_r73430: return Decrypt_v17_r73404(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v17_r73477: return Decrypt_v17_r73477(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v17_r73479: return Decrypt_v17_r73479(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v17_r74021: return Decrypt_v17_r73479(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v18_r75257: return Decrypt_v17_r73479(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v18_r75288: return Decrypt_v17_r73479(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v18_r75291: return Decrypt_v17_r73479(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v18_r75402: return Decrypt_v18_r75402(peImage, fileData, ref dumpedMethods);
			case ConfuserVersion.v19_r75725: return Decrypt_v18_r75402(peImage, fileData, ref dumpedMethods);
			default: throw new ApplicationException("Unknown version");
			}
		}

		bool Decrypt_v17_r73404(MyPEImage peImage, byte[] fileData, ref DumpedMethods dumpedMethods) {
			methodsData = DecryptMethodsData_v17_r73404(peImage);
			dumpedMethods = Decrypt_v17_r73404(peImage, fileData);
			return dumpedMethods != null;
		}

		DumpedMethods Decrypt_v17_r73404(MyPEImage peImage, byte[] fileData) {
			var dumpedMethods = new DumpedMethods();

			var methodDef = peImage.Metadata.TablesStream.MethodTable;
			for (uint rid = 1; rid <= methodDef.Rows; rid++) {
				var dm = new DumpedMethod();
				peImage.ReadMethodTableRowTo(dm, rid);

				if (dm.mdRVA == 0)
					continue;
				uint bodyOffset = peImage.RvaToOffset(dm.mdRVA);

				if (!IsEncryptedMethod(fileData, (int)bodyOffset))
					continue;

				int key = BitConverter.ToInt32(fileData, (int)bodyOffset + 6);
				int mdOffs = BitConverter.ToInt32(fileData, (int)bodyOffset + 2) ^ key;
				int len = BitConverter.ToInt32(fileData, (int)bodyOffset + 11) ^ ~key;
				var codeData = DecryptMethodData_v17_r73404(methodsData, mdOffs + 2, (uint)key, len);

				var reader = ByteArrayDataReaderFactory.CreateReader(codeData);
				var mbHeader = MethodBodyParser.ParseMethodBody(ref reader, out dm.code, out dm.extraSections);
				if (reader.Position != reader.Length)
					throw new ApplicationException("Invalid method data");

				peImage.UpdateMethodHeaderInfo(dm, mbHeader);

				dumpedMethods.Add(dm);
			}

			return dumpedMethods;
		}

		bool Decrypt_v17_r73477(MyPEImage peImage, byte[] fileData, ref DumpedMethods dumpedMethods) {
			methodsData = DecryptMethodsData_v17_r73404(peImage);
			dumpedMethods = Decrypt_v17_r73477(peImage, fileData);
			return dumpedMethods != null;
		}

		DumpedMethods Decrypt_v17_r73477(MyPEImage peImage, byte[] fileData) =>
			Decrypt(peImage, fileData, new DecryptMethodData_v17_r73477());

		bool Decrypt_v17_r73479(MyPEImage peImage, byte[] fileData, ref DumpedMethods dumpedMethods) {
			methodsData = DecryptMethodsData_v17_r73404(peImage);
			dumpedMethods = Decrypt_v17_r73479(peImage, fileData);
			return dumpedMethods != null;
		}

		DumpedMethods Decrypt_v17_r73479(MyPEImage peImage, byte[] fileData) =>
			Decrypt(peImage, fileData, new DecryptMethodData_v17_r73479());

		bool Decrypt_v18_r75402(MyPEImage peImage, byte[] fileData, ref DumpedMethods dumpedMethods) {
			if (peImage.OptionalHeader.CheckSum == 0)
				return false;
			methodsData = DecryptMethodsData_v17_r73404(peImage);
			dumpedMethods = Decrypt_v18_r75402(peImage, fileData);
			return dumpedMethods != null;
		}

		DumpedMethods Decrypt_v18_r75402(MyPEImage peImage, byte[] fileData) =>
			Decrypt(peImage, fileData, new DecryptMethodData_v18_r75402(this));

		abstract class DecryptMethodData {
			public abstract void Decrypt(byte[] fileData, int offset, uint k1, int size, out uint[] methodData, out byte[] codeData);
			public bool IsCodeFollowedByExtraSections(uint options) => (options >> 8) == 0;
		}

		class DecryptMethodData_v17_r73477 : DecryptMethodData {
			public override void Decrypt(byte[] fileData, int offset, uint k1, int size, out uint[] methodData, out byte[] codeData) {
				var data = new byte[size];
				Array.Copy(fileData, offset, data, 0, data.Length);
				var key = BitConverter.GetBytes(k1);
				for (int i = 0; i < data.Length; i++)
					data[i] ^= key[i & 3];

				methodData = new uint[5];
				Buffer.BlockCopy(data, 0, methodData, 0, 20);
				codeData = new byte[size - 20];
				Array.Copy(data, 20, codeData, 0, codeData.Length);
			}
		}

		class DecryptMethodData_v17_r73479 : DecryptMethodData {
			public override void Decrypt(byte[] fileData, int offset, uint k1, int size, out uint[] methodData, out byte[] codeData) {
				var data = new byte[size];
				Array.Copy(fileData, offset, data, 0, data.Length);
				uint k = k1;
				for (int i = 0; i < data.Length; i++) {
					data[i] ^= (byte)k;
					k = (k * data[i] + k1) % 0xFF;
				}

				methodData = new uint[5];
				Buffer.BlockCopy(data, 0, methodData, 0, 20);
				codeData = new byte[size - 20];
				Array.Copy(data, 20, codeData, 0, codeData.Length);
			}
		}

		class DecryptMethodData_v18_r75402 : DecryptMethodData {
			JitMethodsDecrypter jitDecrypter;

			public DecryptMethodData_v18_r75402(JitMethodsDecrypter jitDecrypter) => this.jitDecrypter = jitDecrypter;

			public override void Decrypt(byte[] fileData, int offset, uint k1, int size, out uint[] methodData, out byte[] codeData) {
				var data = new byte[size];
				Array.Copy(fileData, offset, data, 0, data.Length);
				uint k2 = jitDecrypter.key4 * k1;
				for (int i = 0; i < data.Length; i++) {
					data[i] ^= (byte)k2;
					k2 = (byte)((k2 * data[i] + k1) % 0xFF);
				}

				methodData = new uint[5];
				Buffer.BlockCopy(data, 0, methodData, 0, 20);
				codeData = new byte[size - 20];
				Array.Copy(data, 20, codeData, 0, codeData.Length);
			}
		}

		DumpedMethods Decrypt(MyPEImage peImage, byte[] fileData, DecryptMethodData decrypter) {
			var dumpedMethods = new DumpedMethods();

			var methodDef = peImage.Metadata.TablesStream.MethodTable;
			for (uint rid = 1; rid <= methodDef.Rows; rid++) {
				var dm = new DumpedMethod();
				peImage.ReadMethodTableRowTo(dm, rid);

				if (dm.mdRVA == 0)
					continue;
				uint bodyOffset = peImage.RvaToOffset(dm.mdRVA);

				if (!IsEncryptedMethod(fileData, (int)bodyOffset))
					continue;

				int key = BitConverter.ToInt32(fileData, (int)bodyOffset + 6);
				int mdOffs = BitConverter.ToInt32(fileData, (int)bodyOffset + 2) ^ key;
				int len = BitConverter.ToInt32(fileData, (int)bodyOffset + 11) ^ ~key;
				int methodDataOffset = mdOffs + 2;
				decrypter.Decrypt(methodsData, methodDataOffset, (uint)key, len, out var methodData, out var codeData);

				dm.mhFlags = 0x03;
				int maxStack = (int)methodData[methodDataIndexes.maxStack];
				dm.mhMaxStack = (ushort)maxStack;
				dm.mhLocalVarSigTok = methodData[methodDataIndexes.localVarSigTok];
				if (dm.mhLocalVarSigTok != 0 && (dm.mhLocalVarSigTok >> 24) != 0x11)
					throw new ApplicationException("Invalid local var sig token");
				int numExceptions = (int)methodData[methodDataIndexes.ehs];
				uint options = methodData[methodDataIndexes.options];
				int codeSize = (int)methodData[methodDataIndexes.codeSize];

				var codeDataReader = ByteArrayDataReaderFactory.CreateReader(codeData);
				if (decrypter.IsCodeFollowedByExtraSections(options)) {
					dm.code = codeDataReader.ReadBytes(codeSize);
					dm.extraSections = ReadExceptionHandlers(ref codeDataReader, numExceptions);
				}
				else {
					dm.extraSections = ReadExceptionHandlers(ref codeDataReader, numExceptions);
					dm.code = codeDataReader.ReadBytes(codeSize);
				}
				if (codeDataReader.Position != codeDataReader.Length)
					throw new ApplicationException("Invalid method data");
				if (dm.extraSections != null)
					dm.mhFlags |= 8;
				dm.mhCodeSize = (uint)dm.code.Length;

				// Figure out if the original method was tiny or not.
				bool isTiny = dm.code.Length <= 0x3F &&
							dm.mhLocalVarSigTok == 0 &&
							dm.extraSections == null &&
							dm.mhMaxStack == 8;
				if (isTiny)
					dm.mhFlags |= 0x10;	// Set 'init locals'
				dm.mhFlags |= (ushort)(options & 0x10);	// copy 'init locals' bit

				dumpedMethods.Add(dm);
			}

			return dumpedMethods;
		}

		static bool IsEncryptedMethod(byte[] fileData, int offset) =>
			fileData[offset] == 0x46 &&
			fileData[offset + 1] == 0x21 &&
			fileData[offset + 10] == 0x20 &&
			fileData[offset + 15] == 0x26;

		static byte[] ReadExceptionHandlers(ref DataReader reader, int numExceptions) {
			if (numExceptions == 0)
				return null;

			var memStream = new MemoryStream();
			var writer = new BinaryWriter(memStream);

			ulong header64 = (((ulong)numExceptions * 24) << 8) | 0x41;
			if (header64 > uint.MaxValue)
				throw new ApplicationException("Too many exception handlers...");
			writer.Write((uint)header64);
			for (int i = 0; i < numExceptions; i++) {
				writer.Write(reader.ReadUInt32());	// flags
				writer.Write(reader.ReadUInt32());	// try offset
				writer.Write(reader.ReadUInt32());	// try length
				writer.Write(reader.ReadUInt32());	// handler offset
				writer.Write(reader.ReadUInt32());	// handler length
				writer.Write(reader.ReadUInt32());	// catch token or filter offset
			}

			return memStream.ToArray();
		}

		byte[] DecryptMethodData_v17_r73404(byte[] fileData, int offset, uint k1, int size) {
			var data = new byte[size];
			var kbytes = BitConverter.GetBytes(k1);
			for (int i = 0; i < size; i++)
				data[i] = (byte)(fileData[offset + i] ^ kbytes[i & 3]);
			return data;
		}

		string IStringDecrypter.ReadUserString(uint token) {
			if ((token & 0xFF800000) != 0x70800000)
				return null;
			var reader = ByteArrayDataReaderFactory.CreateReader(methodsData);
			reader.Position = (token & ~0xFF800000) + 2;
			int len = reader.ReadInt32();
			if ((len & 1) != 1)
				throw new ApplicationException("Invalid string len");
			int chars = len / 2;
			var sb = new StringBuilder(chars);
			for (int i = 0; i < chars; i++)
				sb.Append((char)(reader.ReadUInt16() ^ key5));
			return sb.ToString();
		}

		public override bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v17_r73404:
				minRev = 73404;
				maxRev = 73404;
				return true;

			case ConfuserVersion.v17_r73430:
				minRev = 73430;
				maxRev = 73430;
				return true;

			case ConfuserVersion.v17_r73477:
				minRev = 73477;
				maxRev = 73477;
				return true;

			case ConfuserVersion.v17_r73479:
				minRev = 73479;
				maxRev = 73822;
				return true;

			case ConfuserVersion.v17_r74021:
				minRev = 74021;
				maxRev = 75184;
				return true;

			case ConfuserVersion.v18_r75257:
				minRev = 75257;
				maxRev = 75267;
				return true;

			case ConfuserVersion.v18_r75288:
				minRev = 75288;
				maxRev = 75288;
				return true;

			case ConfuserVersion.v18_r75291:
				minRev = 75291;
				maxRev = 75369;
				return true;

			case ConfuserVersion.v18_r75402:
				minRev = 75402;
				maxRev = 75720;
				return true;

			case ConfuserVersion.v19_r75725:
				minRev = 75725;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
