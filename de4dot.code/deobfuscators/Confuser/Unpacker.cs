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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;
using SevenZip.Compression.LZMA;

namespace de4dot.code.deobfuscators.Confuser {
	class RealAssemblyInfo {
		public AssemblyDef realAssembly;
		public uint entryPointToken;
		public ModuleKind kind;
		public string moduleName;

		public RealAssemblyInfo(AssemblyDef realAssembly, uint entryPointToken, ModuleKind kind) {
			this.realAssembly = realAssembly;
			this.entryPointToken = entryPointToken;
			this.kind = kind;
			this.moduleName = realAssembly.Name.String + DeobUtils.GetExtension(kind);
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
			this.asmSimpleName = Utils.GetAssemblySimpleName(asmFullName);
			this.kind = kind;
			this.extension = DeobUtils.GetExtension(kind);
		}

		public override string ToString() {
			return asmFullName;
		}
	}

	class Unpacker : IVersionProvider {
		ModuleDefMD module;
		EmbeddedResource mainAsmResource;
		uint key0/*, key1*/;
		uint entryPointToken;
		ConfuserVersion version = ConfuserVersion.Unknown;
		MethodDef asmResolverMethod;

		enum ConfuserVersion {
			Unknown,
			v10_r42915,
			v10_r48717,
			v14_r57778,
			v14_r58564,
			v14_r58802,
			v14_r58852,
			v15_r60785,
			v17_r73404,
			v17_r73477,
			v17_r73566,
			v17_r75076,
			v18_r75184,
			v18_r75367,
			v19_r77172,
		}

		public bool Detected {
			get { return mainAsmResource != null; }
		}

		public Unpacker(ModuleDefMD module, Unpacker other) {
			this.module = module;
			if (other != null)
				this.version = other.version;
		}

		static string[] requiredFields = new string[] {
			 "System.String",
		};
		static string[] requiredEntryPointLocals = new string[] {
			"System.Byte[]",
			"System.IO.BinaryReader",
			"System.IO.Stream",
		};
		public void Find(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var entryPoint = module.EntryPoint;
			if (entryPoint == null)
				return;
			if (!new LocalTypes(entryPoint).All(requiredEntryPointLocals))
				return;
			var type = entryPoint.DeclaringType;
			if (!new FieldTypes(type).All(requiredFields))
				return;

			bool use7zip = type.NestedTypes.Count == 6;
			MethodDef decyptMethod;
			if (use7zip)
				decyptMethod = FindDecryptMethod_7zip(type);
			else
				decyptMethod = FindDecryptMethod_inflate(type);
			if (decyptMethod == null)
				return;

			ConfuserVersion theVersion = ConfuserVersion.Unknown;
			var decryptLocals = new LocalTypes(decyptMethod);
			if (decryptLocals.Exists("System.IO.MemoryStream")) {
				if (DotNetUtils.CallsMethod(entryPoint, "System.Void", "(System.String,System.Byte[])"))
					theVersion = ConfuserVersion.v10_r42915;
				else if (DotNetUtils.CallsMethod(entryPoint, "System.Void", "(System.Security.Permissions.PermissionState)"))
					theVersion = ConfuserVersion.v10_r48717;
				else
					theVersion = ConfuserVersion.v14_r57778;
			}
			else
				theVersion = ConfuserVersion.v14_r58564;

			var cctor = type.FindStaticConstructor();
			if (cctor == null)
				return;

			if ((asmResolverMethod = FindAssemblyResolverMethod(entryPoint.DeclaringType)) != null) {
				theVersion = ConfuserVersion.v14_r58802;
				simpleDeobfuscator.Deobfuscate(asmResolverMethod);
				uint key1;
				if (!FindKey1(asmResolverMethod, out key1))
					return;
			}

			switch (theVersion) {
			case ConfuserVersion.v10_r42915:
			case ConfuserVersion.v10_r48717:
			case ConfuserVersion.v14_r57778:
				break;

			case ConfuserVersion.v14_r58564:
			case ConfuserVersion.v14_r58802:
				simpleDeobfuscator.Deobfuscate(decyptMethod);
				if (FindKey0_v14_r58564(decyptMethod, out key0))
					break;
				if (FindKey0_v14_r58852(decyptMethod, out key0)) {
					if (!decryptLocals.Exists("System.Security.Cryptography.RijndaelManaged")) {
						theVersion = ConfuserVersion.v14_r58852;
						break;
					}
					if (use7zip) {
						if (new LocalTypes(decyptMethod).Exists("System.IO.MemoryStream"))
							theVersion = ConfuserVersion.v17_r75076;
						else if (module.Name == "Stub.exe")
							theVersion = ConfuserVersion.v18_r75184;
						else if (!IsGetLenToPosStateMethodPrivate(type))
							theVersion = ConfuserVersion.v18_r75367;
						else
							theVersion = ConfuserVersion.v19_r77172;
					}
					else if (IsDecryptMethod_v17_r73404(decyptMethod))
						theVersion = ConfuserVersion.v17_r73404;
					else
						theVersion = ConfuserVersion.v15_r60785;
					break;
				}
				throw new ApplicationException("Could not find magic");

			default:
				throw new ApplicationException("Invalid version");
			}

			simpleDeobfuscator.Deobfuscate(cctor);
			simpleDeobfuscator.DecryptStrings(cctor, deob);

			if (FindEntryPointToken(simpleDeobfuscator, cctor, entryPoint, out entryPointToken) && !use7zip) {
				if (DotNetUtils.CallsMethod(asmResolverMethod, "System.Void", "(System.String)"))
					theVersion = ConfuserVersion.v17_r73477;
				else
					theVersion = ConfuserVersion.v17_r73566;
			}

			mainAsmResource = FindResource(cctor);
			if (mainAsmResource == null)
				throw new ApplicationException("Could not find main assembly resource");
			version = theVersion;
		}

