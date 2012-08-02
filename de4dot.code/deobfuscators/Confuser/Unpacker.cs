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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class EmbeddedAssemblyInfo {
		public readonly byte[] data;
		public readonly string asmFullName;
		public readonly string asmSimpleName;
		public readonly string extension;
		public readonly EmbeddedResource resource;

		public EmbeddedAssemblyInfo(EmbeddedResource resource, byte[] data, string asmFullName, string extension) {
			this.resource = resource;
			this.data = data;
			this.asmFullName = asmFullName;
			this.asmSimpleName = Utils.getAssemblySimpleName(asmFullName);
			this.extension = extension;
		}

		public override string ToString() {
			return asmFullName;
		}
	}

	class Unpacker {
		ModuleDefinition module;
		EmbeddedResource mainAsmResource;
		uint key0, key1;
		ConfuserVersion version = ConfuserVersion.Unknown;
		MethodDefinition asmResolverMethod;

		enum ConfuserVersion {
			Unknown,
			v10_r42915,
			v14_r58564,
			v14_r58802,
			v14_r58852,
			v15_r60785,
			v17_r73404,
		}

		public bool Detected {
			get { return mainAsmResource != null; }
		}

		public Unpacker(ModuleDefinition module) {
			this.module = module;
		}

		static string[] requiredFields = new string[] {
			 "System.String",
		};
		static string[] requiredEntryPointLocals = new string[] {
			"System.Byte[]",
			"System.Int32",
			"System.IO.BinaryReader",
			"System.IO.Stream",
		};
		public void find(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var entryPoint = module.EntryPoint;
			if (entryPoint == null)
				return;
			if (!new LocalTypes(entryPoint).all(requiredEntryPointLocals))
				return;
			var type = entryPoint.DeclaringType;
			if (!new FieldTypes(type).all(requiredFields))
				return;
			var decyptMethod = findDecryptMethod(type);
			if (decyptMethod == null)
				return;
			var decryptLocals = new LocalTypes(decyptMethod);
			if (decryptLocals.exists("System.IO.MemoryStream"))
				version = ConfuserVersion.v10_r42915;
			else
				version = ConfuserVersion.v14_r58564;

			var cctor = DotNetUtils.getMethod(type, ".cctor");
			if (cctor == null)
				return;

			if ((asmResolverMethod = findAssemblyResolverMethod(entryPoint.DeclaringType)) != null) {
				version = ConfuserVersion.v14_r58802;
				simpleDeobfuscator.deobfuscate(asmResolverMethod);
				if (!findKey1(asmResolverMethod, out key1))
					return;
			}

			switch (version) {
			case ConfuserVersion.v10_r42915:
				break;

			case ConfuserVersion.v14_r58564:
			case ConfuserVersion.v14_r58802:
				simpleDeobfuscator.deobfuscate(decyptMethod);
				if (findKey0_v14_r58564(decyptMethod, out key0))
					break;
				if (findKey0_v14_r58852(decyptMethod, out key0)) {
					if (!decryptLocals.exists("System.Security.Cryptography.RijndaelManaged")) {
						version = ConfuserVersion.v14_r58852;
						break;
					}
					if (isDecryptMethod_v17_r73404(decyptMethod))
						version = ConfuserVersion.v17_r73404;
					else
						version = ConfuserVersion.v15_r60785;
					break;
				}
				throw new ApplicationException("Could not find magic");

			default:
				throw new ApplicationException("Invalid version");
			}

			simpleDeobfuscator.deobfuscate(cctor);
			simpleDeobfuscator.decryptStrings(cctor, deob);

			mainAsmResource = findResource(cctor);
			if (mainAsmResource == null)
				throw new ApplicationException("Could not find main assembly resource");
		}

		static bool isDecryptMethod_v17_r73404(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			if (instrs.Count < 4)
				return false;
			if (!DotNetUtils.isLdarg(instrs[0]))
				return false;
			if (!isCallorNewobj(instrs[1]) && !isCallorNewobj(instrs[2]))
				return false;
			var stloc = instrs[3];
			if (!DotNetUtils.isStloc(stloc))
				return false;
			var local = DotNetUtils.getLocalVar(method.Body.Variables, stloc);
			if (local == null || local.VariableType.FullName != "System.IO.BinaryReader")
				return false;

			return true;
		}

		static bool isCallorNewobj(Instruction instr) {
			return instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Newobj;
		}

		static MethodDefinition findAssemblyResolverMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
					continue;

				return method;
			}
			return null;
		}

		static bool findKey0_v14_r58564(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				if (instrs[i].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			key = 0;
			return false;
		}

		static bool findKey0_v14_r58852(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 1]))
					continue;
				var ldci4_2 = instrs[i + 2];
				if (!DotNetUtils.isLdcI4(ldci4_2) && DotNetUtils.getLdcI4Value(ldci4_2) != 0)
					continue;
				if (!DotNetUtils.isStloc(instrs[i + 3]))
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4_1);
				return true;
			}
			key = 0;
			return false;
		}

		static bool findKey1(MethodDefinition method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				if (instrs[i].OpCode.Code != Code.Ldelem_U1)
					continue;
				var ldci4 = instrs[i + 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				if (!DotNetUtils.isLdloc(instrs[i + 3]))
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Xor)
					continue;

				key = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			key = 0;
			return false;
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		static string[] requiredDecryptLocals = new string[] {
			"System.Byte[]",
			"System.IO.Compression.DeflateStream",
		};
		static MethodDefinition findDecryptMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte[])"))
					continue;
				if (!new LocalTypes(method).all(requiredDecryptLocals))
					continue;

				return method;
			}
			return null;
		}

		public EmbeddedAssemblyInfo unpackMainAssembly() {
			if (mainAsmResource == null)
				return null;
			return createEmbeddedAssemblyInfo(mainAsmResource, decrypt(mainAsmResource));
		}

		public List<EmbeddedAssemblyInfo> getEmbeddedAssemblyInfos() {
			var infos = new List<EmbeddedAssemblyInfo>();
			foreach (var rsrc in module.Resources) {
				var resource = rsrc as EmbeddedResource;
				if (resource == null || resource == mainAsmResource)
					continue;
				try {
					infos.Add(createEmbeddedAssemblyInfo(resource, decrypt(resource)));
				}
				catch {
				}
			}
			return infos;
		}

		static EmbeddedAssemblyInfo createEmbeddedAssemblyInfo(EmbeddedResource resource, byte[] data) {
			var mod = ModuleDefinition.ReadModule(new MemoryStream(data));
			return new EmbeddedAssemblyInfo(resource, data, mod.Assembly.Name.FullName, DeobUtils.getExtension(mod.Kind));
		}

		byte[] decrypt(EmbeddedResource resource) {
			var data = resource.GetResourceData();
			switch (version) {
			case ConfuserVersion.v10_r42915: return decrypt_v10_r42915(data);
			case ConfuserVersion.v14_r58564: return decrypt_v14_r58564(data);
			case ConfuserVersion.v14_r58802: return decrypt_v14_r58564(data);
			case ConfuserVersion.v14_r58852: return decrypt_v14_r58852(data);
			case ConfuserVersion.v15_r60785: return decrypt_v15_r60785(data);
			case ConfuserVersion.v17_r73404: return decrypt_v17_r73404(data);
			default: throw new ApplicationException("Unknown version");
			}
		}

		byte[] decrypt_v10_r42915(byte[] data) {
			for (int i = 0; i < data.Length; i++)
				data[i] ^= (byte)(i ^ key0);
			return DeobUtils.inflate(data, true);
		}

		byte[] decrypt_v14_r58564(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(decrypt_v10_r42915(data)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		byte[] decrypt_v14_r58852(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(data, true)));
			data = reader.ReadBytes(reader.ReadInt32());
			for (int i = 0; i < data.Length; i++) {
				if ((i & 1) == 0)
					data[i] ^= (byte)((key0 & 0xF) - i);
				else
					data[i] ^= (byte)((key0 >> 4) + i);
				data[i] -= (byte)i;
			}
			return data;
		}

		byte[] decrypt_v15_r60785(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(data, true)));
			byte[] key, iv;
			data = decrypt_v15_r60785(reader, out key, out iv);
			reader = new BinaryReader(new MemoryStream(DeobUtils.aesDecrypt(data, key, iv)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		byte[] decrypt_v15_r60785(BinaryReader reader, out byte[] key, out byte[] iv) {
			var encrypted = reader.ReadBytes(reader.ReadInt32());
			iv = reader.ReadBytes(reader.ReadInt32());
			key = reader.ReadBytes(reader.ReadInt32());
			for (int i = 0; i < key.Length; i += 4) {
				key[i] ^= (byte)key0;
				key[i + 1] ^= (byte)(key0 >> 8);
				key[i + 2] ^= (byte)(key0 >> 16);
				key[i + 3] ^= (byte)(key0 >> 24);
			}
			return encrypted;
		}

		byte[] decrypt_v17_r73404(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(data));
			byte[] key, iv;
			data = decrypt_v15_r60785(reader, out key, out iv);
			reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(DeobUtils.aesDecrypt(data, key, iv), true)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		public void deobfuscate(Blocks blocks) {
			if (asmResolverMethod == null)
				return;
			if (blocks.Method != DotNetUtils.getModuleTypeCctor(module))
				return;
			ConfuserUtils.removeResourceHookCode(blocks, asmResolverMethod);
		}
	}
}
