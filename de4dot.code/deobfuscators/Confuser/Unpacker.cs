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
using Mono.Cecil.Metadata;
using de4dot.blocks;
using SevenZip.Compression.LZMA;

namespace de4dot.code.deobfuscators.Confuser {
	class RealAssemblyInfo {
		public AssemblyDefinition realAssembly;
		public uint entryPointToken;
		public ModuleKind kind;
		public string moduleName;

		public RealAssemblyInfo(AssemblyDefinition realAssembly, uint entryPointToken, ModuleKind kind) {
			this.realAssembly = realAssembly;
			this.entryPointToken = entryPointToken;
			this.kind = kind;
			this.moduleName = realAssembly.Name.Name + DeobUtils.getExtension(kind);
		}
	}

	class EmbeddedAssemblyInfo {
		public readonly byte[] data;
		public string asmFullName;
		public string asmSimpleName;
		public string extension;
		public ModuleKind kind;
		public readonly EmbeddedResource resource;
		public RealAssemblyInfo realAssemblyInfo;

		public EmbeddedAssemblyInfo(EmbeddedResource resource, byte[] data, string asmFullName, ModuleKind kind) {
			this.resource = resource;
			this.data = data;
			this.asmFullName = asmFullName;
			this.asmSimpleName = Utils.getAssemblySimpleName(asmFullName);
			this.kind = kind;
			this.extension = DeobUtils.getExtension(kind);
		}

		public override string ToString() {
			return asmFullName;
		}
	}

	class Unpacker {
		ModuleDefinition module;
		EmbeddedResource mainAsmResource;
		uint key0, key1;
		uint entryPointToken;
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
			v17_r73477,
			v17_r75076,
			v18_r75184,
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
			bool use7zip = type.NestedTypes.Count == 6;
			MethodDefinition decyptMethod;
			if (use7zip)
				decyptMethod = findDecryptMethod_7zip(type);
			else
				decyptMethod = findDecryptMethod_inflate(type);
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
					if (use7zip) {
						if (new LocalTypes(decyptMethod).exists("System.IO.MemoryStream"))
							version = ConfuserVersion.v17_r75076;
						else
							version = ConfuserVersion.v18_r75184;
					}
					else if (isDecryptMethod_v17_r73404(decyptMethod))
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

			if (findEntryPointToken(simpleDeobfuscator, cctor, entryPoint, out entryPointToken) && !use7zip)
				version = ConfuserVersion.v17_r73477;

			mainAsmResource = findResource(cctor);
			if (mainAsmResource == null)
				throw new ApplicationException("Could not find main assembly resource");
		}

		bool findEntryPointToken(ISimpleDeobfuscator simpleDeobfuscator, MethodDefinition cctor, MethodDefinition entryPoint, out uint token) {
			token = 0;
			ulong @base;
			if (!findBase(cctor, out @base))
				return false;

			var modPowMethod = DotNetUtils.getMethod(cctor.DeclaringType, "System.UInt64", "(System.UInt64,System.UInt64,System.UInt64)");
			if (modPowMethod == null)
				throw new ApplicationException("Could not find modPow()");

			simpleDeobfuscator.deobfuscate(entryPoint);
			ulong mod;
			if (!findMod(entryPoint, out mod))
				throw new ApplicationException("Could not find modulus");

			token = 0x06000000 | (uint)modPow(@base, 0x47, mod);
			if ((token >> 24) != 0x06)
				throw new ApplicationException("Illegal entry point token");
			return true;
		}

		static ulong modPow(ulong @base, ulong pow, ulong mod) {
			ulong m = 1;
			while (pow > 0) {
				if ((pow & 1) != 0)
					m = (m * @base) % mod;
				pow = pow >> 1;
				@base = (@base * @base) % mod;
			}
			return m;
		}

		static bool findMod(MethodDefinition method, out ulong mod) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci8 = instrs[i];
				if (ldci8.OpCode.Code != Code.Ldc_I8)
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as MethodReference;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.UInt64", "(System.UInt64,System.UInt64,System.UInt64)"))
					continue;

				mod = (ulong)(long)ldci8.Operand;
				return true;
			}
			mod = 0;
			return false;
		}

		static bool findBase(MethodDefinition method, out ulong @base) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci8 = instrs[i];
				if (ldci8.OpCode.Code != Code.Ldc_I8)
					continue;
				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDefinition;
				if (field == null || field.DeclaringType != method.DeclaringType)
					continue;
				if (field.FieldType.EType != ElementType.U8)
					continue;

				@base = (ulong)(long)ldci8.Operand;
				return true;
			}
			@base = 0;
			return false;
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

		static string[] requiredDecryptLocals_inflate = new string[] {
			"System.Byte[]",
			"System.IO.Compression.DeflateStream",
		};
		static MethodDefinition findDecryptMethod_inflate(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte[])"))
					continue;
				if (!new LocalTypes(method).all(requiredDecryptLocals_inflate))
					continue;

				return method;
			}
			return null;
		}

		static string[] requiredDecryptLocals_7zip = new string[] {
			"System.Byte[]",
			"System.Int64",
			"System.IO.BinaryReader",
			"System.Security.Cryptography.CryptoStream",
			"System.Security.Cryptography.RijndaelManaged",
		};
		static MethodDefinition findDecryptMethod_7zip(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Byte[]", "(System.Byte[])"))
					continue;
				if (!new LocalTypes(method).all(requiredDecryptLocals_7zip))
					continue;

				return method;
			}
			return null;
		}

		public EmbeddedAssemblyInfo unpackMainAssembly() {
			if (mainAsmResource == null)
				return null;
			var info = createEmbeddedAssemblyInfo(mainAsmResource, decrypt(mainAsmResource));

			var asm = module.Assembly;
			if (asm != null && entryPointToken != 0 && info.kind == ModuleKind.NetModule) {
				info.extension = DeobUtils.getExtension(module.Kind);
				info.kind = module.Kind;

				var realAsm = new AssemblyDefinition { Name = asm.Name };
				info.realAssemblyInfo = new RealAssemblyInfo(realAsm, entryPointToken, info.kind);
				info.asmFullName = realAsm.Name.FullName;
				info.asmSimpleName = realAsm.Name.Name;
			}

			return info;
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
			var asmFullName = mod.Assembly != null ? mod.Assembly.Name.FullName : mod.Name;
			return new EmbeddedAssemblyInfo(resource, data, asmFullName, mod.Kind);
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
			case ConfuserVersion.v17_r73477: return decrypt_v17_r73404(data);
			case ConfuserVersion.v17_r75076: return decrypt_v17_r75076(data);
			case ConfuserVersion.v18_r75184: return decrypt_v17_r75076(data);
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

		byte[] decrypt_v17_r75076(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(data));
			byte[] key, iv;
			data = decrypt_v15_r60785(reader, out key, out iv);
			return sevenzipDecompress(DeobUtils.aesDecrypt(data, key, iv));
		}

		static byte[] sevenzipDecompress(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(data));
			int totalSize = reader.ReadInt32();
			var props = reader.ReadBytes(5);
			var decoder = new Decoder();
			decoder.SetDecoderProperties(props);
			if ((long)totalSize != reader.ReadInt64())
				throw new ApplicationException("Invalid total size");
			long compressedSize = data.Length - props.Length - 8;
			var decompressed = new byte[totalSize];
			decoder.Code(reader.BaseStream, new MemoryStream(decompressed, true), compressedSize, totalSize, null);
			return decompressed;
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