		static bool IsGetLenToPosStateMethodPrivate(TypeDef type) {
			foreach (var m in type.Methods) {
				if (!DotNetUtils.IsMethod(m, "System.UInt32", "(System.UInt32)"))
					continue;
				return m.IsPrivate;
			}
			return false;
		}

		bool FindEntryPointToken(ISimpleDeobfuscator simpleDeobfuscator, MethodDef cctor, MethodDef entryPoint, out uint token) {
			token = 0;
			ulong @base;
			if (!FindBase(cctor, out @base))
				return false;

			var modPowMethod = DotNetUtils.GetMethod(cctor.DeclaringType, "System.UInt64", "(System.UInt64,System.UInt64,System.UInt64)");
			if (modPowMethod == null)
				throw new ApplicationException("Could not find modPow()");

			simpleDeobfuscator.Deobfuscate(entryPoint);
			ulong mod;
			if (!FindMod(entryPoint, out mod))
				throw new ApplicationException("Could not find modulus");

			token = 0x06000000 | (uint)ModPow(@base, 0x47, mod);
			if ((token >> 24) != 0x06)
				throw new ApplicationException("Illegal entry point token");
			return true;
		}

		static ulong ModPow(ulong @base, ulong pow, ulong mod) {
			ulong m = 1;
			while (pow > 0) {
				if ((pow & 1) != 0)
					m = (m * @base) % mod;
				pow = pow >> 1;
				@base = (@base * @base) % mod;
			}
			return m;
		}

		static bool FindMod(MethodDef method, out ulong mod) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldci8 = instrs[i];
				if (ldci8.OpCode.Code != Code.Ldc_I8)
					continue;

				var call = instrs[i + 1];
				if (call.OpCode.Code != Code.Call)
					continue;
				var calledMethod = call.Operand as IMethod;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.UInt64", "(System.UInt64,System.UInt64,System.UInt64)"))
					continue;

