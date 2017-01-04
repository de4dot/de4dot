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
using dnlib.DotNet;
using dnlib.DotNet.MD;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class MethodsDecrypter {
		ModuleDefMD module;
		EncryptedResource encryptedResource;
		Dictionary<uint, byte[]> tokenToNativeMethod = new Dictionary<uint, byte[]>();
		Dictionary<MethodDef, byte[]> methodToNativeMethod = new Dictionary<MethodDef, byte[]>();
		List<MethodDef> validNativeMethods;
		int totalEncryptedNativeMethods = 0;
		long xorKey;

		public bool Detected {
			get { return encryptedResource.Method != null; }
		}

		public bool HasNativeMethods {
			get { return methodToNativeMethod.Count > 0; }
		}

		public TypeDef DecrypterType {
			get { return encryptedResource.Type; }
		}

		public MethodDef Method {
			get { return encryptedResource.Method; }
		}

		public EmbeddedResource Resource {
			get { return encryptedResource.Resource; }
		}

		public DnrDecrypterType DecrypterTypeVersion {
			get { return encryptedResource.GuessDecrypterType(); }
		}

		public MethodsDecrypter(ModuleDefMD module) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module);
		}

		public MethodsDecrypter(ModuleDefMD module, MethodsDecrypter oldOne) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module, oldOne.encryptedResource);
			this.tokenToNativeMethod = oldOne.tokenToNativeMethod;
			this.totalEncryptedNativeMethods = oldOne.totalEncryptedNativeMethods;
			this.xorKey = oldOne.xorKey;
		}

		public void Find() {
			var additionalTypes = new string[] {
				"System.IntPtr",
//				"System.Reflection.Assembly",		//TODO: Not in unknown DNR version with jitter support
			};
			var checkedMethods = new Dictionary<IMethod, bool>(MethodEqualityComparer.CompareDeclaringTypes);
			var callCounter = new CallCounter();
			int typesLeft = 30;
			foreach (var type in module.GetTypes()) {
				var cctor = type.FindStaticConstructor();
				if (cctor == null || cctor.Body == null)
					continue;
				if (typesLeft-- <= 0)
					break;

				foreach (var method in DotNetUtils.GetCalledMethods(module, cctor)) {
					if (!checkedMethods.ContainsKey(method)) {
						checkedMethods[method] = false;
						if (method.DeclaringType.BaseType == null || method.DeclaringType.BaseType.FullName != "System.Object")
							continue;
						if (!DotNetUtils.IsMethod(method, "System.Void", "()"))
							continue;
						if (!encryptedResource.CouldBeResourceDecrypter(method, additionalTypes))
							continue;
						checkedMethods[method] = true;
					}
					else if (!checkedMethods[method])
						continue;
					callCounter.Add(method);
				}
			}

			encryptedResource.Method = (MethodDef)callCounter.Most();
		}

		void XorEncrypt(byte[] data) {
			if (xorKey == 0)
				return;

			var stream = new MemoryStream(data);
			var reader = new BinaryReader(stream);
			var writer = new BinaryWriter(stream);
			int count = data.Length / 8;
			for (int i = 0; i < count; i++) {
				long val = reader.ReadInt64();
				val ^= xorKey;
				stream.Position -= 8;
				writer.Write(val);
			}
		}

		readonly static short[] nativeLdci4 = new short[] { 0x55, 0x8B, 0xEC, 0xB8, -1, -1, -1, -1, 0x5D, 0xC3 };
		readonly static short[] nativeLdci4_0 = new short[] { 0x55, 0x8B, 0xEC, 0x33, 0xC0, 0x5D, 0xC3 };
		public bool Decrypt(MyPEImage peImage, ISimpleDeobfuscator simpleDeobfuscator, ref DumpedMethods dumpedMethods, Dictionary<uint, byte[]> tokenToNativeCode, bool unpackedNativeFile) {
			if (encryptedResource.Method == null)
				return false;

			encryptedResource.Initialize(simpleDeobfuscator);
			if (!encryptedResource.FoundResource)
				return false;
			var methodsData = encryptedResource.Decrypt();

			bool hooksJitter = FindDnrCompileMethod(encryptedResource.Method.DeclaringType) != null;

			xorKey = GetXorKey();
			XorEncrypt(methodsData);

			var methodsDataReader = MemoryImageStream.Create(methodsData);
			int patchCount = methodsDataReader.ReadInt32();
			int mode = methodsDataReader.ReadInt32();

			int tmp = methodsDataReader.ReadInt32();
			methodsDataReader.Position -= 4;
			if ((tmp & 0xFF000000) == 0x06000000) {
				// It's method token + rva. DNR 3.7.0.3 (and earlier?) - 3.9.0.1
				methodsDataReader.Position += 8L * patchCount;
				patchCount = methodsDataReader.ReadInt32();
				mode = methodsDataReader.ReadInt32();

				PatchDwords(peImage, methodsDataReader, patchCount);
				while (methodsDataReader.Position < methodsData.Length - 1) {
					/*uint token =*/ methodsDataReader.ReadUInt32();
					int numDwords = methodsDataReader.ReadInt32();
					PatchDwords(peImage, methodsDataReader, numDwords / 2);
				}
			}
			else if (!hooksJitter || mode == 1) {
				// DNR 3.9.8.0, 4.0+

				PatchDwords(peImage, methodsDataReader, patchCount);
				bool oldCode = !IsNewer45Decryption(encryptedResource.Method);
				while (methodsDataReader.Position < methodsData.Length - 1) {
					uint rva = methodsDataReader.ReadUInt32();
					int size;
					if (oldCode) {
						methodsDataReader.ReadUInt32();	// token, unknown, or index
						size = methodsDataReader.ReadInt32();
					}
					else
						size = methodsDataReader.ReadInt32() * 4;

					var newData = methodsDataReader.ReadBytes(size);
					if (unpackedNativeFile)
						peImage.DotNetSafeWriteOffset(rva, newData);
					else
						peImage.DotNetSafeWrite(rva, newData);
				}
			}
			else {
				// DNR 4.0+ (jitter is hooked)

				var methodDef = peImage.MetaData.TablesStream.MethodTable;
				var rvaToIndex = new Dictionary<uint, int>((int)methodDef.Rows);
				uint offset = (uint)methodDef.StartOffset;
				for (int i = 0; i < methodDef.Rows; i++) {
					uint rva = peImage.OffsetReadUInt32(offset);
					offset += methodDef.RowSize;
					if (rva == 0)
						continue;

					if ((peImage.ReadByte(rva) & 3) == 2)
						rva++;
					else
						rva += (uint)(4 * (peImage.ReadByte(rva + 1) >> 4));
					rvaToIndex[rva] = i;
				}

				PatchDwords(peImage, methodsDataReader, patchCount);
				/*int count =*/ methodsDataReader.ReadInt32();
				dumpedMethods = new DumpedMethods();
				while (methodsDataReader.Position < methodsData.Length - 1) {
					uint rva = methodsDataReader.ReadUInt32();
					uint index = methodsDataReader.ReadUInt32();
					bool isNativeCode = index >= 0x70000000;
					int size = methodsDataReader.ReadInt32();
					var methodData = methodsDataReader.ReadBytes(size);

					int methodIndex;
					if (!rvaToIndex.TryGetValue(rva, out methodIndex)) {
						Logger.w("Could not find method having code RVA {0:X8}", rva);
						continue;
					}

					uint methodToken = 0x06000001 + (uint)methodIndex;

					if (isNativeCode) {
						totalEncryptedNativeMethods++;
						if (tokenToNativeCode != null)
							tokenToNativeCode[methodToken] = methodData;

						// Convert return true / false methods. The others are converted to
						// throw 0xDEADCODE.
						if (DeobUtils.IsCode(nativeLdci4, methodData)) {
							uint val = BitConverter.ToUInt32(methodData, 4);
							// ldc.i4 XXXXXXXXh / ret
							methodData = new byte[] { 0x20, 0, 0, 0, 0, 0x2A };
							methodData[1] = (byte)val;
							methodData[2] = (byte)(val >> 8);
							methodData[3] = (byte)(val >> 16);
							methodData[4] = (byte)(val >> 24);
						}
						else if (DeobUtils.IsCode(nativeLdci4_0, methodData)) {
							// ldc.i4.0 / ret
							methodData = new byte[] { 0x16, 0x2A };
						}
						else {
							tokenToNativeMethod[methodToken] = methodData;

							// ldc.i4 0xDEADCODE / conv.u4 / throw
							methodData = new byte[] { 0x20, 0xDE, 0xC0, 0xAD, 0xDE, 0x6D, 0x7A };
						}
					}

					var dm = new DumpedMethod();
					peImage.ReadMethodTableRowTo(dm, MDToken.ToRID(methodToken));
					dm.code = methodData;

					var codeReader = peImage.Reader;
					codeReader.Position = peImage.RvaToOffset(dm.mdRVA);
					byte[] code;
					var mbHeader = MethodBodyParser.ParseMethodBody(codeReader, out code, out dm.extraSections);
					peImage.UpdateMethodHeaderInfo(dm, mbHeader);

					dumpedMethods.Add(dm);
				}
			}

			return true;
		}

		public static bool IsNewer45Decryption(MethodDef method) {
			if (method == null || method.Body == null)
				return false;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				var ldci4 = instrs[i];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 4)
					continue;
				if (instrs[i + 1].OpCode.Code != Code.Mul)
					continue;
				ldci4 = instrs[i + 2];
				if (!ldci4.IsLdcI4() || ldci4.GetLdcI4Value() != 4)
					continue;
				if (instrs[i + 3].OpCode.Code != Code.Ldloca_S && instrs[i + 3].OpCode.Code != Code.Ldloca)
					continue;
				var call = instrs[i + 4];
				if (call.OpCode.Code != Code.Call)
					continue;
				if (!DotNetUtils.IsPinvokeMethod(call.Operand as MethodDef, "kernel32", "VirtualProtect"))
					continue;

				return true;
			}
			return false;
		}

		static void PatchDwords(MyPEImage peImage, IBinaryReader reader, int count) {
			for (int i = 0; i < count; i++) {
				uint rva = reader.ReadUInt32();
				uint data = reader.ReadUInt32();
				peImage.DotNetSafeWrite(rva, BitConverter.GetBytes(data));
			}
		}

		long GetXorKey() {
			var instructions = encryptedResource.Method.Body.Instructions;
			for (int i = 0; i < instructions.Count - 1; i++) {
				if (instructions[i].OpCode.Code != Code.Ldind_I8)
					continue;
				var ldci4 = instructions[i + 1];
				if (ldci4.IsLdcI4())
					return ldci4.GetLdcI4Value();
				if (ldci4.OpCode.Code == Code.Ldc_I8)
					return (long)ldci4.Operand;
			}
			return 0;
		}

		public void Reloaded() {
			foreach (var pair in tokenToNativeMethod) {
				int token = (int)pair.Key;
				var method = module.ResolveToken(token) as MethodDef;
				if (method == null)
					throw new ApplicationException(string.Format("Could not find method {0:X8}", token));
				methodToNativeMethod[method] = pair.Value;
			}
			tokenToNativeMethod = null;
		}

		public void PrepareEncryptNativeMethods(ModuleWriterBase moduleWriter) {
			if (methodToNativeMethod.Count == 0)
				return;

			validNativeMethods = new List<MethodDef>(methodToNativeMethod.Count);
			int len = 12;
			foreach (var kv in methodToNativeMethod) {
				if (kv.Key.DeclaringType == null)
					continue;	// Method was removed
				if (kv.Key.DeclaringType.Module != module)
					continue;	// method.DeclaringType was removed
				validNativeMethods.Add(kv.Key);
				len += 3 * 4 + kv.Value.Length;
			}
			if (validNativeMethods.Count == 0)
				return;

			len = (len & ~15) + 16;
			encryptedResource.Resource.Data = MemoryImageStream.Create(new byte[len]);
		}

		public void EncryptNativeMethods(ModuleWriterBase moduleWriter) {
			if (validNativeMethods == null || validNativeMethods.Count == 0)
				return;

			Logger.v("Encrypting native methods");

			var stream = new MemoryStream();
			var writer = new BinaryWriter(stream);
			writer.Write((uint)0);	// patch count
			writer.Write((uint)0);	// mode
			writer.Write(validNativeMethods.Count);

			int index = 0;
			foreach (var method in validNativeMethods) {
				var code = methodToNativeMethod[method];

				var mb = moduleWriter.MetaData.GetMethodBody(method);
				if (mb == null) {
					Logger.e("Could not find method body for method {0} ({1:X8})", method, method.MDToken.Raw);
					continue;
				}

				uint codeRva = (uint)mb.RVA;
				if (mb.IsTiny)
					codeRva++;
				else
					codeRva += (uint)(4 * (mb.Code[1] >> 4));

				Logger.v("Native method {0:X8}, code RVA {1:X8}", new MDToken(Table.Method, moduleWriter.MetaData.GetRid(method)).Raw, codeRva);

				writer.Write(codeRva);
				writer.Write(0x70000000 + index++);
				writer.Write(code.Length);
				writer.Write(code);
			}

			if (index != 0)
				Logger.n("Re-encrypted {0}/{1} native methods", index, totalEncryptedNativeMethods);

			var resourceChunk = moduleWriter.MetaData.GetChunk(encryptedResource.Resource);
			var resourceData = resourceChunk.Data;

			var encrypted = stream.ToArray();
			XorEncrypt(encrypted);

			encrypted = encryptedResource.Encrypt(encrypted);
			if (encrypted.Length != resourceData.Length)
				Logger.e("Encrypted native methods array is not same size as original array");
			Array.Copy(encrypted, resourceData, resourceData.Length);
		}

		enum CompileMethodType {
			Unknown,
			V1,	// <= DNR 4.5.0.0 (2012-11-06 <= endDate < 2013-01-31)
			V2,	// >= DNR 4.5.0.0 (2012-11-06 < startDate <= 2013-01-31)
		}

		public static MethodDef FindDnrCompileMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				var sig = method.MethodSig;
				if (sig == null || sig.Params.Count != 6)
					continue;
				if (GetCompileMethodType(method) == CompileMethodType.Unknown)
					continue;
				return method;
			}
			return null;
		}

		static CompileMethodType GetCompileMethodType(MethodDef method) {
			if (DotNetUtils.IsMethod(method, "System.UInt32", "(System.UInt64&,System.IntPtr,System.IntPtr,System.UInt32,System.IntPtr&,System.UInt32&)"))
				return CompileMethodType.V1;
			if (DotNetUtils.IsMethod(method, "System.UInt32", "(System.IntPtr,System.IntPtr,System.IntPtr,System.UInt32,System.IntPtr,System.UInt32&)"))
				return CompileMethodType.V2;
			return CompileMethodType.Unknown;
		}
	}
}
