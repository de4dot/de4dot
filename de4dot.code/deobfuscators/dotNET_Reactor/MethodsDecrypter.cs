/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.MyStuff;
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class MethodsDecrypter {
		ModuleDefinition module;
		EncryptedResource encryptedResource;
		long xorKey;
		bool useXorKey;

		public bool Detected {
			get { return encryptedResource.ResourceDecrypterMethod != null; }
		}

		public MethodDefinition MethodsDecrypterMethod {
			get { return encryptedResource.ResourceDecrypterMethod; }
		}

		public EmbeddedResource MethodsResource {
			get { return encryptedResource.EncryptedDataResource; }
		}

		public MethodsDecrypter(ModuleDefinition module) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module);
		}

		public MethodsDecrypter(ModuleDefinition module, MethodsDecrypter oldOne) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module, oldOne.encryptedResource);
		}

		public void find() {
			var additionalTypes = new string[] {
//				"System.Diagnostics.StackFrame",	//TODO: Not in DNR <= 3.7.0.3
				"System.IntPtr",
//				"System.Reflection.Assembly",		//TODO: Not in unknown DNR version with jitter support
			};
			var checkedMethods = new Dictionary<MethodReferenceAndDeclaringTypeKey, bool>();
			var callCounter = new CallCounter();
			int typesLeft = 30;
			foreach (var type in module.GetTypes()) {
				var cctor = DotNetUtils.getMethod(type, ".cctor");
				if (cctor == null || cctor.Body == null)
					continue;
				if (typesLeft-- <= 0)
					break;

				foreach (var info in DotNetUtils.getCalledMethods(module, cctor)) {
					var method = info.Item2;
					var key = new MethodReferenceAndDeclaringTypeKey(method);
					if (!checkedMethods.ContainsKey(key)) {
						checkedMethods[key] = false;
						if (info.Item1.BaseType == null || info.Item1.BaseType.FullName != "System.Object")
							continue;
						if (!DotNetUtils.isMethod(method, "System.Void", "()"))
							continue;
						if (!encryptedResource.couldBeResourceDecrypter(method, additionalTypes))
							continue;
						checkedMethods[key] = true;
					}
					else if (!checkedMethods[key])
						continue;
					callCounter.add(method);
				}
			}

			encryptedResource.ResourceDecrypterMethod = (MethodDefinition)callCounter.most();
		}

		public bool decrypt(PE.PeImage peImage, ISimpleDeobfuscator simpleDeobfuscator, ref Dictionary<uint, DumpedMethod> dumpedMethods) {
			if (encryptedResource.ResourceDecrypterMethod == null)
				return false;

			encryptedResource.init(simpleDeobfuscator);
			initXorKey();
			var methodsData = encryptedResource.decrypt();

			ArrayFinder arrayFinder = new ArrayFinder(encryptedResource.ResourceDecrypterMethod);
			bool hooksJitter = arrayFinder.exists(new byte[] { (byte)'g', (byte)'e', (byte)'t', (byte)'J', (byte)'i', (byte)'t' });

			if (useXorKey) {
				// DNR 4.3, 4.4
				var stream = new MemoryStream(methodsData);
				var reader = new BinaryReader(stream);
				var writer = new BinaryWriter(stream);
				int count = methodsData.Length / 8;
				for (int i = 0; i < count; i++) {
					long val = reader.ReadInt64();
					val ^= xorKey;
					stream.Position -= 8;
					writer.Write(val);
				}
			}

			var methodsDataReader = new BinaryReader(new MemoryStream(methodsData));
			int patchCount = methodsDataReader.ReadInt32();
			int mode = methodsDataReader.ReadInt32();

			int tmp = methodsDataReader.ReadInt32();
			methodsDataReader.BaseStream.Position -= 4;
			if ((tmp & 0xFF000000) == 0x06000000) {
				// It's method token + rva. DNR 3.7.0.3 (and earlier?) - 3.9.0.1
				methodsDataReader.BaseStream.Position += 8L * patchCount;
				patchCount = methodsDataReader.ReadInt32();
				mode = methodsDataReader.ReadInt32();

				patchDwords(peImage, methodsDataReader, patchCount);
				while (methodsDataReader.BaseStream.Position < methodsData.Length - 1) {
					uint token = methodsDataReader.ReadUInt32();
					int numDwords = methodsDataReader.ReadInt32();
					patchDwords(peImage, methodsDataReader, numDwords / 2);
				}
			}
			else if (!hooksJitter || mode == 1) {
				// DNR 3.9.8.0, 4.0, 4.1, 4.2, 4.3, 4.4
				patchDwords(peImage, methodsDataReader, patchCount);
				while (methodsDataReader.BaseStream.Position < methodsData.Length - 1) {
					uint rva = methodsDataReader.ReadUInt32();
					uint token = methodsDataReader.ReadUInt32();	// token, unknown, or index
					int size = methodsDataReader.ReadInt32();
					if (size > 0)
						peImage.dotNetSafeWrite(rva, methodsDataReader.ReadBytes(size));
				}
			}
			else {
				// DNR (4.0-4.2?), 4.3, 4.4 (jitter is hooked)

				var metadataTables = peImage.Cor20Header.createMetadataTables();
				var methodDef = metadataTables.getMetadataType(PE.MetadataIndex.iMethodDef);
				var rvaToIndex = new Dictionary<uint, int>((int)methodDef.rows);
				uint offset = methodDef.fileOffset;
				for (int i = 0; i < methodDef.rows; i++) {
					uint rva = peImage.offsetReadUInt32(offset);
					offset += methodDef.totalSize;
					if (rva == 0)
						continue;

					if ((peImage.readByte(rva) & 3) == 2)
						rva++;
					else
						rva += (uint)(4 * (peImage.readByte(rva + 1) >> 4));
					rvaToIndex[rva] = i;
				}

				patchDwords(peImage, methodsDataReader, patchCount);
				int count = methodsDataReader.ReadInt32();
				dumpedMethods = new Dictionary<uint, DumpedMethod>();
				while (methodsDataReader.BaseStream.Position < methodsData.Length - 1) {
					uint rva = methodsDataReader.ReadUInt32();
					uint index = methodsDataReader.ReadUInt32();
					bool isNativeCode = index >= 0x70000000;
					int size = methodsDataReader.ReadInt32();
					var methodData = methodsDataReader.ReadBytes(size);

					int methodIndex;
					if (!rvaToIndex.TryGetValue(rva, out methodIndex)) {
						Log.w("Could not find method having code RVA {0:X8}", rva);
						continue;
					}

					if (isNativeCode) {
						//TODO: Convert to CIL code
						Log.w("Found native code. Ignoring it for now... Assembly won't run. token: {0:X8}", 0x06000001 + methodIndex);
					}
					else {
						var dm = new DumpedMethod();
						dm.token = (uint)(0x06000001 + methodIndex);
						dm.code = methodData;

						offset = methodDef.fileOffset + (uint)(methodIndex * methodDef.totalSize);
						rva = peImage.offsetReadUInt32(offset);
						dm.mdImplFlags = peImage.offsetReadUInt16(offset + (uint)methodDef.fields[1].offset);
						dm.mdFlags = peImage.offsetReadUInt16(offset + (uint)methodDef.fields[2].offset);
						dm.mdName = peImage.offsetRead(offset + (uint)methodDef.fields[3].offset, methodDef.fields[3].size);
						dm.mdSignature = peImage.offsetRead(offset + (uint)methodDef.fields[4].offset, methodDef.fields[4].size);
						dm.mdParamList = peImage.offsetRead(offset + (uint)methodDef.fields[5].offset, methodDef.fields[5].size);

						if ((peImage.readByte(rva) & 3) == 2) {
							dm.mhFlags = 2;
							dm.mhMaxStack = 8;
							dm.mhCodeSize = (uint)dm.code.Length;
							dm.mhLocalVarSigTok = 0;
						}
						else {
							dm.mhFlags = peImage.readUInt16(rva);
							dm.mhMaxStack = peImage.readUInt16(rva + 2);
							dm.mhCodeSize = (uint)dm.code.Length;
							dm.mhLocalVarSigTok = peImage.readUInt32(rva + 8);
						}

						dumpedMethods[dm.token] = dm;
					}
				}
			}

			return true;
		}

		static void patchDwords(PE.PeImage peImage, BinaryReader reader, int count) {
			for (int i = 0; i < count; i++) {
				uint rva = reader.ReadUInt32();
				uint data = reader.ReadUInt32();
				peImage.dotNetSafeWrite(rva, BitConverter.GetBytes(data));
			}
		}

		void initXorKey() {
			useXorKey = false;

			var instructions = encryptedResource.ResourceDecrypterMethod.Body.Instructions;
			for (int i = 0; i < instructions.Count - 1; i++) {
				if (instructions[i].OpCode.Code != Code.Ldind_I8)
					continue;
				var ldci4 = instructions[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;

				xorKey = DotNetUtils.getLdcI4Value(ldci4);
				useXorKey = true;
				return;
			}
		}
	}
}