				mod = (ulong)(long)ldci8.Operand;
				return true;
			}
			mod = 0;
			return false;
		}

		static bool FindBase(MethodDef method, out ulong @base) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldci8 = instrs[i];
				if (ldci8.OpCode.Code != Code.Ldc_I8)
					continue;
				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;
				var field = stsfld.Operand as FieldDef;
				if (field == null || field.DeclaringType != method.DeclaringType)
					continue;
				if (field.FieldType.GetElementType() != ElementType.U8)
					continue;

				@base = (ulong)(long)ldci8.Operand;
				return true;
			}
			@base = 0;
			return false;
		}

		static bool IsDecryptMethod_v17_r73404(MethodDef method) {
			var instrs = method.Body.Instructions;
			if (instrs.Count < 4)
				return false;
			if (!instrs[0].IsLdarg())
				return false;
			if (!IsCallorNewobj(instrs[1]) && !IsCallorNewobj(instrs[2]))
				return false;
			var stloc = instrs[3];
			if (!stloc.IsStloc())
				return false;
			var local = stloc.GetLocal(method.Body.Variables);
			if (local == null || local.Type.FullName != "System.IO.BinaryReader")
				return false;

			return true;
		}

		static bool IsCallorNewobj(Instruction instr) {
			return instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Newobj;
		}

		static MethodDef FindAssemblyResolverMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
					continue;

				return method;
			}
			return null;
		}

		static bool FindKey0_v14_r58564(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				if (instrs[i].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			key = 0;
			return false;
		}

		static bool FindKey0_v14_r58852(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				var ldci4_1 = instrs[i];
				if (!ldci4_1.IsLdcI4())
					continue;
				if (!instrs[i + 1].IsStloc())
					continue;
				var ldci4_2 = instrs[i + 2];
				if (!ldci4_2.IsLdcI4() && ldci4_2.GetLdcI4Value() != 0)
					continue;
				if (!instrs[i + 3].IsStloc())
					continue;

				key = (uint)ldci4_1.GetLdcI4Value();
				return true;
			}
			key = 0;
			return false;
		}

		static bool FindKey1(MethodDef method, out uint key) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				if (instrs[i].OpCode.Code != Code.Ldelem_U1)
					continue;
				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (instrs[i + 2].OpCode.Code != Code.Xor)
					continue;
				if (!instrs[i + 3].IsLdloc())
					continue;
				if (instrs[i + 4].OpCode.Code != Code.Xor)
					continue;

				key = (uint)ldci4.GetLdcI4Value();
				return true;
			}
			key = 0;
			return false;
		}

		EmbeddedResource FindResource(MethodDef method) {
			return DotNetUtils.GetResource(module, DotNetUtils.GetCodeStrings(method)) as EmbeddedResource;
		}

		static string[] requiredDecryptLocals_inflate = new string[] {
			"System.Byte[]",
			"System.IO.Compression.DeflateStream",
		};
		static MethodDef FindDecryptMethod_inflate(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Byte[])"))
					continue;
				if (!new LocalTypes(method).All(requiredDecryptLocals_inflate))
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
		static MethodDef FindDecryptMethod_7zip(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Byte[])"))
					continue;
				if (!new LocalTypes(method).All(requiredDecryptLocals_7zip))
					continue;

				return method;
			}
			return null;
		}

		public EmbeddedAssemblyInfo UnpackMainAssembly(bool createAssembly) {
			if (mainAsmResource == null)
				return null;
			var info = CreateEmbeddedAssemblyInfo(mainAsmResource, Decrypt(mainAsmResource));

			var asm = module.Assembly;
			if (createAssembly && asm != null && entryPointToken != 0 && info.kind == ModuleKind.NetModule) {
				info.extension = DeobUtils.GetExtension(module.Kind);
				info.kind = module.Kind;

				var realAsm = module.UpdateRowId(new AssemblyDefUser(asm.Name, new Version(0, 0, 0, 0)));
				info.realAssemblyInfo = new RealAssemblyInfo(realAsm, entryPointToken, info.kind);
				if (module.Name != "Stub.exe")
					info.realAssemblyInfo.moduleName = module.Name.String;
				info.asmFullName = realAsm.FullName;
				info.asmSimpleName = realAsm.Name.String;
			}

			return info;
		}

		public List<EmbeddedAssemblyInfo> GetEmbeddedAssemblyInfos() {
			var infos = new List<EmbeddedAssemblyInfo>();
			foreach (var rsrc in module.Resources) {
				var resource = rsrc as EmbeddedResource;
				if (resource == null || resource == mainAsmResource)
					continue;
				try {
					infos.Add(CreateEmbeddedAssemblyInfo(resource, Decrypt(resource)));
				}
				catch {
				}
			}
			return infos;
		}

		static EmbeddedAssemblyInfo CreateEmbeddedAssemblyInfo(EmbeddedResource resource, byte[] data) {
			var mod = ModuleDefMD.Load(data);
			var asmFullName = mod.Assembly != null ? mod.Assembly.FullName : mod.Name.String;
			return new EmbeddedAssemblyInfo(resource, data, asmFullName, mod.Kind);
		}

		byte[] Decrypt(EmbeddedResource resource) {
			var data = resource.GetResourceData();
			switch (version) {
			case ConfuserVersion.v10_r42915: return Decrypt_v10_r42915(data);
			case ConfuserVersion.v10_r48717: return Decrypt_v10_r42915(data);
			case ConfuserVersion.v14_r57778: return Decrypt_v10_r42915(data);
			case ConfuserVersion.v14_r58564: return Decrypt_v14_r58564(data);
			case ConfuserVersion.v14_r58802: return Decrypt_v14_r58564(data);
			case ConfuserVersion.v14_r58852: return Decrypt_v14_r58852(data);
			case ConfuserVersion.v15_r60785: return Decrypt_v15_r60785(data);
			case ConfuserVersion.v17_r73404: return Decrypt_v17_r73404(data);
			case ConfuserVersion.v17_r73477: return Decrypt_v17_r73404(data);
			case ConfuserVersion.v17_r73566: return Decrypt_v17_r73404(data);
			case ConfuserVersion.v17_r75076: return Decrypt_v17_r75076(data);
			case ConfuserVersion.v18_r75184: return Decrypt_v17_r75076(data);
			case ConfuserVersion.v18_r75367: return Decrypt_v17_r75076(data);
			case ConfuserVersion.v19_r77172: return Decrypt_v17_r75076(data);
			default: throw new ApplicationException("Unknown version");
			}
		}

		byte[] Decrypt_v10_r42915(byte[] data) {
			for (int i = 0; i < data.Length; i++)
				data[i] ^= (byte)(i ^ key0);
			return DeobUtils.Inflate(data, true);
		}

		byte[] Decrypt_v14_r58564(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(Decrypt_v10_r42915(data)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		byte[] Decrypt_v14_r58852(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(DeobUtils.Inflate(data, true)));
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

		byte[] Decrypt_v15_r60785(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(DeobUtils.Inflate(data, true)));
			byte[] key, iv;
			data = Decrypt_v15_r60785(reader, out key, out iv);
			reader = new BinaryReader(new MemoryStream(DeobUtils.AesDecrypt(data, key, iv)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		byte[] Decrypt_v15_r60785(BinaryReader reader, out byte[] key, out byte[] iv) {
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

		byte[] Decrypt_v17_r73404(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(data));
			byte[] key, iv;
			data = Decrypt_v15_r60785(reader, out key, out iv);
			reader = new BinaryReader(new MemoryStream(DeobUtils.Inflate(DeobUtils.AesDecrypt(data, key, iv), true)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		byte[] Decrypt_v17_r75076(byte[] data) {
			var reader = new BinaryReader(new MemoryStream(data));
			byte[] key, iv;
			data = Decrypt_v15_r60785(reader, out key, out iv);
			return SevenZipDecompress(DeobUtils.AesDecrypt(data, key, iv));
		}

		static byte[] SevenZipDecompress(byte[] data) {
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

		public void Deobfuscate(Blocks blocks) {
			if (asmResolverMethod == null)
				return;
			if (blocks.Method != DotNetUtils.GetModuleTypeCctor(module))
				return;
			ConfuserUtils.RemoveResourceHookCode(blocks, asmResolverMethod);
		}

		public bool GetRevisionRange(out int minRev, out int maxRev) {
			switch (version) {
			case ConfuserVersion.Unknown:
				minRev = maxRev = 0;
				return false;

			case ConfuserVersion.v10_r42915:
				minRev = 42915;
				maxRev = 48509;
				return true;

			case ConfuserVersion.v10_r48717:
				minRev = 48717;
				maxRev = 57699;
				return true;

			case ConfuserVersion.v14_r57778:
				minRev = 57778;
				maxRev = 58446;
				return true;

			case ConfuserVersion.v14_r58564:
				minRev = 58564;
				maxRev = 58741;
				return true;

			case ConfuserVersion.v14_r58802:
				minRev = 58802;
				maxRev = 58817;
				return true;

			case ConfuserVersion.v14_r58852:
				minRev = 58852;
				maxRev = 60408;
				return true;

			case ConfuserVersion.v15_r60785:
				minRev = 60785;
				maxRev = 72989;
				return true;

			case ConfuserVersion.v17_r73404:
				minRev = 73404;
				maxRev = 73430;
				return true;

			case ConfuserVersion.v17_r73477:
				minRev = 73477;
				maxRev = 73479;
				return true;

			case ConfuserVersion.v17_r73566:
				minRev = 73566;
				maxRev = 75056;
				return true;

			case ConfuserVersion.v17_r75076:
				minRev = 75076;
				maxRev = 75158;
				return true;

			case ConfuserVersion.v18_r75184:
				minRev = 75184;
				maxRev = int.MaxValue;
				return true;

			case ConfuserVersion.v18_r75367:
				minRev = 75367;
				maxRev = 77124;
				return true;

			case ConfuserVersion.v19_r77172:
				minRev = 77172;
				maxRev = int.MaxValue;
				return true;

			default: throw new ApplicationException("Invalid version");
			}
		}
	}
}
